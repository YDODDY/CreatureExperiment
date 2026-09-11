using UnityEngine;
using CreatureExperiment.Player;

namespace CreatureExperiment.Creature.Perception
{
    /// <summary>
    /// Player movement -&gt; logical <see cref="SoundStimulus"/> 0.1. Turns actual Player movement/state
    /// (never raw input) into Footstep/Jump sound stimuli that <see cref="CreatureHearing"/> (or any
    /// future listener) can subscribe to via <see cref="Emitted"/>. NOT an audio system - no
    /// AudioClip, no AudioSource; this is a logical "how loud would this movement be heard" signal
    /// only, and is fully decoupled from any listener (raises an event; knows nothing about Creatures).
    ///
    /// Reads <see cref="PlayerMovement"/>'s existing crouch/sprint/jump state read-only
    /// (<c>IsCrouching</c> / <c>IsSprinting</c> / <c>JumpedThisFrame</c> - three minimal seams added to
    /// that class for this step; its own movement logic is completely unchanged). This project has no
    /// separate "Dash" action - <c>IsSprinting</c> (code name "Sprint") IS the fast-movement tier this
    /// component treats as its Dash sound tier; no new movement system was introduced to rename or
    /// duplicate it.
    ///
    /// Movement tier -&gt; intensity, lowest to highest: Crouch &lt; Normal &lt; Dash(=Sprint) &lt; Jump.
    /// Jump is a one-shot DISCRETE stimulus (once per jump trigger), never a continuous tier - see
    /// <see cref="PlayerMovementSoundMode"/>, which only ever reads Silent/Crouch/Normal/Dash.
    /// Standing still emits NOTHING - even crouched, even holding a move key against a wall - tier and
    /// gating both come from <see cref="CurrentMoveSpeed"/>, a real measured flat (XZ) world-space
    /// frame-delta speed (same simple idiom <c>PlayerObservation</c>/<c>CreatureEncounterObservation</c>
    /// already use), computed independently here rather than reused from those, so this component works
    /// standing alone with none of the Brain/Observation sensors present.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    public class PlayerMovementSoundEmitter : MonoBehaviour
    {
        /// <summary>Continuous movement tiers only - Jump is a discrete event, not a tier, so it is never reported here.</summary>
        public enum PlayerMovementSoundMode { Silent, Crouch, Normal, Dash }

        [Header("References")]
        [Tooltip("Empty = this GameObject's own PlayerMovement.")]
        [SerializeField] private PlayerMovement playerMovement;
        [Tooltip("Player root whose world-space XZ movement is measured. Empty = this GameObject's own transform.")]
        [SerializeField] private Transform playerRoot;

        [Header("Movement threshold")]
        [Tooltip("Flat (XZ) speed, m/s, above which the Player counts as ACTUALLY moving (not just holding a key while blocked/still). Below this: Silent, no stimulus at all, regardless of crouch/sprint state.")]
        [SerializeField] private float movementThreshold = 0.15f;

        [Header("Intensity per tier (0..1) - keep crouch < normal < dash <= jump")]
        [SerializeField] private float crouchIntensity = 0.25f;
        [SerializeField] private float normalIntensity = 0.55f;
        [SerializeField] private float dashIntensity = 0.90f;
        [SerializeField] private float jumpIntensity = 1.00f;

        [Header("Emission")]
        [Tooltip("Seconds between Footstep-tier stimuli while continuously moving. A prototype cadence, not synced to animation foot contact - swap for a real footstep event later without changing CreatureHearing (it only ever sees SoundStimulus values).")]
        [SerializeField] private float movementSoundInterval = 0.35f;
        [Tooltip("Simple teleport/scene-reset guard, same philosophy as PlayerObservation's: an instantaneous speed above this (m/s) is treated as a non-gameplay jump, not real movement (no stimulus emitted for it).")]
        [SerializeField] private float teleportSpeedGuard = 20f;

        /// <summary>Raised once per emitted SoundStimulus - both interval Footsteps and the one-shot Jump. Any listener may subscribe; this component knows nothing about who does.</summary>
        public event System.Action<SoundStimulus> Emitted;

        /// <summary>Which continuous movement tier is selected right now (Silent if not actually moving). For debugging/HUD. Jump is not reported here - see class summary.</summary>
        public PlayerMovementSoundMode CurrentMode { get; private set; } = PlayerMovementSoundMode.Silent;

        /// <summary>The intensity CurrentMode currently maps to (0 while Silent). Convenience for debug/HUD only - the actual emitted stimulus intensity is computed the same way at emission time.</summary>
        public float CurrentIntensity => CurrentMode switch
        {
            PlayerMovementSoundMode.Crouch => crouchIntensity,
            PlayerMovementSoundMode.Dash => dashIntensity,
            PlayerMovementSoundMode.Normal => normalIntensity,
            _ => 0f,
        };

        /// <summary>Flat (XZ) world speed this frame, m/s - the actual-movement gate. Computed independently of PlayerObservation (see class summary).</summary>
        public float CurrentMoveSpeed { get; private set; }

        private Vector3 _prevPos;
        private bool _initialized;
        private float _intervalTimer;

        private void Awake()
        {
            if (playerMovement == null)
                playerMovement = GetComponent<PlayerMovement>();
            if (playerRoot == null)
                playerRoot = transform;
        }

        // LateUpdate - same reasoning used throughout this project's Brain/Perception work:
        // PlayerMovement is Update(), so LateUpdate always sees this frame's settled position AND its
        // JumpedThisFrame pulse (set earlier in the same frame's PlayerMovement.Update()).
        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            float now = Time.time;

            UpdateMoveSpeed(dt);

            bool moving = CurrentMoveSpeed > movementThreshold;
            bool crouching = playerMovement != null && playerMovement.IsCrouching;
            bool sprinting = playerMovement != null && playerMovement.IsSprinting;

            // Mirrors PlayerMovement's own precedence (crouch overrides sprint for movement SPEED), so
            // the sound tier never contradicts what the Player's feet are actually doing.
            CurrentMode = !moving ? PlayerMovementSoundMode.Silent
                : crouching ? PlayerMovementSoundMode.Crouch
                : sprinting ? PlayerMovementSoundMode.Dash
                : PlayerMovementSoundMode.Normal;

            if (moving)
            {
                _intervalTimer += dt;
                if (_intervalTimer >= movementSoundInterval)
                {
                    _intervalTimer = 0f;
                    Raise(SoundStimulusType.Footstep, CurrentIntensity, now);
                }
            }
            else
            {
                _intervalTimer = 0f; // don't bank silent time into an instant burst once moving resumes
            }

            if (playerMovement != null && playerMovement.JumpedThisFrame)
                Raise(SoundStimulusType.Jump, jumpIntensity, now); // one-shot: fires only on the JumpedThisFrame pulse, never per airborne frame
        }

        private void UpdateMoveSpeed(float dt)
        {
            if (playerRoot == null)
            {
                CurrentMoveSpeed = 0f;
                return;
            }

            Vector3 pos = playerRoot.position;
            if (!_initialized)
            {
                _prevPos = pos;
                _initialized = true;
                CurrentMoveSpeed = 0f;
                return;
            }

            Vector3 flatDelta = pos - _prevPos;
            flatDelta.y = 0f;
            _prevPos = pos;

            float raw = dt > 0f ? flatDelta.magnitude / dt : 0f;
            CurrentMoveSpeed = raw > teleportSpeedGuard ? 0f : raw;
        }

        private void Raise(SoundStimulusType type, float intensity, float now)
        {
            Vector3 pos = playerRoot != null ? playerRoot.position : transform.position;
            Emitted?.Invoke(new SoundStimulus(pos, intensity, type, playerRoot, now));
        }

        private void OnValidate()
        {
            movementThreshold = Mathf.Max(0f, movementThreshold);
            movementSoundInterval = Mathf.Max(0.05f, movementSoundInterval);
            teleportSpeedGuard = Mathf.Max(0.1f, teleportSpeedGuard);

            crouchIntensity = Mathf.Clamp01(crouchIntensity);
            normalIntensity = Mathf.Clamp01(normalIntensity);
            dashIntensity = Mathf.Clamp01(dashIntensity);
            jumpIntensity = Mathf.Clamp01(jumpIntensity);

            // Order is not force-clamped (values stay independently editable for tuning), but a
            // misconfigured order silently defeats the whole point of the tiers, so flag it.
            if (!(crouchIntensity < normalIntensity && normalIntensity < dashIntensity && dashIntensity <= jumpIntensity))
                Debug.LogWarning("[PlayerMovementSoundEmitter] Intensity tiers are not in the expected " +
                    "order crouch < normal < dash <= jump - CreatureHearing's detection range per tier " +
                    "will not reflect the intended Crouch/Normal/Dash/Jump progression.", this);
        }
    }
}
