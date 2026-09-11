using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// The creature's first perception -> gaze loop.
    ///
    /// Perception: is the player within <see cref="perceptionRange"/> (flat XZ distance)?
    /// Attention (0.1): pick the nearest <see cref="Interactable"/> the creature can actually SEE
    /// right now (flat XZ distance, over the range + FOV + LOS subset) and gaze at its live position.
    /// Only when NO perceivable Interactable exists does the player become the gaze target (a
    /// range-only fallback). This removes the earlier asymmetry where the player - being range-only,
    /// no FOV / LOS - kept winning gaze off an Interactable that had merely drifted off-axis for a
    /// frame. It is NOT the final attention design: still distance only among Interactables, no
    /// motion bias, no memory, no per-type weighting, no switch cooldown, no score. Nothing
    /// perceivable and no player in range -> rest gaze. <see cref="_forcePlayerGaze"/> (Probe) still
    /// overrides all of this.
    ///
    /// Object visibility (0.2): an <see cref="Interactable"/> is a candidate only if it is within
    /// <see cref="perceptionRange"/>, inside the <see cref="fovAngle"/> cone measured from the
    /// creature's gaze direction (<see cref="facingReference"/> forward, flattened to XZ - see the
    /// anchor note below), AND has a clear line of sight - one ray from the eye to the object's
    /// origin that hits nothing on <see cref="objectOcclusionMask"/> except that object itself. An
    /// object that leaves the cone or goes behind a wall simply drops out of the candidate set that
    /// frame; there is no memory of having seen it (Known Object / permanence is a later step). This
    /// gate is applied ONLY to Interactables - player perception (<see cref="IsPlayerPerceived"/> and
    /// the player as a gaze candidate) is unchanged and still a pure omnidirectional distance test.
    ///
    /// FOV anchor = the gaze, NOT the body: <see cref="facingReference"/> is wired to
    /// <see cref="lookPivot"/>, which this component re-aims every frame toward the current gaze
    /// target (a visible object, else the player, else rest-forward) - fast, in 3D, and completely
    /// independent of whether the creature is translating. Anchoring the cone to BodyVisual instead
    /// self-locks: BodyVisual is only yawed by <c>CreatureBodyExpression</c> WHILE the creature is
    /// moving, and <c>CreatureMovement</c> only moves (Approach / Inspect-slide) when it already has
    /// an attended object - so the moment the creature stops with an empty cone (very easy right
    /// after a Retreat, whose delta points BodyVisual straight away from the player, and on every
    /// Inspect slide, which faces the body tangent to the ring so the watched object sits ~90 deg
    /// off-axis) nothing can ever swing the cone back onto an object, and it never re-acquires.
    /// The gaze anchor tracks the object through the whole Inspect cycle and, when the creature has
    /// lost every object and is staring at the player, keeps the cone pointed where the player is -
    /// so an object the player brings toward the creature re-enters range + FOV + LOS on its own.
    /// Gaze direction: <see cref="lookPivot"/> eases toward the chosen target (yaw + pitch)
    /// and back to its start forward when there is none. It carries nothing visible; it is
    /// just the smoothed "where the creature wants to look" vector.
    /// Expression: the <see cref="headPivot"/> turns the whole face toward that direction (yaw
    /// unlimited - this is not a human neck; pitch clamped only so the face stays off the capsule
    /// body), and a small <see cref="pupil"/> then slides inside the eye toward the residual
    /// direction the head has not yet covered. Body rotation is still not part of this.
    ///
    /// Gaze interruption (0.1) - the FIRST split of gaze target from behavioural target. While an
    /// object is the attention target (<see cref="AttendedInteractable"/> != null) and a player who
    /// is in range AND inside the current FOV cone moves faster than
    /// <see cref="playerGazeMotionThreshold"/>, the creature GLANCES at that player for
    /// <see cref="playerGazeInterruptDuration"/> s, then looks back at the object; another glance
    /// cannot start for <see cref="playerGazeInterruptCooldown"/> s, so a continuously-moving player
    /// gets an occasional flick, never a locked stare or a follow. This changes ONLY the gaze /
    /// head / pupil aim: <see cref="AttendedInteractable"/>, <c>CreatureMovement._activeTarget</c>,
    /// Approach, Inspect, Pickup dwell, the physical probe, the approach dash, lost-target state and
    /// Nav movement are all untouched, and the attended object is FOV-exempted in
    /// <see cref="SelectGazeTarget"/> for the glance so moving the gaze off it can never make
    /// selection drop it. <see cref="_forcePlayerGaze"/> (Probe) still outranks it.
    ///
    /// Object attention budget (0.1) - a small lifetime on autonomous attention so the creature
    /// cannot chase ONE moving object forever by re-acquiring it every time a lost coast re-finds
    /// it. Attention to a given <see cref="Interactable"/> accumulates across one "episode" -
    /// live -> lost coast -> re-acquire, all the same episode; it is NOT reset by re-seeing the same
    /// object, by a gaze interruption, or by a lost gap. It IS frozen while a probe owns the gaze,
    /// while the object is held, and while the creature is Approaching or Inspecting it (so a normal
    /// investigate-then-pickup on a reachable object is never cut off - only watching an un-catchable
    /// in-flight object and the lost coasts after it burn budget).
    /// When <see cref="objectAttentionBudget"/> is spent the object is dropped and held out of
    /// <see cref="SelectGazeTarget"/> (and out of <c>CreatureMovement</c>'s lost coast, via
    /// <see cref="AttentionCooldownObject"/>) for <see cref="objectAttentionCooldown"/> s; other
    /// Interactables and the player fallback are unaffected. A genuinely different object starts a
    /// fresh episode.
    ///
    /// Auditory orient (Hearing 0.1) - a SECOND, independent gaze-only channel, this time driven by
    /// <c>CreatureHearing</c> via <see cref="SetAuditoryOrientPoint"/> rather than by this component's
    /// own perception. A heard sound can steal the gaze toward its position for a brief,
    /// CreatureHearing-timed span, FOV-exempting the attended object exactly like the Player gaze
    /// interruption above and for the identical reason. It sits below that interruption and the
    /// Probe's forced gaze, but above just continuing to look at an object - see <see cref="Update"/>.
    /// <see cref="IsPlayerPerceived"/> (this component's own visual/range perception) is never written
    /// by Hearing - hearing a sound is evidence of a sound, not player identification.
    ///
    /// Deliberately still tiny: FOV + LOS for objects only, and no memory, no object familiarity,
    /// no wander / search, no rig, no gaze framework, no target registry, no pathfinding, no
    /// meaning/type judgement, no behavioural interrupt / "what?" reaction / follow decision,
    /// no boredom / personality, no per-object learning, no motion-based attention score.
    /// <see cref="IsPlayerPerceived"/> stays a pure player-in-range test for <c>CreatureMovement</c>;
    /// it is unaffected by the object FOV/LOS gate and by what the creature is actually looking at.
    /// </summary>
    public class CreaturePerception : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Empty transform whose rotation represents the gaze direction. Not a parent of the eye.")]
        [SerializeField] private Transform lookPivot;
        [Tooltip("Player root, used for the distance check. Auto-found by the \"Player\" tag if empty.")]
        [SerializeField] private Transform player;
        [Tooltip("Where to look when the chosen target is the player. Defaults to the player's camera, else the player root.")]
        [SerializeField] private Transform playerGazeTarget;

        [Header("Perception")]
        [Tooltip("A target is perceivable when within this flat (XZ) distance. Governs both the player check and the maximum distance an Interactable can be spotted.")]
        [SerializeField] private float perceptionRange = 5f;

        [Header("Object field of view (Interactables only - not the player)")]
        [Tooltip("Transform whose forward (flattened to XZ) is the axis of the object FOV cone. Wire this to LookPivot: the cone then follows the creature's gaze, which is re-aimed every frame and never freezes while the creature stands still (anchoring it to BodyVisual self-locks - see the class summary). Empty falls back to LookPivot, then the creature root.")]
        [SerializeField] private Transform facingReference;
        [Tooltip("Full horizontal cone, in degrees, within which an Interactable can be seen. A candidate must be within half of this angle either side of the facing direction.")]
        [SerializeField] private float fovAngle = 90f;
        [Tooltip("Colliders that block line of sight to an Interactable (walls, floors, other objects). The Interactable's own collider never blocks itself. The creature's own capsule is ignored because the ray starts inside it.")]
        [SerializeField] private LayerMask objectOcclusionMask = ~0;

        [Header("Gaze direction")]
        [Tooltip("How fast the gaze direction turns, in degrees per second.")]
        [SerializeField] private float turnSpeed = 240f;

        [Header("Head follow")]
        [Tooltip("Pivot the head/face turns around. Empty child of the creature root, parent of Face.")]
        [SerializeField] private Transform headPivot;
        [Tooltip("How fast the head turns toward the gaze direction, in degrees per second. Keep below turnSpeed so the eye leads.")]
        [SerializeField] private float headTurnSpeed = 140f;
        [Tooltip("Max downward pitch, in degrees. Not a neck limit - keeps the face off the capsule body when looking down.")]
        [SerializeField] private float maxLookDown = 55f;
        [Tooltip("Max upward pitch, in degrees.")]
        [SerializeField] private float maxLookUp = 80f;

        [Header("Pupil expression")]
        [Tooltip("The moving pupil. Child of the fixed eye; only its local X/Y are driven.")]
        [SerializeField] private Transform pupil;
        [Tooltip("Fixed eye transform. Its local axes define 'straight ahead' (+Z) for the pupil.")]
        [SerializeField] private Transform eyeReference;
        [Tooltip("Max pupil travel from centre, in the eye's local units.")]
        [SerializeField] private float pupilMaxOffset = 0.26f;
        [Tooltip("Maps how far off-axis the gaze is to pupil travel. Higher = pupil reaches the rim sooner.")]
        [SerializeField] private float pupilGain = 0.6f;

        [Header("Gaze interruption 0.1 (glance at a moving player, keep doing the object behaviour)")]
        [Tooltip("Player flat (XZ) speed, m/s, above which a player crossing the FOV while the creature " +
                 "is busy with an object earns a brief glance. A standing / slowly-adjusting player never " +
                 "interrupts. Player walk speed is ~4 m/s for reference.")]
        [SerializeField] private float playerGazeMotionThreshold = 1.5f;
        [Tooltip("How long the glance at the player lasts, in seconds. GAZE ONLY - the object stays the " +
                 "behavioural / attention target the whole time.")]
        [SerializeField] private float playerGazeInterruptDuration = 0.5f;
        [Tooltip("Minimum seconds of looking back at the object after a glance before another glance may " +
                 "start. Stops a continuously-moving player from holding the gaze - an occasional flick, not a follow.")]
        [SerializeField] private float playerGazeInterruptCooldown = 2.5f;

        [Header("Object attention budget 0.1 (don't chase one moving object forever)")]
        [Tooltip("Seconds of autonomous attention the creature will spend on ONE Interactable before " +
                 "giving up on it. The timer accumulates across brief lost/re-acquire gaps (it is NOT " +
                 "reset by re-seeing the same object) and while glancing at the player, but ONLY while " +
                 "the creature is making no catch progress. It is FROZEN while Approaching or Inspecting " +
                 "the object (a normal investigate-then-pickup on a reachable object is never cut off) " +
                 "and while a probe or a hold is using it - only watching an un-catchable in-flight " +
                 "object and the lost coasts after it keep burning budget.")]
        [SerializeField] private float objectAttentionBudget = 7f;
        [Tooltip("After the budget is spent, seconds that object is held OUT of autonomous " +
                 "SelectGazeTarget candidacy so the creature does not instantly re-lock onto it. " +
                 "Other Interactables and the player fallback are unaffected.")]
        [SerializeField] private float objectAttentionCooldown = 2.5f;

        private Quaternion _defaultLocalRotation;
        private Vector3 _pupilRestLocalPos;
        private Interactable[] _interactables;

        // CreatureProbe seam: while true AND the player is actually perceived, the creature LOOKS at the
        // player regardless of what SelectGazeTarget picked. It does not change attention selection
        // itself (AttendedInteractable is still computed every frame), only the gaze/head/pupil aim.
        // When the player is not perceived this falls back to the normal gaze - there is no search gaze.
        private bool _forcePlayerGaze;

        // Sibling on the same GameObject (CreatureMovement RequireComponent's CreaturePerception).
        // Read-only: while CreatureMovement is coasting to a lost object's last-known position,
        // LostGazePoint is that world spot and the gaze is aimed there (see Update). This is NOT
        // "the object is perceived" - AttendedInteractable / CurrentGazeTarget stay null.
        private CreatureMovement _movement;

        // Gaze interruption 0.1 state. GAZE ONLY: nothing here is read by CreatureMovement or any
        // behaviour - it only briefly changes what CurrentGazeTarget / the FOV anchor point at.
        private Vector3 _playerLastPos;         // for the player's frame-to-frame flat speed
        private bool _gazeInterruptActive;     // currently glancing at the player
        private float _gazeInterruptTimer;     // seconds left in the glance
        private float _gazeInterruptCooldown;  // seconds left before another glance may start
        private Interactable _interruptObject;  // the attended object the glance protects (FOV-exempt in SelectGazeTarget while active)

        // Hearing 0.1 auditory orient - gaze-only integration seam. CreatureHearing calls
        // SetAuditoryOrientPoint every frame; this component never initiates or times the orient
        // itself, it only aims the gaze at the point while one is set and (mirroring _interruptObject
        // above) FOV-exempts whatever was attended when the orient began, for the identical reason.
        private Vector3? _auditoryOrientPoint;
        private bool _auditoryOrientWasActive;
        private Interactable _auditoryProtectedObject;

        // Player Salience 0.1 - a THIRD, independent gaze-only seam, same shape as auditory orient
        // just above: PlayerSalience calls SetSalienceObservePoint every frame while its
        // PlayerAttentionLevel is Observe; this component never computes salience or decides when to
        // hold it, it only aims the gaze and FOV-exempts the attended object while told to.
        private Vector3? _salienceObservePoint;
        private bool _salienceObserveWasActive;
        private Interactable _salienceProtectedObject;

        // Object attention budget 0.1 state. One attention "episode" per Interactable; spans
        // live -> lost coast -> re-acquire without resetting. On expiry the object is dropped and
        // held out of SelectGazeTarget for a cooldown.
        private Interactable _episodeObject;         // the object the current episode is about, or null
        private float _episodeElapsed;               // seconds of attention spent this episode
        private Interactable _attentionCooldownObject; // the object currently excluded (budget spent), or null
        private float _attentionCooldownTimer;       // seconds left on that exclusion

        /// <summary>Whether the player is currently within perception range. The seam <c>CreatureMovement</c> reads.</summary>
        public bool IsPlayerPerceived { get; private set; }

        /// <summary>
        /// The one Interactable currently held out of autonomous attention by the Object Attention
        /// Budget (its episode just ran out), or null. It is excluded from <see cref="SelectGazeTarget"/>;
        /// <c>CreatureMovement</c> also reads this so it never lost-coasts toward a deliberately-
        /// abandoned object. Clears itself after <see cref="objectAttentionCooldown"/> seconds.
        /// </summary>
        public Interactable AttentionCooldownObject => _attentionCooldownObject;

        // --- TEMPORARY diagnostic getters (Object Attention Budget). Read-only; safe to delete with
        //     CreatureAttentionDebug. ---
        public Interactable DebugEpisodeObject => _episodeObject;
        public float DebugEpisodeElapsed => _episodeElapsed;
        public float DebugObjectAttentionBudget => objectAttentionBudget;
        public float DebugAttentionCooldownTimer => _attentionCooldownTimer;
        public bool DebugForcePlayerGaze => _forcePlayerGaze;

        /// <summary>The player transform this component tracks, or null. Read-only seam for sibling components (e.g. movement).</summary>
        public Transform Player => player;

        /// <summary>The transform the creature is gazing at this frame, or null when the range is empty. Read-only, for inspection.</summary>
        public Transform CurrentGazeTarget { get; private set; }

        /// <summary>
        /// The <see cref="Interactable"/> the creature is currently attending to (the nearest one that won
        /// <see cref="SelectGazeTarget"/> this frame), or null when the winner is the player or nothing is
        /// in range. This is only a read-only seam - attention and any "action target" stay separate ideas;
        /// <c>CreatureMovement</c> merely borrows this to seed what it walks to and inspects this prototype.
        /// </summary>
        public Interactable AttendedInteractable { get; private set; }

        private void Awake()
        {
            if (lookPivot == null)
                lookPivot = transform;
            _defaultLocalRotation = lookPivot.localRotation;

            // Gaze anchor by default (see class summary): a body-facing anchor self-locks because it
            // only turns while the creature is already moving. lookPivot is guaranteed set just above.
            if (facingReference == null)
                facingReference = lookPivot != null ? lookPivot : transform;

            if (pupil != null)
                _pupilRestLocalPos = pupil.localPosition;

            if (player == null)
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null)
                    player = tagged.transform;
            }

            if (playerGazeTarget == null && player != null)
            {
                var cam = player.GetComponentInChildren<Camera>();
                playerGazeTarget = cam != null ? cam.transform : player;
            }

            if (player != null)
                _playerLastPos = player.position; // seed so frame 1 does not read a bogus huge speed

            // Prototype scope: interactables are never spawned or destroyed at runtime, so one
            // lookup is enough. No registry, no per-frame scene search.
            _interactables = FindObjectsByType<Interactable>(FindObjectsSortMode.None);

            _movement = GetComponent<CreatureMovement>();
        }

        /// <summary>
        /// CreatureProbe only: force the creature's gaze onto the player for the duration of a probe.
        /// Honoured only while <see cref="IsPlayerPerceived"/>; otherwise the normal nearest-target gaze
        /// is used (no search gaze). Attention selection is unaffected either way.
        /// </summary>
        public void SetForcePlayerGaze(bool on) => _forcePlayerGaze = on;

        /// <summary>
        /// Hearing 0.1 (<c>CreatureHearing</c>) only: aim the gaze at <paramref name="point"/> for as
        /// long as it keeps calling this with a non-null value each frame; pass null the instant the
        /// orient should end. GAZE ONLY - never touches <see cref="AttendedInteractable"/>,
        /// <c>CreatureMovement</c>'s Approach/Inspect target, or any Probe/Wander state. Sits below the
        /// Probe force-gaze and the Player gaze-interruption in priority (see <see cref="Update"/>) and
        /// above the normal selected object - "a heard sound briefly steals the gaze from whatever the
        /// creature was looking at, but never overrides an explicit Probe, and never cancels the
        /// underlying behaviour target". All timing/hysteresis/refresh logic lives in
        /// <c>CreatureHearing</c>; this is a pure actuator seam, the same shape as
        /// <see cref="SetForcePlayerGaze"/>.
        /// </summary>
        public void SetAuditoryOrientPoint(Vector3? point) => _auditoryOrientPoint = point;

        /// <summary>
        /// Player Salience 0.1 (<c>PlayerSalience</c>) only: aim the gaze at <paramref name="point"/>
        /// (the Player's position) for as long as it keeps calling this with a non-null value -
        /// typically for as long as <c>PlayerSalience.AttentionLevel</c> is Observe, which can be many
        /// seconds. GAZE ONLY, identical shape and guarantees to <see cref="SetAuditoryOrientPoint"/>:
        /// never touches <see cref="AttendedInteractable"/>, <c>CreatureMovement</c>'s Approach/Inspect
        /// target, Wander, PlayerObserve, or any Probe state - a highly-salient Player wins the GAZE,
        /// not the underlying behaviour. Sits below the Probe's forced gaze but above the (shorter)
        /// Player gaze-interruption and auditory orient - see <see cref="Update"/> - since a sustained
        /// Observe decision should not itself flicker against those brief triggers.
        /// </summary>
        public void SetSalienceObservePoint(Vector3? point) => _salienceObservePoint = point;

        private void Update()
        {
            IsPlayerPerceived = PerceivePlayer();

            // Selection still runs every frame so AttendedInteractable stays correct for
            // CreatureMovement / CreaturePickup. The overrides below only change what the creature
            // looks at, not what it treats as its attention target.
            Transform selected = SelectGazeTarget();

            // Age the current attention episode and, if its budget is spent, put that object on a
            // brief cooldown (excluded from the next SelectGazeTarget). Runs after SelectGazeTarget so
            // it sees this frame's AttendedInteractable.
            UpdateAttentionBudget();

            // Decide whether the creature glances at a moving player (gaze only - see the method).
            // Runs after SelectGazeTarget so it sees this frame's AttendedInteractable.
            UpdateGazeInterrupt();

            // Auditory orient bookkeeping (Hearing 0.1): on the rising edge of CreatureHearing setting
            // a point, snapshot whatever is attended right now so SelectGazeTarget can FOV-exempt it
            // (see there) - identical reasoning to _interruptObject above, just for this second,
            // independent gaze override.
            if (_auditoryOrientPoint.HasValue && !_auditoryOrientWasActive)
                _auditoryProtectedObject = AttendedInteractable;
            else if (!_auditoryOrientPoint.HasValue)
                _auditoryProtectedObject = null;
            _auditoryOrientWasActive = _auditoryOrientPoint.HasValue;

            // Salience-observe bookkeeping (Player Salience 0.1): identical rising-edge snapshot, a
            // THIRD independent gaze override.
            if (_salienceObservePoint.HasValue && !_salienceObserveWasActive)
                _salienceProtectedObject = AttendedInteractable;
            else if (!_salienceObservePoint.HasValue)
                _salienceProtectedObject = null;
            _salienceObserveWasActive = _salienceObservePoint.HasValue;

            // Gaze priority:
            //   1. a probe's forced player-gaze (only while the player is actually perceived),
            //   2. a Player Salience "Observe" hold (0.1) - a sustained look at the Player while an
            //      object stays the behavioural target (FOV-exempt, same treatment as case 3 below).
            //      Sits below an explicit Probe but above the brief triggers in cases 3-4, so a
            //      standing Observe decision does not itself flicker against them.
            //   3. a gaze interruption - a brief glance at a moving player while an object stays the
            //      behavioural target (nothing behavioural changes; the object is FOV-exempt during it),
            //   4. an auditory orient (Hearing 0.1) - a brief glance toward a heard sound's position
            //      while an object stays the behavioural target (same FOV-exempt treatment as case 3).
            //      Sits below an explicit Probe/glance/Observe but above just continuing to look at an
            //      object - "a heard sound is worth a glance, never worth abandoning what you were doing".
            //   5. the perceived object / player-as-fallback that SelectGazeTarget picked,
            //   6. ONLY when 1-5 are empty - CreatureMovement's lost-object last-known position,
            //   7. otherwise nothing -> ease back to rest.
            // Cases 2-4 and 6 leave CurrentGazeTarget conceptually about "where the head points", not
            // "what is perceived": AttendedInteractable is untouched by any of them.
            Vector3? lostGazePoint = null;
            if (_forcePlayerGaze && IsPlayerPerceived && player != null)
            {
                CurrentGazeTarget = playerGazeTarget != null ? playerGazeTarget : player;
            }
            else if (_salienceObservePoint.HasValue)
            {
                CurrentGazeTarget = null;
                lostGazePoint = _salienceObservePoint; // UpdateGazeDirection's worldPoint param - see below
            }
            else if (_gazeInterruptActive && AttendedInteractable != null && player != null)
            {
                CurrentGazeTarget = playerGazeTarget != null ? playerGazeTarget : player;
            }
            else if (_auditoryOrientPoint.HasValue)
            {
                CurrentGazeTarget = null;
                lostGazePoint = _auditoryOrientPoint; // UpdateGazeDirection's worldPoint param - see below
            }
            else if (selected != null)
            {
                CurrentGazeTarget = selected;
            }
            else
            {
                CurrentGazeTarget = null;
                lostGazePoint = _movement != null ? _movement.LostGazePoint : null;
            }

            UpdateGazeDirection(CurrentGazeTarget, lostGazePoint);
            UpdateHead();
            UpdatePupil();
        }

        private bool PerceivePlayer()
        {
            if (player == null)
                return false;

            Vector3 flat = player.position - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude <= perceptionRange * perceptionRange;
        }

        // Attention: nearest perceivable Interactable wins; the player is only a fallback when there
        // is none. Both are measured by the same flat XZ distance; the Interactable is gated by
        // range + FOV cone + LOS (CanPerceiveInteractable), the player fallback by range only.
        // Null when no Interactable is perceivable and the player is out of range.
        private Transform SelectGazeTarget()
        {
            float rangeSqr = perceptionRange * perceptionRange;

            // 1. Nearest Interactable that passes the full gate. An object the creature can actually
            //    see always outranks the player for gaze/attention at this stage.
            float bestSqr = float.MaxValue;
            Transform best = null;
            Interactable bestInteractable = null;

            if (_interactables != null)
            {
                foreach (var it in _interactables)
                {
                    if (it == null)
                        continue;

                    // Object Attention Budget: an object that just spent its attention episode is
                    // briefly not an autonomous candidate, so the creature does not re-lock onto it
                    // the instant it looks back. Other objects and the player fallback are unaffected.
                    if (it == _attentionCooldownObject)
                        continue;

                    // Range + FOV cone + LOS. Below this line it is a pure nearest-by-flat-distance
                    // pick over the visible subset. While a gaze interruption, an auditory orient
                    // (Hearing 0.1), OR a salience observe hold (Player Salience 0.1) is glancing
                    // elsewhere, the object whichever one is protecting keeps its attention on
                    // range + LOS only - deliberately moving the GAZE off it must never make selection
                    // drop it.
                    bool skipFov = (_gazeInterruptActive && it == _interruptObject)
                                   || (_auditoryOrientPoint.HasValue && it == _auditoryProtectedObject)
                                   || (_salienceObservePoint.HasValue && it == _salienceProtectedObject);
                    if (!CanPerceiveInteractable(it, skipFov))
                        continue;

                    float sqr = FlatSqrDistance(it.transform.position);
                    if (sqr <= rangeSqr && sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = it.transform;
                        bestInteractable = it;
                    }
                }
            }

            // 2. Only when no perceivable Interactable won: fall back to the player, range-only (no
            //    FOV / LOS), exactly the old player test. This is what stops the player from stealing
            //    gaze off an Interactable that briefly left the cone.
            if (bestInteractable == null && player != null)
            {
                if (FlatSqrDistance(player.position) <= rangeSqr)
                    best = playerGazeTarget != null ? playerGazeTarget : player;
            }

            AttendedInteractable = bestInteractable;
            return best;
        }

        private float FlatSqrDistance(Vector3 worldPos)
        {
            Vector3 flat = worldPos - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude;
        }

        // Object Attention Budget 0.1: age the current attention episode and, when its budget is
        // spent, drop the object and hold it out of SelectGazeTarget for a cooldown. Purpose: the
        // creature must not chase ONE moving object forever by re-acquiring it every time a lost
        // coast re-finds it. Runs each Update after SelectGazeTarget.
        //
        // Episode identity spans "live -> lost coast -> re-acquire": the episode object is
        // AttendedInteractable, or - to bridge a brief lost gap without a reset - the object
        // CreatureMovement is lost-coasting toward. It is a fresh episode only when attention moves
        // to a genuinely different object (or after a full give-up).
        //
        // Not counted against the budget: time while a probe owns the gaze (a deliberate use of an
        // object), time while the object is held, and time while the creature is actually Inspecting
        // it (it has caught up - a normal investigate-then-pickup is never cut off; only the chase is).
        private void UpdateAttentionBudget()
        {
            float dt = Time.deltaTime;

            if (_attentionCooldownTimer > 0f)
            {
                _attentionCooldownTimer -= dt;
                if (_attentionCooldownTimer <= 0f)
                    _attentionCooldownObject = null;
            }

            // A probe is deliberately using an object right now - freeze the whole budget (do not
            // accumulate, expire, or reset). It resumes exactly where it was when the probe ends.
            if (_forcePlayerGaze)
                return;

            Interactable attn = AttendedInteractable;
            if (attn == null && _movement != null)
                attn = _movement.LostTargetInteractable; // bridge a lost coast without resetting
            if (attn != null && attn.IsHeld)
                attn = null;                             // a held object is not autonomous watching

            if (attn == null || attn == _attentionCooldownObject)
            {
                // No episode subject this frame (nothing attended / coasted, or it is the object we
                // just abandoned). End the episode; the cooldown timer above keeps running.
                _episodeObject = null;
                _episodeElapsed = 0f;
                return;
            }

            if (attn != _episodeObject)
            {
                // First episode, or attention genuinely moved to a different object.
                _episodeObject = attn;
                _episodeElapsed = 0f;
                return;
            }

            // Same object, same episode. Accumulate ONLY while the creature is making no catch
            // progress - i.e. it is NOT walking toward the object (Approach) and NOT orbiting it
            // (Inspect). Those two phases mean the object is reachable and being dealt with normally
            // (a stationary-Cube investigate-then-pickup is never cut off); what keeps burning budget
            // is watching an un-catchable in-flight object and the lost coasts that follow it.
            if (_movement != null && (_movement.IsApproaching || _movement.IsInspecting))
                return;

            _episodeElapsed += dt;
            if (_episodeElapsed >= objectAttentionBudget)
            {
                _attentionCooldownObject = _episodeObject;
                _attentionCooldownTimer = objectAttentionCooldown;
                _episodeObject = null;
                _episodeElapsed = 0f;
            }
        }

        /// <summary>
        /// <c>CreatureMovement</c> calls this when it judges a MOVING-object pursuit succeeded - it
        /// coasted after the object and caught up to within interaction distance. Puts that ONE
        /// object on the same brief autonomous-attention cooldown a spent budget uses
        /// (<see cref="objectAttentionCooldown"/> s, via <see cref="_attentionCooldownObject"/>), so
        /// the creature does not immediately re-acquire and chase it again. Other Interactables and
        /// the player fallback are unaffected. Does not touch any movement / probe / observe state.
        /// </summary>
        public void ReportPursuitComplete(Interactable obj)
        {
            if (obj == null)
                return;

            _attentionCooldownObject = obj;
            _attentionCooldownTimer = objectAttentionCooldown;
            if (_episodeObject == obj)
            {
                _episodeObject = null;
                _episodeElapsed = 0f;
            }
        }

        // Gaze interruption 0.1: decide whether the creature briefly glances at a moving player while
        // it is busy with an object. GAZE ONLY - this reads AttendedInteractable / player motion / the
        // FOV cone and writes only the interrupt timers + _interruptObject. It never writes
        // AttendedInteractable and nothing behavioural ever reads it. Runs each Update right after
        // SelectGazeTarget (so AttendedInteractable is this frame's) and before the gaze is resolved.
        private void UpdateGazeInterrupt()
        {
            float dt = Time.deltaTime;

            // Player's flat (XZ) speed this frame.
            float playerSpeed = 0f;
            if (player != null)
            {
                if (dt > 0f)
                {
                    Vector3 d = player.position - _playerLastPos;
                    d.y = 0f;
                    playerSpeed = d.magnitude / dt;
                }
                _playerLastPos = player.position;
            }

            // Already glancing: run the timer down. End early (and start the cooldown) if there is no
            // longer the SAME object to look back at - it left perception, or attention moved on.
            if (_gazeInterruptActive)
            {
                _gazeInterruptTimer -= dt;
                if (_gazeInterruptTimer <= 0f
                    || AttendedInteractable == null
                    || AttendedInteractable != _interruptObject)
                {
                    _gazeInterruptActive = false;
                    _gazeInterruptTimer = 0f;
                    _interruptObject = null;
                    _gazeInterruptCooldown = playerGazeInterruptCooldown;
                }
                return;
            }

            // Looking back at the object after a glance: no new glance until the cooldown elapses.
            if (_gazeInterruptCooldown > 0f)
            {
                _gazeInterruptCooldown -= dt;
                return;
            }

            // May a fresh glance start? ALL of:
            //  - a probe is not already forcing the gaze (it outranks this);
            //  - there is an object attention target to keep and return to;
            //  - the player is in perception range AND inside the current FOV cone;
            //  - the player is actually moving, not just standing / drifting.
            if (_forcePlayerGaze) return;
            if (AttendedInteractable == null) return;
            if (player == null || !IsPlayerPerceived) return;
            if (playerSpeed < playerGazeMotionThreshold) return;
            if (!IsPlayerInFov()) return;

            _interruptObject = AttendedInteractable;
            _gazeInterruptTimer = playerGazeInterruptDuration;
            _gazeInterruptActive = true;
        }

        // Is the player inside the same fovAngle cone (around facingReference's flattened forward)
        // that gates Interactables? Used only to decide a gaze interruption - no LOS test, a glance
        // follows a movement the creature would catch in the corner of its eye.
        private bool IsPlayerInFov()
        {
            if (player == null)
                return false;

            Vector3 flat = player.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f)
                return true;

            Transform face = facingReference != null ? facingReference : transform;
            Vector3 facing = face.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-6f)
                return false;

            return Vector3.Angle(facing, flat) <= fovAngle * 0.5f;
        }

        /// <summary>
        /// The 0.2 object-visibility gate: range (flat XZ, same limit as the player check) AND inside
        /// the <see cref="fovAngle"/> cone around <see cref="facingReference"/>'s flattened forward
        /// AND a clear line of sight from the eye. Applied to Interactables only; never to the player.
        /// Also used by the gizmos so the Scene view matches what selection actually sees.
        /// <paramref name="skipFov"/> drops only the cone test (range + LOS still apply): used for the
        /// one object a gaze interruption is protecting while the gaze is deliberately elsewhere.
        /// </summary>
        private bool CanPerceiveInteractable(Interactable it, bool skipFov = false)
        {
            Vector3 flat = it.transform.position - transform.position;
            flat.y = 0f;
            float distSqr = flat.sqrMagnitude;
            if (distSqr > perceptionRange * perceptionRange)
                return false;

            // FOV: angle between the creature's flattened facing and the flat direction to the target.
            // Skipped only when the target is basically on top of the creature (direction undefined).
            if (!skipFov && distSqr > 1e-4f)
            {
                Transform face = facingReference != null ? facingReference : transform;
                Vector3 facing = face.forward;
                facing.y = 0f;
                if (facing.sqrMagnitude > 1e-6f && Vector3.Angle(facing, flat) > fovAngle * 0.5f)
                    return false;
            }

            // LOS: one ray from the eye to the object's origin. Anything on objectOcclusionMask that
            // is not part of THIS interactable blocks it. The ray starts on the creature's own capsule
            // axis, so Unity never reports the capsule as the blocker.
            Vector3 eye = lookPivot != null ? lookPivot.position : transform.position;
            Vector3 toTarget = it.transform.position - eye;
            float dist = toTarget.magnitude;
            if (dist > 1e-3f &&
                Physics.Raycast(eye, toTarget / dist, out RaycastHit hit, dist + 0.01f,
                                objectOcclusionMask, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<Interactable>() != it)
                return false;

            return true;
        }

        // Eases lookPivot toward the chosen target, else a lost object's last-known world point
        // (CreatureMovement's coast - not a perceived target), else back to default.
        private void UpdateGazeDirection(Transform target, Vector3? worldPoint = null)
        {
            Quaternion desired;

            if (target != null)
            {
                Vector3 dir = target.position - lookPivot.position;
                if (dir.sqrMagnitude < 0.0001f)
                    return; // target essentially on the pivot; hold this frame
                desired = Quaternion.LookRotation(dir);
            }
            else if (worldPoint.HasValue)
            {
                Vector3 dir = worldPoint.Value - lookPivot.position;
                if (dir.sqrMagnitude < 0.0001f)
                    return; // point essentially on the pivot; hold this frame
                desired = Quaternion.LookRotation(dir);
            }
            else
            {
                desired = lookPivot.parent != null
                    ? lookPivot.parent.rotation * _defaultLocalRotation
                    : _defaultLocalRotation;
            }

            lookPivot.rotation = Quaternion.RotateTowards(
                lookPivot.rotation, desired, turnSpeed * Time.deltaTime);
        }

        // Turns the head/face pivot toward the same desired direction the pupil chases, so the
        // creature's attention is readable from the side and behind. Yaw is unlimited (this is
        // not a human neck); pitch is clamped only so the face does not sink into the capsule.
        private void UpdateHead()
        {
            if (headPivot == null)
                return;

            Vector3 aimDir = lookPivot.forward;

            // Yaw from the flat (XZ) part of the aim. When the target is almost straight up or
            // down that part is ~0 and yaw is meaningless, so hold the head's current yaw.
            Vector3 flat = new Vector3(aimDir.x, 0f, aimDir.z);
            float yaw = flat.sqrMagnitude < 1e-6f
                ? headPivot.eulerAngles.y
                : Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg;

            // Pitch from the vertical part; positive euler X points the face down. Clamp only to
            // keep the face off the body, not as a neck limit.
            float pitch = -Mathf.Asin(Mathf.Clamp(aimDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, -maxLookUp, maxLookDown);

            Quaternion desired = Quaternion.Euler(pitch, yaw, 0f); // world-space aim; body is not rotated
            headPivot.rotation = Quaternion.RotateTowards(
                headPivot.rotation, desired, headTurnSpeed * Time.deltaTime);
        }

        // Slides the pupil within the eye toward the (already smoothed) gaze direction, clamped.
        private void UpdatePupil()
        {
            if (pupil == null || eyeReference == null)
                return;

            Vector3 localDir = eyeReference.InverseTransformDirection(lookPivot.forward);
            Vector2 planar = new Vector2(localDir.x, localDir.y);
            Vector2 planarDir = planar.sqrMagnitude > 1e-6f ? planar.normalized : Vector2.zero;

            // tan(off-axis angle): 0 dead ahead, grows with angle, "infinite" at / behind the eye plane.
            float travel = localDir.z > 0.001f ? planar.magnitude / localDir.z : float.MaxValue;
            Vector2 offset = planarDir * Mathf.Min(travel * pupilGain, pupilMaxOffset);

            pupil.localPosition = new Vector3(
                _pupilRestLocalPos.x + offset.x,
                _pupilRestLocalPos.y + offset.y,
                _pupilRestLocalPos.z);
        }

        private void OnValidate()
        {
            perceptionRange = Mathf.Max(0f, perceptionRange);
            fovAngle = Mathf.Clamp(fovAngle, 1f, 360f);
            playerGazeMotionThreshold = Mathf.Max(0f, playerGazeMotionThreshold);
            playerGazeInterruptDuration = Mathf.Max(0.05f, playerGazeInterruptDuration);
            playerGazeInterruptCooldown = Mathf.Max(0f, playerGazeInterruptCooldown);
            objectAttentionBudget = Mathf.Max(0.5f, objectAttentionBudget);
            objectAttentionCooldown = Mathf.Max(0f, objectAttentionCooldown);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, perceptionRange);

            Transform pivot = lookPivot != null ? lookPivot : transform;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(pivot.position, pivot.forward * 1.5f);

            // Object FOV cone (flattened) from the facing reference, plus a green/red line to every
            // Interactable showing whether it passes the range + FOV + LOS gate right now.
            Transform face = facingReference != null ? facingReference : transform;
            Vector3 facing = face.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude > 1e-6f)
            {
                facing.Normalize();
                Vector3 origin = transform.position;
                Vector3 left = Quaternion.AngleAxis(-fovAngle * 0.5f, Vector3.up) * facing;
                Vector3 right = Quaternion.AngleAxis(fovAngle * 0.5f, Vector3.up) * facing;
                Gizmos.color = new Color(1f, 0.6f, 0.1f);
                Gizmos.DrawRay(origin, left * perceptionRange);
                Gizmos.DrawRay(origin, right * perceptionRange);

                var list = Application.isPlaying
                    ? _interactables
                    : FindObjectsByType<Interactable>(FindObjectsSortMode.None);
                if (list != null)
                {
                    Vector3 eye = lookPivot != null ? lookPivot.position : transform.position;
                    foreach (var it in list)
                    {
                        if (it == null)
                            continue;
                        Gizmos.color = CanPerceiveInteractable(it) ? Color.green : new Color(1f, 0.25f, 0.2f);
                        Gizmos.DrawLine(eye, it.transform.position);
                    }
                }
            }
        }
    }
}
