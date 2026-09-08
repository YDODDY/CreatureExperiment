using UnityEngine;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Local Wander (0.1): the creature's "nothing better to do" fidget. When
    /// <see cref="CreatureMovement"/> is not driving the root this frame (no Retreat, no Probe
    /// approach, no Approach/Inspect target), no <see cref="Interactable"/> is attended, and
    /// <see cref="CreaturePickup"/> is idle, the creature waits out a short random beat and then
    /// strolls to a random flat spot within a small radius, waits again, and repeats -
    /// Idle -> Wander -> Idle, never a continuous walk.
    ///
    /// Strictly the LOWEST priority locomotion. It translates the root's XZ only on frames nothing
    /// else does, with the exact same "transform.position += dir * speed * dt" step Retreat /
    /// Approach use - so <see cref="CreatureWallCollision"/> (depenetration in LateUpdate),
    /// <see cref="CreatureBodyExpression"/> (walk swing + body yaw from the frame delta) and
    /// <see cref="CreatureJump"/> (Y only) all compose with it for free. It reads
    /// <see cref="CreatureMovement.IsDrivingRoot"/> and yields the instant that flips true, so a real
    /// FOV/LOS object sighting, a Retreat, or a Probe takes the creature straight back mid-stroll.
    /// It also stands down while <see cref="CreaturePlayerObserve"/> is engaged, so a perceived
    /// Player outranks wandering.
    ///
    /// Deliberately tiny: no NavMesh / pathfinding, no path around walls (a destination behind a wall
    /// is simply abandoned on a no-progress or timeout check and a new one rolled later), no patrol,
    /// no deliberate room-to-room search, no Known Object / memory, no walking to a remembered
    /// object, no Brain / Decision / Intent, no following the player. It never reads or changes
    /// Attention, Approach, Inspect or the Probes - it only fills the idle gaps between them.
    /// </summary>
    [RequireComponent(typeof(CreatureMovement))]
    [RequireComponent(typeof(CreaturePerception))]
    [DefaultExecutionOrder(50)] // after CreatureMovement (0) so IsDrivingRoot is this frame's; before CreatureWallCollision (200)
    public class CreatureWander : MonoBehaviour
    {
        public enum WanderState { Idle, Wandering }

        [Header("Idle -> Wander rhythm")]
        [Tooltip("Minimum seconds of genuinely-free standing still before rolling a wander destination.")]
        [SerializeField] private float idleTimeMin = 3f;
        [Tooltip("Maximum seconds of genuinely-free standing still before rolling a wander destination.")]
        [SerializeField] private float idleTimeMax = 8f;

        [Header("Destination")]
        [Tooltip("Nearest a random destination may be picked, in metres from the creature's current position.")]
        [SerializeField] private float wanderRadiusMin = 1.5f;
        [Tooltip("Farthest a random destination may be picked, in metres from the creature's current position.")]
        [SerializeField] private float wanderRadius = 4f;
        [Tooltip("Flat XZ distance at which the destination counts as reached.")]
        [SerializeField] private float arriveDistance = 0.35f;

        [Header("Movement")]
        [Tooltip("Stroll speed, m/s. Kept at or a little below the creature's Approach speed so wander reads as unhurried.")]
        [SerializeField] private float wanderSpeed = 0.9f;

        [Header("Give up (no pathfinding - just abandon a blocked destination)")]
        [Tooltip("Hard cap: seconds allowed to reach one destination before abandoning it.")]
        [SerializeField] private float wanderTimeout = 6f;
        [Tooltip("How often to check whether the creature is still closing on the destination, in seconds.")]
        [SerializeField] private float stuckCheckInterval = 1f;
        [Tooltip("If the flat distance to the destination has not dropped by at least this much over one check interval, the destination is abandoned (wall in the way).")]
        [SerializeField] private float stuckMinProgress = 0.15f;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private WanderState state = WanderState.Idle;
        [Tooltip("Seconds spent in the current state - counts only frames where the creature was actually free to wander.")]
        [SerializeField] private float secondsInState;
        [Tooltip("Current wander destination, world space. Meaningless unless state is Wandering.")]
        [SerializeField] private Vector3 destination;

        /// <summary>Current wander phase. Idle = standing by; Wandering = strolling to the destination.</summary>
        public WanderState State => state;

        private CreatureMovement _movement;
        private CreaturePerception _perception;
        private CreaturePickup _pickup;   // optional
        private CreatureDash _dash;       // optional - lets a dev-test Dash speed the stroll too, like every other verb
        private CreaturePlayerObserve _observe; // optional - when engaged, Player Observe outranks wander

        private float _idleDuration;      // this Idle beat's rolled length
        private float _stuckTimer;        // seconds since the last progress check
        private float _lastDistToDest;    // flat distance measured at the last progress check

        private void Awake()
        {
            _movement = GetComponent<CreatureMovement>();
            _perception = GetComponent<CreaturePerception>();
            _pickup = GetComponent<CreaturePickup>();
            _dash = GetComponent<CreatureDash>();
            _observe = GetComponent<CreaturePlayerObserve>();
            RollIdle();
        }

        private void Update()
        {
            // Free to wander only when nothing more important is happening this frame.
            bool free = !_movement.IsDrivingRoot
                        && _perception.AttendedInteractable == null
                        && (_pickup == null || _pickup.IsIdle)
                        && (_observe == null || !_observe.IsEngaged);

            if (!free)
            {
                // Yield now. Drop any destination and hold Idle "fresh" so a wander does not pop the
                // instant control returns - the idle beat is measured in genuinely-free time only.
                state = WanderState.Idle;
                secondsInState = 0f;
                return;
            }

            secondsInState += Time.deltaTime;

            switch (state)
            {
                case WanderState.Idle:
                    if (secondsInState >= _idleDuration)
                        EnterWander();
                    break;

                case WanderState.Wandering:
                    WanderStep();
                    break;
            }
        }

        private void WanderStep()
        {
            Vector3 toDest = destination - transform.position;
            toDest.y = 0f;
            float dist = toDest.magnitude;

            if (dist <= arriveDistance)
            {
                RollIdle();
                return;
            }

            if (secondsInState >= wanderTimeout)
            {
                RollIdle(); // took too long - give up, roll a new beat
                return;
            }

            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= stuckCheckInterval)
            {
                if (_lastDistToDest - dist < stuckMinProgress)
                {
                    RollIdle(); // not closing the gap - wall in the way, abandon this destination
                    return;
                }
                _lastDistToDest = dist;
                _stuckTimer = 0f;
            }

            float speed = (_dash != null && _dash.IsDashing) ? _dash.DashSpeed : wanderSpeed;
            transform.position += (toDest / dist) * (speed * Time.deltaTime);
        }

        private void EnterWander()
        {
            Vector2 dir = Random.insideUnitCircle;
            if (dir.sqrMagnitude < 1e-4f)
                dir = Vector2.up;
            dir.Normalize();
            float r = Random.Range(wanderRadiusMin, wanderRadius);

            destination = transform.position + new Vector3(dir.x, 0f, dir.y) * r;
            destination.y = transform.position.y;

            state = WanderState.Wandering;
            secondsInState = 0f;
            _stuckTimer = 0f;
            _lastDistToDest = r; // == the flat distance to a fresh destination
        }

        private void RollIdle()
        {
            state = WanderState.Idle;
            secondsInState = 0f;
            _idleDuration = Random.Range(idleTimeMin, idleTimeMax);
        }

        private void OnValidate()
        {
            idleTimeMin = Mathf.Max(0f, idleTimeMin);
            idleTimeMax = Mathf.Max(idleTimeMin, idleTimeMax);
            wanderRadiusMin = Mathf.Max(0f, wanderRadiusMin);
            wanderRadius = Mathf.Max(wanderRadiusMin + 0.01f, wanderRadius);
            arriveDistance = Mathf.Max(0.01f, arriveDistance);
            wanderSpeed = Mathf.Max(0f, wanderSpeed);
            wanderTimeout = Mathf.Max(0.5f, wanderTimeout);
            stuckCheckInterval = Mathf.Max(0.1f, stuckCheckInterval);
            stuckMinProgress = Mathf.Max(0f, stuckMinProgress);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.5f, 1f, 0.7f);
            DrawFlatCircle(transform.position, wanderRadiusMin);
            DrawFlatCircle(transform.position, wanderRadius);

            if (Application.isPlaying && state == WanderState.Wandering)
            {
                Gizmos.color = new Color(0.6f, 0.5f, 1f);
                Gizmos.DrawLine(transform.position, destination);
                Gizmos.DrawWireSphere(destination, arriveDistance);
            }
        }

        private static void DrawFlatCircle(Vector3 centre, float radius)
        {
            const int segments = 40;
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
