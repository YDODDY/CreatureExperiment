using UnityEngine;
using CreatureExperiment.Creature;
using CreatureExperiment.Creature.Perception;

namespace CreatureExperiment.Creature.Brain
{
    /// <summary>
    /// Player Salience / Attention Arbitration 0.1. A single accumulating 0..1 score, built from
    /// transient stimulus bonuses (a heard Footstep/Jump, a new Visual Acquisition, a confirmed
    /// Thrown-Object HIT) plus bounded persistent-context floors (VeryClose, FastApproach), decaying
    /// over time and read through a 3-level hysteresis (<see cref="PlayerAttentionLevel"/>) into
    /// Ignore/Glance/Observe. This exists to fix ONE symptom: rapid Object&lt;-&gt;Player gaze-bounce
    /// near SortingArea caused by Hearing 0.1's short auditory-orient glances repeating in quick
    /// succession. Only <see cref="PlayerAttentionLevel.Observe"/> does anything visible - it holds a
    /// sustained gaze on the Player via <c>CreaturePerception.SetSalienceObservePoint</c>, the SAME
    /// gaze-only seam shape as <c>SetForcePlayerGaze</c>/<c>SetAuditoryOrientPoint</c>. This component
    /// never touches <see cref="CreaturePerception.AttendedInteractable"/>, never touches
    /// <c>CreatureMovement</c>'s Approach/Inspect/pursuit state, never cancels Probe or Manipulation,
    /// never applies force/velocity to anything - Observe makes the Creature's HEAD turn to notice the
    /// Player for a while, it does not interrupt whatever the Creature's body is doing.
    ///
    /// A Thrown-Object HIT is treated here as "VERY STRONG ATTENTION STIMULUS" only - a large score
    /// bonus plus a dedicated <see cref="throwHitAttentionHold"/> longer than the normal
    /// <see cref="minimumObserveDuration"/>. It carries no Fear/Threat/Hostility meaning and drives no
    /// retaliation/flee/aggression behaviour - see the Report for the explicit non-goals list.
    ///
    /// Deliberately NOT a generic per-object salience system: every stimulus/floor here is specific to
    /// "the Player", read through existing narrow seams (<see cref="CreatureHearing"/>,
    /// <see cref="CreatureObservation.ThrowHitSerial"/>, <see cref="CreatureEncounterObservation"/>,
    /// <see cref="CreaturePerception.IsPlayerPerceived"/>) - nothing here generalizes to Objects.
    ///
    /// Edge detection reuses the project's established monotonic-serial idiom
    /// (<see cref="CreatureHearing.HeardStimulusSerial"/>, <see cref="CreatureEncounterObservation.VisualAcquiredSerial"/>,
    /// <see cref="CreatureObservation.ThrowHitSerial"/>) - a snapshot-and-compare against the serial
    /// value seen last frame, so a bonus is added exactly once per NEW stimulus, never once per frame
    /// while a rolling window merely happens to contain one.
    ///
    /// Persistent-context floors (VeryClose, FastApproach) are BOUNDED: each is
    /// <c>_score = Mathf.Max(_score, floor)</c>, applied every frame the condition holds - never added
    /// per frame. Standing right next to the Creature holds the score at (at least) its floor, it does
    /// not climb past it just by continuing to stand there.
    /// </summary>
    [RequireComponent(typeof(CreaturePerception))]
    public class PlayerSalience : MonoBehaviour
    {
        public enum PlayerAttentionLevel { Ignore, Glance, Observe }

        public enum PlayerSalienceStimulus { None, Footstep, Jump, VisualAcquire, ThrowHit }

        [Header("References (auto-found if left empty)")]
        [SerializeField] private CreaturePerception perception;
        [SerializeField] private CreatureHearing hearing;
        [SerializeField] private CreatureObservation observation;
        [SerializeField] private CreatureEncounterObservation encounter;

        [Header("Transient stimulus bonuses (added once per NEW edge)")]
        [Tooltip("Footstep bonus at the quietest heard tier (intensity == footstepIntensityReferenceMin, e.g. a heard Crouch step).")]
        [SerializeField] private float footstepBonusMin = 0.12f;
        [Tooltip("Footstep bonus at the loudest continuous tier (intensity == footstepIntensityReferenceMax, e.g. a heard Dash step).")]
        [SerializeField] private float footstepBonusMax = 0.30f;
        [Tooltip("Heard-Footstep intensity mapped to footstepBonusMin. Matches PlayerMovementSoundEmitter's default Crouch intensity.")]
        [SerializeField] private float footstepIntensityReferenceMin = 0.25f;
        [Tooltip("Heard-Footstep intensity mapped to footstepBonusMax. Matches PlayerMovementSoundEmitter's default Dash intensity.")]
        [SerializeField] private float footstepIntensityReferenceMax = 0.90f;
        [SerializeField] private float jumpBonus = 0.35f;
        [SerializeField] private float visualAcquireBonus = 0.45f;
        [Tooltip("Large, deliberately close to maxing the score out - a confirmed thrown-object HIT on the Creature. See class summary: strength only, no meaning.")]
        [SerializeField] private float throwHitBonus = 1.0f;

        [Header("Persistent context floors (bounded - Mathf.Max only, never additive)")]
        [SerializeField] private float veryCloseDistance = 2.5f;
        [SerializeField] private float veryCloseFloor = 0.35f;
        [Tooltip("CreatureEncounterObservation.TowardCreatureSpeed at/above which the Player counts as FastApproach, m/s.")]
        [SerializeField] private float fastApproachSpeed = 2.0f;
        [SerializeField] private float fastApproachFloor = 0.30f;

        [Header("Decay")]
        [SerializeField] private float decayPerSecond = 0.25f;

        [Header("Attention level thresholds (hysteresis - enter > exit)")]
        [SerializeField] private float glanceEnterScore = 0.35f;
        [SerializeField] private float glanceExitScore = 0.20f;
        [SerializeField] private float observeEnterScore = 0.65f;
        [SerializeField] private float observeExitScore = 0.50f;

        [Header("Attention hold (Observe does not drop the instant score dips)")]
        [Tooltip("Seconds Observe is guaranteed to hold once entered, regardless of decay, before hysteresis is allowed to drop it.")]
        [SerializeField] private float minimumObserveDuration = 2.5f;
        [Tooltip("Seconds Observe is held after a ThrowHit specifically - longer than minimumObserveDuration, and combined with it via Mathf.Max (whichever is later wins).")]
        [SerializeField] private float throwHitAttentionHold = 4.0f;

        private float _score;
        private PlayerAttentionLevel _level = PlayerAttentionLevel.Ignore;
        private float _attentionHoldUntil = -1f;

        private int _lastHeardSerial;
        private int _lastVisualSerial;
        private int _lastThrowHitSerial;

        private PlayerSalienceStimulus _lastStimulus = PlayerSalienceStimulus.None;
        private float _lastStimulusBonus;
        private float _lastStimulusTime = -1f;

        private bool _isVeryClose;
        private bool _isFastApproach;

        /// <summary>Current 0..1 salience score.</summary>
        public float Score => _score;

        /// <summary>Current thresholded attention level - only Observe visibly does anything (see class summary).</summary>
        public PlayerAttentionLevel AttentionLevel => _level;

        /// <summary>Kind of the most recent stimulus edge that added a bonus. None before the first one.</summary>
        public PlayerSalienceStimulus LastStimulus => _lastStimulus;

        /// <summary>Bonus applied by <see cref="LastStimulus"/>.</summary>
        public float LastStimulusBonus => _lastStimulusBonus;

        /// <summary>Time.time of the most recent stimulus edge. -1 before the first one.</summary>
        public float LastStimulusTime => _lastStimulusTime;

        /// <summary>True this frame while the VeryClose persistent-context floor is being applied.</summary>
        public bool IsVeryClose => _isVeryClose;

        /// <summary>True this frame while the FastApproach persistent-context floor is being applied.</summary>
        public bool IsFastApproach => _isFastApproach;

        /// <summary>Seconds remaining on the current attention hold (Observe guaranteed at least this long). 0 if not holding.</summary>
        public float AttentionHoldRemaining => Mathf.Max(0f, _attentionHoldUntil - Time.time);

        public int FootstepStimulusCount { get; private set; }
        public int JumpStimulusCount { get; private set; }
        public int VisualAcquireStimulusCount { get; private set; }
        public int ThrowHitStimulusCount { get; private set; }

        private void Awake()
        {
            if (perception == null)
                perception = GetComponent<CreaturePerception>();
            if (hearing == null)
                hearing = GetComponent<CreatureHearing>();
            if (hearing == null)
                hearing = FindFirstObjectByType<CreatureHearing>();
            if (observation == null)
                observation = GetComponent<CreatureObservation>();
            if (observation == null)
                observation = FindFirstObjectByType<CreatureObservation>();
            if (encounter == null)
                encounter = FindFirstObjectByType<CreatureEncounterObservation>();

            // Snapshot current serials so only NEW edges after this component starts count.
            if (hearing != null) _lastHeardSerial = hearing.HeardStimulusSerial;
            if (encounter != null) _lastVisualSerial = encounter.VisualAcquiredSerial;
            if (observation != null) _lastThrowHitSerial = observation.ThrowHitSerial;
        }

        private void OnDisable()
        {
            if (perception != null)
                perception.SetSalienceObservePoint(null); // never leave the gaze pinned by a component that just disabled
            _level = PlayerAttentionLevel.Ignore;
            _attentionHoldUntil = -1f;
        }

        // LateUpdate - same reasoning used throughout this project's Brain/Perception work: everything
        // this reads (Hearing's orient, the encounter sensor's distance/speed, the observation's throw
        // hit) is itself settled by LateUpdate/Update earlier in the frame; this only ever WRITES the
        // gaze-only seam, never anything read by another Update() this same frame.
        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            float now = Time.time;

            ApplyDecay(dt);
            ApplyStimulusEdges(now);
            ApplyPersistentFloors();
            _score = Mathf.Clamp01(_score);

            UpdateAttentionLevel(now);

            if (perception != null)
            {
                bool observing = _level == PlayerAttentionLevel.Observe && perception.Player != null;
                perception.SetSalienceObservePoint(observing ? perception.Player.position : (Vector3?)null);
            }
        }

        private void ApplyDecay(float dt)
        {
            _score = Mathf.Max(0f, _score - decayPerSecond * dt);
        }

        private void ApplyStimulusEdges(float now)
        {
            if (hearing != null && hearing.HeardStimulusSerial != _lastHeardSerial)
            {
                _lastHeardSerial = hearing.HeardStimulusSerial;
                if (hearing.LastHeardType == SoundStimulusType.Jump)
                {
                    AddStimulus(PlayerSalienceStimulus.Jump, jumpBonus, now);
                }
                else if (hearing.LastHeardType == SoundStimulusType.Footstep)
                {
                    float t = Mathf.InverseLerp(footstepIntensityReferenceMin, footstepIntensityReferenceMax, hearing.LastHeardIntensity);
                    float bonus = Mathf.Lerp(footstepBonusMin, footstepBonusMax, Mathf.Clamp01(t));
                    AddStimulus(PlayerSalienceStimulus.Footstep, bonus, now);
                }
            }

            if (encounter != null && encounter.VisualAcquiredSerial != _lastVisualSerial)
            {
                _lastVisualSerial = encounter.VisualAcquiredSerial;
                AddStimulus(PlayerSalienceStimulus.VisualAcquire, visualAcquireBonus, now);
            }

            if (observation != null && observation.ThrowHitSerial != _lastThrowHitSerial)
            {
                _lastThrowHitSerial = observation.ThrowHitSerial;
                AddStimulus(PlayerSalienceStimulus.ThrowHit, throwHitBonus, now);
                // Dedicated hold, independent of (and typically longer than) the normal
                // minimumObserveDuration granted once AttentionLevel actually reaches Observe below -
                // combined via Mathf.Max so whichever is later wins.
                _attentionHoldUntil = Mathf.Max(_attentionHoldUntil, now + throwHitAttentionHold);
            }
        }

        private void AddStimulus(PlayerSalienceStimulus kind, float bonus, float now)
        {
            _score = Mathf.Clamp01(_score + bonus);
            _lastStimulus = kind;
            _lastStimulusBonus = bonus;
            _lastStimulusTime = now;

            switch (kind)
            {
                case PlayerSalienceStimulus.Footstep: FootstepStimulusCount++; break;
                case PlayerSalienceStimulus.Jump: JumpStimulusCount++; break;
                case PlayerSalienceStimulus.VisualAcquire: VisualAcquireStimulusCount++; break;
                case PlayerSalienceStimulus.ThrowHit: ThrowHitStimulusCount++; break;
            }
        }

        private void ApplyPersistentFloors()
        {
            _isVeryClose = false;
            _isFastApproach = false;

            if (encounter == null)
                return;

            _isVeryClose = encounter.DistanceToCreature <= veryCloseDistance;
            if (_isVeryClose)
                _score = Mathf.Max(_score, veryCloseFloor);

            _isFastApproach = encounter.TowardCreatureSpeed >= fastApproachSpeed;
            if (_isFastApproach)
                _score = Mathf.Max(_score, fastApproachFloor);
        }

        private void UpdateAttentionLevel(float now)
        {
            PlayerAttentionLevel raw = ComputeLevel(_level, _score);

            // Rising into Observe for the first time this "visit" grants the minimum hold - a
            // standing Observe decision should not itself flicker off the instant score dips below
            // observeExitScore for one frame.
            if (raw == PlayerAttentionLevel.Observe && _level != PlayerAttentionLevel.Observe)
                _attentionHoldUntil = Mathf.Max(_attentionHoldUntil, now + minimumObserveDuration);

            bool holding = now < _attentionHoldUntil;
            _level = (holding && raw != PlayerAttentionLevel.Observe) ? PlayerAttentionLevel.Observe : raw;
        }

        // Hysteresis: which threshold applies depends on which side we are CURRENTLY on, so a state
        // never flickers right at a single boundary value. A big enough single-frame bonus (ThrowHit)
        // can still jump Ignore -> Observe directly in one frame - that is intended, not a bug.
        private PlayerAttentionLevel ComputeLevel(PlayerAttentionLevel prev, float score)
        {
            if (prev == PlayerAttentionLevel.Observe)
            {
                if (score >= observeExitScore) return PlayerAttentionLevel.Observe;
                return score >= glanceExitScore ? PlayerAttentionLevel.Glance : PlayerAttentionLevel.Ignore;
            }

            if (prev == PlayerAttentionLevel.Glance)
            {
                if (score >= observeEnterScore) return PlayerAttentionLevel.Observe;
                return score >= glanceExitScore ? PlayerAttentionLevel.Glance : PlayerAttentionLevel.Ignore;
            }

            // prev == Ignore
            if (score >= observeEnterScore) return PlayerAttentionLevel.Observe;
            return score >= glanceEnterScore ? PlayerAttentionLevel.Glance : PlayerAttentionLevel.Ignore;
        }

        private void OnValidate()
        {
            footstepBonusMin = Mathf.Clamp01(footstepBonusMin);
            footstepBonusMax = Mathf.Clamp01(footstepBonusMax);
            jumpBonus = Mathf.Clamp01(jumpBonus);
            visualAcquireBonus = Mathf.Clamp01(visualAcquireBonus);
            throwHitBonus = Mathf.Clamp01(throwHitBonus);

            veryCloseDistance = Mathf.Max(0f, veryCloseDistance);
            veryCloseFloor = Mathf.Clamp01(veryCloseFloor);
            fastApproachSpeed = Mathf.Max(0f, fastApproachSpeed);
            fastApproachFloor = Mathf.Clamp01(fastApproachFloor);

            decayPerSecond = Mathf.Max(0f, decayPerSecond);

            glanceExitScore = Mathf.Clamp01(glanceExitScore);
            glanceEnterScore = Mathf.Clamp(glanceEnterScore, glanceExitScore, 1f);
            observeExitScore = Mathf.Clamp(observeExitScore, glanceEnterScore, 1f);
            observeEnterScore = Mathf.Clamp(observeEnterScore, observeExitScore, 1f);

            minimumObserveDuration = Mathf.Max(0f, minimumObserveDuration);
            throwHitAttentionHold = Mathf.Max(0f, throwHitAttentionHold);
        }
    }
}
