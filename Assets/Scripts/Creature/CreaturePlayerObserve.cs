using UnityEngine;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Player Observe (0.1): what the creature does when the Player is the only thing it perceives.
    /// It shows interest without becoming a pet - it watches from a distance and, now and then,
    /// repositions a little closer so it can keep watching. NOT a follower.
    ///
    /// Flow: engage -> Observing (stand still, look at the Player for a random
    /// <see cref="observeTimeMin"/>..<see cref="observeTimeMax"/> s - gaze is already handled by
    /// <see cref="CreaturePerception"/>) -> if the Player is farther than <see cref="observeDistance"/>,
    /// take ONE short Approaching hop toward a SNAPSHOT of where the Player was when the hop began,
    /// capped at <see cref="approachMaxTime"/> s -> back to Observing. If the Player is already within
    /// <see cref="observeDistance"/> it just keeps watching. Repeat.
    ///
    /// Why it can never turn into an infinite follow:
    ///  - every hop is preceded by a full Observing pause, so movement is never continuous;
    ///  - a hop walks toward a STALE snapshot of the Player's position, not the live transform, so it
    ///    is structurally a "reposition toward where I last saw you", not a pursuit;
    ///  - a hop is time-capped, so it only ever closes a little of the gap;
    ///  - it only hops while the Player is beyond <see cref="observeDistance"/>; once inside, it settles;
    ///  - a Player walking away at normal speed out-paces the hop/observe cadence and leaves
    ///    perceptionRange, which ends Player Observe outright.
    ///
    /// Strictly below Retreat / Probe / Pickup / Object Attention-Approach-Inspect and strictly above
    /// <see cref="CreatureWander"/>. It reads <see cref="CreatureMovement.IsDrivingRoot"/> /
    /// <see cref="CreaturePerception"/> and translates the root's XZ directly, the same
    /// "transform.position += dir * speed * dt" step every other verb uses, so
    /// <see cref="CreatureWallCollision"/>, <see cref="CreatureBodyExpression"/> and
    /// <see cref="CreatureJump"/> compose with it for free. <see cref="CreatureWander"/> reads
    /// <see cref="IsEngaged"/> and stands down whenever it is true.
    ///
    /// Autonomous crouch (0.1): while genuinely in the Observing sub-state it MAY, with probability
    /// <see cref="autonomousCrouchChance"/>, drop into a low posture for
    /// <see cref="crouchDurationMin"/>..<see cref="crouchDurationMax"/> s and then stand - at most
    /// once per Observing beat, and only through <see cref="CreatureCrouch.SetCrouching"/> (the
    /// existing capability; no posture/visual code is duplicated here). It stands again on the hold
    /// timer, on Observing -> Approaching, and on every disengage path (object found, Retreat / Probe
    /// / Pickup, Player gone). Gaze is untouched - CreatureCrouch only lowers HeadPivot's position,
    /// never its rotation.
    ///
    /// Deliberately tiny: no Follow, no Trust / relationship / aggression reading, no Player action
    /// Pattern/Memory, no random behaviour picker, no Brain / Decision / Intent, no NavMesh /
    /// pathfinding (a Player behind a wall is just approached in a straight line and the hop times
    /// out), no crouch-as-hide / stealth check, no Player mimicry, no autonomous Jump / Dash. It
    /// never touches Attention, Approach, Inspect, the Probes or Wander - it only fills the
    /// "Player seen, nothing else" gap.
    /// </summary>
    [RequireComponent(typeof(CreatureMovement))]
    [RequireComponent(typeof(CreaturePerception))]
    [DefaultExecutionOrder(40)] // after CreatureMovement (0) so IsDrivingRoot is this frame's; before CreatureWander (50)
    public class CreaturePlayerObserve : MonoBehaviour
    {
        public enum ObserveState { Observing, Approaching }

        [Header("Observe")]
        [Tooltip("Flat XZ distance the creature is content to watch the Player from. It only repositions closer while the Player is farther than this, and stops each hop as soon as it is back within it. Keep well above CreatureMovement.personalSpace / comfortDistance so Retreat and Observe do not fight at the boundary.")]
        [SerializeField] private float observeDistance = 5f;
        [Tooltip("The Player must be this much farther than observeDistance before a new approach hop starts - stops twitching at the boundary.")]
        [SerializeField] private float approachHysteresis = 0.75f;
        [Tooltip("Minimum seconds spent just watching (standing still) before the creature will consider a reposition hop. Also the delay before the very first hop after the Player is spotted.")]
        [SerializeField] private float observeTimeMin = 3f;
        [Tooltip("Maximum seconds spent just watching before the creature will consider a reposition hop.")]
        [SerializeField] private float observeTimeMax = 7f;

        [Header("Reposition hop")]
        [Tooltip("Speed of a reposition hop, m/s. Kept slow so it reads as 'edging closer', not chasing.")]
        [SerializeField] private float approachSpeed = 0.9f;
        [Tooltip("Hard cap on a single hop, in seconds. hop distance is at most approachSpeed * this, so the creature only ever closes a little of the gap before it must watch again.")]
        [SerializeField] private float approachMaxTime = 2f;
        [Tooltip("Flat XZ distance at which the snapshot destination counts as reached.")]
        [SerializeField] private float approachArriveDistance = 0.4f;

        [Header("Autonomous crouch (reuses the CreatureCrouch capability)")]
        [Tooltip("Probability that a given Observing beat lowers into a crouch. 0 = never (as before), 1 = every beat. At most one crouch per beat regardless. Needs a CreatureCrouch sibling; without one, no crouch.")]
        [Range(0f, 1f)]
        [SerializeField] private float autonomousCrouchChance = 0.35f;
        [Tooltip("Minimum seconds to hold the low posture before standing back up.")]
        [SerializeField] private float crouchDurationMin = 1f;
        [Tooltip("Maximum seconds to hold the low posture before standing back up.")]
        [SerializeField] private float crouchDurationMax = 3f;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private ObserveState state = ObserveState.Observing;
        [Tooltip("Seconds spent in the current sub-state.")]
        [SerializeField] private float secondsInState;
        [Tooltip("Snapshot the current hop walks toward - where the Player was when the hop began. Meaningless unless state is Approaching.")]
        [SerializeField] private Vector3 approachTarget;
        [Tooltip("True while an autonomous crouch is currently being held (Observing only).")]
        [SerializeField] private bool crouchActive;

        /// <summary>
        /// True whenever Player Observe owns the creature's idle-time behaviour this frame - watching
        /// OR hopping. <see cref="CreatureWander"/> reads this and stands down while it is true.
        /// </summary>
        public bool IsEngaged { get; private set; }

        /// <summary>Current sub-state: standing and watching, or mid reposition hop.</summary>
        public ObserveState State => state;

        private CreatureMovement _movement;
        private CreaturePerception _perception;
        private CreaturePickup _pickup;   // optional
        private CreatureCrouch _crouch;   // optional - autonomous crouch is skipped entirely when absent

        private float _observeDuration;   // this Observing beat's rolled length
        private bool _crouchPlanned;      // this Observing beat rolled a crouch that has not finished yet
        private float _crouchStartAt;     // seconds into the beat at which to lower
        private float _crouchDuration;    // seconds to hold the low posture

        private void Awake()
        {
            _movement = GetComponent<CreatureMovement>();
            _perception = GetComponent<CreaturePerception>();
            _pickup = GetComponent<CreaturePickup>();
            _crouch = GetComponent<CreatureCrouch>();
            _observeDuration = Random.Range(observeTimeMin, observeTimeMax);
        }

        private void Update()
        {
            bool canObserve = _perception.IsPlayerPerceived
                              && _perception.Player != null
                              && _perception.AttendedInteractable == null   // an Interactable outranks the Player
                              && !_movement.IsDrivingRoot                    // Retreat / Probe-approach / Approach / Inspect
                              && (_pickup == null || _pickup.IsIdle);        // no grab / carry / probe carry

            if (!canObserve)
            {
                if (IsEngaged)
                {
                    // Disengaged (object found / Retreat / Probe / Pickup / Player gone) -> stand.
                    IsEngaged = false;
                    state = ObserveState.Observing;
                    secondsInState = 0f;
                    EnsureStanding();
                }
                return;
            }

            if (!IsEngaged)
            {
                // Rising edge: start by watching, never by moving.
                IsEngaged = true;
                state = ObserveState.Observing;
                BeginObservingBeat();
            }

            secondsInState += Time.deltaTime;

            switch (state)
            {
                case ObserveState.Observing:   TickObserving();   break;
                case ObserveState.Approaching: TickApproaching(); break;
            }
        }

        private void TickObserving()
        {
            TickAutonomousCrouch();

            if (secondsInState < _observeDuration)
                return;

            // Watched long enough. Reposition only if the Player is meaningfully far; otherwise settle
            // and keep watching from here.
            if (FlatDistance(_perception.Player.position) > observeDistance + approachHysteresis)
            {
                EnsureStanding();                              // stand before a hop
                approachTarget = _perception.Player.position;  // SNAPSHOT - not re-read while hopping
                state = ObserveState.Approaching;
                secondsInState = 0f;
            }
            else
            {
                BeginObservingBeat(); // still watching from here - fresh beat, fresh crouch roll
            }
        }

        // At most one crouch per Observing beat: lower at _crouchStartAt, stand again after
        // _crouchDuration. Purely a request to the existing CreatureCrouch capability.
        private void TickAutonomousCrouch()
        {
            if (!_crouchPlanned || _crouch == null)
                return;

            if (!crouchActive)
            {
                if (secondsInState >= _crouchStartAt)
                {
                    _crouch.SetCrouching(true);
                    crouchActive = true;
                }
            }
            else if (secondsInState >= _crouchStartAt + _crouchDuration)
            {
                _crouch.SetCrouching(false);
                crouchActive = false;
                _crouchPlanned = false; // done for this beat - no repeats
            }
        }

        // Start a fresh Observing beat: always stand first, roll a new watch length, then maybe plan
        // one crouch that fits inside it.
        private void BeginObservingBeat()
        {
            EnsureStanding();
            secondsInState = 0f;
            _observeDuration = Random.Range(observeTimeMin, observeTimeMax);

            _crouchPlanned = false;
            if (_crouch == null || autonomousCrouchChance <= 0f || Random.value >= autonomousCrouchChance)
                return;

            float dur = Random.Range(crouchDurationMin, crouchDurationMax);
            float earliest = 0.6f;                        // watch a beat before lowering
            float latest = _observeDuration - dur - 0.4f; // fully stand again before the beat ends
            if (latest <= earliest)
                return;                                   // not enough room this beat - skip

            _crouchDuration = dur;
            _crouchStartAt = Random.Range(earliest, latest);
            _crouchPlanned = true;
        }

        // Idempotent: force Stand and clear the beat's crouch plan. Called on every exit from a
        // watching beat (hop, re-roll, disengage) so a crouch never outlives the Observing state.
        private void EnsureStanding()
        {
            if (_crouch != null)
                _crouch.SetCrouching(false);
            crouchActive = false;
            _crouchPlanned = false;
        }

        private void TickApproaching()
        {
            Vector3 toSnap = approachTarget - transform.position;
            toSnap.y = 0f;
            float snapDist = toSnap.magnitude;

            // End the hop: reached the snapshot, back within observeDistance of the LIVE player, or the
            // hop's time cap hit. Any of these -> stop and watch again.
            if (snapDist <= approachArriveDistance
                || FlatDistance(_perception.Player.position) <= observeDistance
                || secondsInState >= approachMaxTime)
            {
                state = ObserveState.Observing;
                BeginObservingBeat();
                return;
            }

            transform.position += (toSnap / snapDist) * (approachSpeed * Time.deltaTime);
        }

        private float FlatDistance(Vector3 world)
        {
            Vector3 f = world - transform.position;
            f.y = 0f;
            return f.magnitude;
        }

        // Safety net: if this component is disabled while a crouch is being held, don't leave the
        // creature stuck low. (CreatureCrouch's own OnDisable only covers CreatureCrouch being disabled.)
        private void OnDisable()
        {
            IsEngaged = false;
            EnsureStanding();
        }

        private void OnValidate()
        {
            observeDistance = Mathf.Max(0.5f, observeDistance);
            approachHysteresis = Mathf.Max(0f, approachHysteresis);
            observeTimeMin = Mathf.Max(0f, observeTimeMin);
            observeTimeMax = Mathf.Max(observeTimeMin, observeTimeMax);
            approachSpeed = Mathf.Max(0f, approachSpeed);
            approachMaxTime = Mathf.Max(0.1f, approachMaxTime);
            approachArriveDistance = Mathf.Max(0.01f, approachArriveDistance);
            crouchDurationMin = Mathf.Max(0f, crouchDurationMin);
            crouchDurationMax = Mathf.Max(crouchDurationMin, crouchDurationMax);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
            DrawFlatCircle(transform.position, observeDistance);

            if (Application.isPlaying && IsEngaged && _perception != null && _perception.Player != null)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.3f);
                Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, _perception.Player.position + Vector3.up * 0.5f);
                if (state == ObserveState.Approaching)
                    Gizmos.DrawWireSphere(approachTarget, approachArriveDistance);
            }
        }

        private static void DrawFlatCircle(Vector3 centre, float radius)
        {
            const int segments = 44;
            Vector3 prev = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
