using UnityEngine;
using CreatureExperiment.Creature;

namespace CreatureExperiment.Creature.Brain
{
    /// <summary>
    /// Creature-relative Player behaviour sensor 0.2 - sibling to <see cref="PlayerObservation"/>, not
    /// a merge into it. <see cref="PlayerObservation"/> answers "how does this Player normally play?"
    /// (Creature-blind, still true with zero Creatures in the scene). This component answers a
    /// different question: "how does the Player behave WITH RESPECT TO the Creature - distance,
    /// gaze, approach/retreat?" Kept separate on purpose so the pure baseline sensor never grows a
    /// Creature dependency.
    ///
    /// It does NOT read any Creature AI/behaviour state - not <c>CreatureMovement._activeTarget</c>,
    /// not Wander/PlayerObserve/Probe/Attention/Pursuit state, nothing about what the Creature is
    /// currently doing or deciding. The only thing it touches on the Creature side is a Transform
    /// position (<see cref="creatureTransform"/> for distance/approach, <see cref="creatureViewTarget"/>
    /// for the gaze angle) - geometry, not behaviour. <c>CreaturePerception</c> is referenced ONLY as
    /// an anchor type to auto-find that Transform in <see cref="Awake"/> (a one-time lookup of "where
    /// is the Creature", nothing is ever read from it afterwards); assigning
    /// <see cref="creatureTransform"/> in the Inspector removes even that.
    ///
    /// Measurement only, still no meaning: this can say "the Player is N metres from the Creature",
    /// "the Player is looking at the Creature", "the Player is closing the gap" - never "curious",
    /// "afraid", "hostile". Nothing here changes Creature behaviour; nothing here is read by any
    /// Creature script yet.
    ///
    /// Gaze / exposure model: an "exposure" is one continuous stretch of
    /// <see cref="creatureTransform"/> being within <see cref="observationRange"/> of
    /// <see cref="playerRoot"/>. Re-entering range after leaving starts a BRAND NEW exposure -
    /// <see cref="GazeCount"/>, <see cref="TotalGazeTime"/>, <see cref="LongestGazeDuration"/> and
    /// <see cref="CurrentExposureDuration"/> all reset to zero. No exposure history is kept across
    /// that boundary (yet - a later "Reaction Episode" step is where that, and event-anchored gaze
    /// latency, belong). Within one exposure, a "gaze" is one continuous stretch of
    /// <see cref="IsLookingAtCreature"/> being true (hysteresis on <see cref="CreatureViewAngle"/> via
    /// <see cref="gazeEnterAngle"/>/<see cref="gazeExitAngle"/>, so camera jitter at the boundary does
    /// not start/stop many tiny gazes) - <see cref="GazeCount"/> increments only on the not-looking to
    /// looking edge, never per frame while already looking.
    ///
    /// <see cref="TowardCreatureSpeed"/> / <see cref="RelativeMoveState"/> are independent of range and
    /// of the exposure/gaze model - they are just "is the Player's own XZ velocity, computed the same
    /// simple frame-delta way <see cref="PlayerObservation"/> computes its own, currently pointed
    /// toward or away from the Creature". No approach/retreat COUNTS or durations yet - current value
    /// and current classification only; that summary belongs to a later step.
    ///
    /// Never throws for a missing Creature: every property degrades to a safe, clearly "no Creature"
    /// value (distance = <see cref="float.PositiveInfinity"/>, in-range/looking = false, angle = 180,
    /// toward-speed = 0, state = Neutral) instead of a NullReferenceException.
    ///
    /// Visual detection 0.3 - deliberately a SEPARATE concept from the angle-based gaze above, and
    /// deliberately NOT gated by <see cref="observationRange"/> (a Player can spot the Creature from
    /// well beyond it - discovery must work at any distance). <see cref="IsCreatureVisibleInFOV"/> asks
    /// "is the Creature literally on screen and not blocked", nothing about whether the Player is
    /// paying attention to it - <c>Visible != Looking</c>: a Creature at the edge of the screen is
    /// Visible but not Looking; dead-centre it can be both. Gate: <see cref="Camera.WorldToViewportPoint"/>
    /// inside [<see cref="viewportMargin"/>, 1-viewportMargin] on both axes (so a one-frame edge clip
    /// is not a detection), in front of the camera (viewport z &gt; 0), AND line-of-sight clear (a ray
    /// from the camera to <see cref="creatureViewTarget"/> hitting nothing that is not part of the
    /// Creature). <see cref="VisualAcquiredSerial"/> is a monotonic count (same idiom as
    /// <see cref="PlayerObservation.HighViewEpisodeSerial"/>) that increments once per sustained
    /// acquisition - armed/re-armed with its own hysteresis-in-time
    /// (<see cref="visualAcquireMinDuration"/> continuously visible to count;
    /// <see cref="visualRearmMinHiddenDuration"/> continuously hidden to allow the next one) so a
    /// flicker across the FOV edge cannot fire repeatedly. This is Creature-side-blind: it says only
    /// what the PLAYER's camera can see, never anything about whether the Creature itself perceives
    /// the Player - that is <c>CreaturePerception.IsPlayerPerceived</c>, read independently elsewhere
    /// (<see cref="ReactionEpisodeRecorder"/>), never here.
    /// </summary>
    public class CreatureEncounterObservation : MonoBehaviour
    {
        public enum RelativeMoveState { Neutral, Approach, Retreat }

        [Header("References")]
        [Tooltip("Player root whose world-space XZ position/velocity is measured. Empty = auto-found by the \"Player\" tag.")]
        [SerializeField] private Transform playerRoot;
        [Tooltip("Transform whose rotation/position IS the player's view (yaw + pitch combined) - the actual camera. Empty = Camera.main, else the first Camera found in children of playerRoot.")]
        [SerializeField] private Transform playerViewTransform;
        [Tooltip("The Creature's root transform, used for DistanceToCreature / TowardCreatureSpeed. Empty = auto-found via the first CreaturePerception in the scene (a one-time lookup of its Transform only - no Creature state is ever read from it).")]
        [SerializeField] private Transform creatureTransform;
        [Tooltip("The point CreatureViewAngle is measured to - a face/head point reads more naturally than the capsule root if one is wired (e.g. the Creature's HeadPivot). Empty = creatureTransform.")]
        [SerializeField] private Transform creatureViewTarget;
        [Tooltip("Optional cross-link to the sibling pure Player sensor, for a Brain (or a combined HUD) to read both through one place. Not consumed by this component's own calculations. Empty = auto-found in the scene.")]
        [SerializeField] private PlayerObservation playerObservation;

        [Header("Observation range")]
        [Tooltip("Flat (XZ) distance within which the Creature counts as \"in range\" for gaze/exposure purposes. DailyLife scale check: rooms run roughly 11-21 m, CreaturePerception's own perceptionRange is 16 m in this scene - kept below that so this measures a closer \"the Player would actually notice the Creature nearby\" band, not the Creature's full sensing radius.")]
        [SerializeField] private float observationRange = 10f;

        [Header("Gaze (degrees, hysteresis)")]
        [Tooltip("CreatureViewAngle at or below which a gaze STARTS (only while not already looking, and only while the Creature is in range).")]
        [SerializeField] private float gazeEnterAngle = 20f;
        [Tooltip("CreatureViewAngle at or above which an active gaze ENDS. Kept above gazeEnterAngle (hysteresis) so camera jitter at the boundary does not start/stop many tiny gazes.")]
        [SerializeField] private float gazeExitAngle = 30f;

        [Header("Movement")]
        [Tooltip("Same simple teleport/scene-reset guard PlayerObservation uses, computed independently here: an instantaneous player speed above this (m/s) is treated as a non-gameplay jump, not real movement.")]
        [SerializeField] private float teleportSpeedGuard = 20f;
        [Tooltip("TowardCreatureSpeed magnitude below which the Player counts as Neutral (neither approaching nor retreating) - absorbs small position jitter.")]
        [SerializeField] private float relativeMoveDeadZone = 0.15f;

        [Header("Visual detection (viewport + LOS - independent of observationRange)")]
        [Tooltip("Colliders that can block visual line of sight from the Player's camera to creatureViewTarget. Trigger colliders are always ignored regardless of this mask (SortingArea etc. never block sight). The Player's own body does not need excluding - the ray starts at the camera, inside/above the CharacterController capsule, and Unity never reports a hit against a collider the ray starts inside of.")]
        [SerializeField] private LayerMask visualOcclusionMask = ~0;
        [Tooltip("Viewport-space margin kept clear of all four screen edges (0..0.5). Stops a one-or-two-frame clip across the very edge of the screen from counting as a stable visual detection.")]
        [SerializeField] private float viewportMargin = 0.03f;
        [Tooltip("Seconds the Creature must be continuously Visible before ONE VisualAcquiredSerial increment fires.")]
        [SerializeField] private float visualAcquireMinDuration = 0.2f;
        [Tooltip("Seconds the Creature must be continuously NOT Visible after an acquisition before another acquisition can fire - stops re-triggering while it flickers in and out near the FOV/LOS boundary.")]
        [SerializeField] private float visualRearmMinHiddenDuration = 0.4f;

        private Camera _playerCamera;
        private bool _visualArmed = true;
        private float _visualHiddenTimer;

        private Vector3 _prevPlayerPos;
        private bool _initialized;

        private bool _wasInRange;

        private float _longestCompletedGazeDuration; // longest gaze that has already ENDED this exposure

        /// <summary>Optional cross-link to the sibling pure Player sensor (see class summary). May be null.</summary>
        public PlayerObservation PlayerObservation => playerObservation;

        /// <summary>Flat (XZ) distance from <see cref="playerRoot"/> to <see cref="creatureTransform"/>, metres. <see cref="float.PositiveInfinity"/> when there is no Creature reference.</summary>
        public float DistanceToCreature { get; private set; } = float.PositiveInfinity;

        /// <summary>True while <see cref="DistanceToCreature"/> is at or below <see cref="observationRange"/> (and a Creature reference exists).</summary>
        public bool IsCreatureInObservationRange { get; private set; }

        /// <summary>Angle, degrees, between the Player's view forward and the direction to <see cref="creatureViewTarget"/> (full 3D, not flattened - a literal "is the crosshair on the Creature" angle). 180 when there is no Creature/view reference.</summary>
        public float CreatureViewAngle { get; private set; } = 180f;

        /// <summary>True while an active gaze episode is in progress (see class summary for the hysteresis).</summary>
        public bool IsLookingAtCreature { get; private set; }

        /// <summary>Seconds the CURRENT gaze (if any) has lasted so far. 0 when not looking.</summary>
        public float CurrentGazeDuration { get; private set; }

        /// <summary>How many gazes have STARTED (not-looking -&gt; looking edges) within the current exposure.</summary>
        public int GazeCount { get; private set; }

        /// <summary>Total seconds spent looking at the Creature within the current exposure (summed across every gaze so far).</summary>
        public float TotalGazeTime { get; private set; }

        /// <summary>TotalGazeTime / CurrentExposureDuration, clamped 0..1. 0 while CurrentExposureDuration is ~0.</summary>
        public float GazeRatio { get; private set; }

        /// <summary>Longest single gaze within the current exposure - includes the in-progress gaze's current duration if it is already the longest, so it updates live (see class summary).</summary>
        public float LongestGazeDuration => Mathf.Max(_longestCompletedGazeDuration, IsLookingAtCreature ? CurrentGazeDuration : 0f);

        /// <summary>Seconds the Creature has been continuously within <see cref="observationRange"/> (the current exposure). 0 when out of range.</summary>
        public float CurrentExposureDuration { get; private set; }

        /// <summary>
        /// Player's own XZ velocity (frame-delta, same philosophy as <see cref="PlayerObservation.CurrentMoveSpeed"/>
        /// but signed/directional) dotted with the flat unit direction from Player to Creature. &gt;0 =
        /// closing the gap, &lt;0 = opening it, ~0 = no radial change (e.g. pure sideways movement). m/s.
        /// Computed whenever a Creature reference exists, independent of <see cref="observationRange"/>.
        /// </summary>
        public float TowardCreatureSpeed { get; private set; }

        /// <summary>Sign-classified <see cref="TowardCreatureSpeed"/> with <see cref="relativeMoveDeadZone"/> absorbing jitter.</summary>
        public RelativeMoveState CurrentRelativeMoveState { get; private set; } = RelativeMoveState.Neutral;

        /// <summary>True while the Creature is on screen (within viewportMargin of all four edges), in front of the camera, AND not blocked by line-of-sight. Independent of <see cref="observationRange"/> and of <see cref="IsLookingAtCreature"/> - see class summary.</summary>
        public bool IsCreatureVisibleInFOV { get; private set; }

        /// <summary>Distance from the exact screen centre (viewport (0.5, 0.5)) to the Creature's viewport position. 0 = dead centre; ~0.707 = exactly at a screen corner; larger = off-screen/behind (only meaningful while <see cref="IsCreatureVisibleInFOV"/> is true - see Report).</summary>
        public float ScreenCenterOffset { get; private set; } = 1f;

        /// <summary>Seconds <see cref="IsCreatureVisibleInFOV"/> has been continuously true. Resets to 0 the instant it goes false.</summary>
        public float CurrentVisualExposureDuration { get; private set; }

        /// <summary>Monotonically increasing count of every sustained visual acquisition (never decreases) - see class summary for the armed/re-armed hysteresis. Compare a snapshot against this to detect a NEW acquisition edge, the same idiom as <see cref="PlayerObservation.HighViewEpisodeSerial"/>.</summary>
        public int VisualAcquiredSerial { get; private set; }

        private void Awake()
        {
            if (playerRoot == null)
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null)
                    playerRoot = tagged.transform;
            }

            if (playerViewTransform == null)
            {
                var cam = Camera.main;
                if (cam == null && playerRoot != null)
                    cam = playerRoot.GetComponentInChildren<Camera>();
                if (cam != null)
                    playerViewTransform = cam.transform;
            }

            if (creatureTransform == null)
            {
                // One-time lookup of WHERE the Creature is. Nothing is read from CreaturePerception
                // beyond its Transform - see the class summary.
                var perception = FindFirstObjectByType<CreaturePerception>();
                if (perception != null)
                    creatureTransform = perception.transform;
            }

            if (creatureViewTarget == null)
                creatureViewTarget = creatureTransform;

            if (playerObservation == null)
            {
                playerObservation = GetComponent<PlayerObservation>();
                if (playerObservation == null)
                    playerObservation = FindFirstObjectByType<PlayerObservation>();
            }

            if (playerViewTransform != null)
                _playerCamera = playerViewTransform.GetComponent<Camera>();
        }

        // LateUpdate, not Update - same reasoning as PlayerObservation: everything that could move the
        // Player this frame (PlayerMovement / PlayerLook) is Update(), so LateUpdate always reads the
        // frame's settled result. The Creature's own movers are also Update() (CreatureWallCollision's
        // final depenetration is LateUpdate at order 200, after this component's default order 0, so
        // creatureTransform can be up to one frame behind a same-frame wall correction) - a deliberately
        // accepted, negligible simplification rather than adding execution-order machinery for this.
        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            float now = Time.time;

            UpdatePlayerVelocity(dt);
            UpdateDistanceAndRange(dt);
            UpdateGaze(dt);
            UpdateRelativeMovement();
            UpdateVisualDetection(dt);
        }

        private Vector3 _playerVelocityXZ;

        private void UpdatePlayerVelocity(float dt)
        {
            if (playerRoot == null)
            {
                _playerVelocityXZ = Vector3.zero;
                return;
            }

            Vector3 pos = playerRoot.position;

            if (!_initialized)
            {
                _prevPlayerPos = pos;
                _initialized = true;
                _playerVelocityXZ = Vector3.zero;
                return;
            }

            Vector3 flatDelta = pos - _prevPlayerPos;
            flatDelta.y = 0f;
            _prevPlayerPos = pos;

            Vector3 rawVelocity = dt > 0f ? flatDelta / dt : Vector3.zero;
            _playerVelocityXZ = rawVelocity.magnitude > teleportSpeedGuard ? Vector3.zero : rawVelocity;
        }

        private void UpdateDistanceAndRange(float dt)
        {
            if (playerRoot == null || creatureTransform == null)
            {
                DistanceToCreature = float.PositiveInfinity;
                IsCreatureInObservationRange = false;
                _wasInRange = false;
                CurrentExposureDuration = 0f;
                return;
            }

            Vector3 delta = creatureTransform.position - playerRoot.position;
            delta.y = 0f;
            DistanceToCreature = delta.magnitude;

            bool inRangeNow = DistanceToCreature <= observationRange;
            IsCreatureInObservationRange = inRangeNow;

            if (inRangeNow && !_wasInRange)
                BeginExposure();
            else if (!inRangeNow && _wasInRange)
                EndExposure();

            if (inRangeNow)
                CurrentExposureDuration += dt;

            _wasInRange = inRangeNow;
        }

        private void BeginExposure()
        {
            // A brand new exposure - see class summary: no history carried across this boundary.
            GazeCount = 0;
            TotalGazeTime = 0f;
            _longestCompletedGazeDuration = 0f;
            CurrentExposureDuration = 0f;
            // IsLookingAtCreature / CurrentGazeDuration are left to UpdateGaze() this same frame - a
            // gaze can legitimately start the instant the Creature comes into range.
        }

        private void EndExposure()
        {
            // Range lost - fold any in-progress gaze into the longest-duration tally (via EndGaze, the
            // same path a normal angle-hysteresis exit uses) and stop looking. The exposure summary
            // numbers otherwise simply hold their last value until BeginExposure() resets them on the
            // next entry (see class summary).
            EndGaze();
        }

        private void UpdateGaze(float dt)
        {
            if (playerViewTransform == null || creatureViewTarget == null)
            {
                CreatureViewAngle = 180f;
                if (IsLookingAtCreature)
                    EndGaze();
                return;
            }

            Vector3 toTarget = creatureViewTarget.position - playerViewTransform.position;
            CreatureViewAngle = toTarget.sqrMagnitude > 1e-6f
                ? Vector3.Angle(playerViewTransform.forward, toTarget)
                : 0f;

            if (!IsCreatureInObservationRange)
            {
                if (IsLookingAtCreature)
                    EndGaze();
                return;
            }

            if (!IsLookingAtCreature)
            {
                if (CreatureViewAngle <= gazeEnterAngle)
                {
                    IsLookingAtCreature = true;
                    CurrentGazeDuration = 0f;
                    GazeCount++; // only on the not-looking -> looking edge
                }
            }
            else
            {
                CurrentGazeDuration += dt;
                TotalGazeTime += dt;

                if (CreatureViewAngle >= gazeExitAngle)
                    EndGaze();
            }

            GazeRatio = CurrentExposureDuration > 0.01f ? Mathf.Clamp01(TotalGazeTime / CurrentExposureDuration) : 0f;
        }

        private void EndGaze()
        {
            if (CurrentGazeDuration > _longestCompletedGazeDuration)
                _longestCompletedGazeDuration = CurrentGazeDuration;
            IsLookingAtCreature = false;
            CurrentGazeDuration = 0f;
        }

        private void UpdateRelativeMovement()
        {
            if (playerRoot == null || creatureTransform == null)
            {
                TowardCreatureSpeed = 0f;
                CurrentRelativeMoveState = RelativeMoveState.Neutral;
                return;
            }

            Vector3 toCreature = creatureTransform.position - playerRoot.position;
            toCreature.y = 0f;
            Vector3 toCreatureDir = toCreature.sqrMagnitude > 1e-6f ? toCreature.normalized : Vector3.zero;

            TowardCreatureSpeed = Vector3.Dot(_playerVelocityXZ, toCreatureDir);

            CurrentRelativeMoveState =
                TowardCreatureSpeed > relativeMoveDeadZone ? RelativeMoveState.Approach :
                TowardCreatureSpeed < -relativeMoveDeadZone ? RelativeMoveState.Retreat :
                RelativeMoveState.Neutral;
        }

        // Player-side visual acquisition: viewport + front-of-camera + LOS, then a min-duration /
        // re-arm hysteresis so VisualAcquiredSerial fires once per sustained look, not once per frame
        // or once per flicker across the FOV/LOS boundary. Deliberately does not read
        // IsCreatureInObservationRange - discovery must work beyond it (see class summary).
        private void UpdateVisualDetection(float dt)
        {
            bool visible = ComputeVisible(out float centerOffset);
            IsCreatureVisibleInFOV = visible;
            ScreenCenterOffset = centerOffset;

            CurrentVisualExposureDuration = visible ? CurrentVisualExposureDuration + dt : 0f;

            if (_visualArmed && visible && CurrentVisualExposureDuration >= visualAcquireMinDuration)
            {
                VisualAcquiredSerial++;
                _visualArmed = false;
                _visualHiddenTimer = 0f;
            }

            if (!_visualArmed)
            {
                if (visible)
                {
                    _visualHiddenTimer = 0f;
                }
                else
                {
                    _visualHiddenTimer += dt;
                    if (_visualHiddenTimer >= visualRearmMinHiddenDuration)
                        _visualArmed = true;
                }
            }
        }

        // Range-independent viewport + LOS test. centerOffset is always computed from the raw viewport
        // position (even when the result is false) so it stays available for debugging, but is only
        // meaningful while the return value is true.
        private bool ComputeVisible(out float centerOffset)
        {
            centerOffset = 1f;

            if (_playerCamera == null || playerViewTransform == null || creatureViewTarget == null)
                return false;

            Vector3 vp = _playerCamera.WorldToViewportPoint(creatureViewTarget.position);
            centerOffset = new Vector2(vp.x - 0.5f, vp.y - 0.5f).magnitude;

            if (vp.z <= 0f) // behind the camera
                return false;

            float m = viewportMargin;
            if (vp.x < m || vp.x > 1f - m || vp.y < m || vp.y > 1f - m)
                return false;

            Vector3 eye = playerViewTransform.position;
            Vector3 toTarget = creatureViewTarget.position - eye;
            float dist = toTarget.magnitude;
            if (dist > 1e-3f &&
                Physics.Raycast(eye, toTarget / dist, out RaycastHit hit, dist, visualOcclusionMask, QueryTriggerInteraction.Ignore))
            {
                bool hitIsCreature = creatureTransform != null &&
                    (hit.collider.transform == creatureTransform || hit.collider.transform.IsChildOf(creatureTransform));
                if (!hitIsCreature)
                    return false; // something else (a wall, geometry) is in the way
            }

            return true;
        }

        private void OnValidate()
        {
            observationRange = Mathf.Max(0.1f, observationRange);
            gazeEnterAngle = Mathf.Clamp(gazeEnterAngle, 0.1f, 180f);
            gazeExitAngle = Mathf.Clamp(gazeExitAngle, gazeEnterAngle, 180f);
            teleportSpeedGuard = Mathf.Max(0.1f, teleportSpeedGuard);
            relativeMoveDeadZone = Mathf.Max(0f, relativeMoveDeadZone);

            viewportMargin = Mathf.Clamp(viewportMargin, 0f, 0.49f);
            visualAcquireMinDuration = Mathf.Max(0f, visualAcquireMinDuration);
            visualRearmMinHiddenDuration = Mathf.Max(0f, visualRearmMinHiddenDuration);
        }
    }
}
