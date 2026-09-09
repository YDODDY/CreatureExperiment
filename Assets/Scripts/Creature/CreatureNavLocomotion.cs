using UnityEngine;
using UnityEngine.AI;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Navigation 0.1: a path-following primitive - NOT a mover, NOT a decision-maker. It wraps a
    /// <see cref="NavMeshAgent"/> used purely as a path oracle (<c>updatePosition</c> and
    /// <c>updateRotation</c> both forced false). The movers that already open-code "step toward a
    /// point" - <see cref="CreatureMovement"/>'s Object Approach and Probe approach,
    /// <see cref="CreaturePlayerObserve"/>'s hop - call <see cref="MoveToward"/> with the SAME
    /// destination they always chose, and it advances the creature root one frame-step along the
    /// NavMesh path toward it instead of one step in a straight line. WHICH point and WHY stay
    /// entirely with the caller. Retreat / Inspect / Wander / Jump keep their own straight-line writes.
    ///
    /// When no NavMesh is usable this frame (not baked, no <see cref="NavMeshAgent"/>, agent disabled,
    /// or off-mesh) every call is the exact straight-line step it replaced, so behaviour is unchanged
    /// until a surface is baked.
    ///
    /// It also exposes just enough for the ONE give-up condition that had to become path-aware
    /// (CreatureProbe's delivery): <see cref="IsProgressing"/> - is the distance to the destination
    /// actually shrinking (path length while navigating, straight-line in fallback, held true briefly
    /// while a fresh path computes) - and <see cref="DistanceToDestination"/> - the NavMesh path
    /// length to a point when a COMPLETE path to it exists, straight-line otherwise. No failure /
    /// recovery state machine; the only re-pathing policy is "re-issue the destination when it moves".
    /// </summary>
    public class CreatureNavLocomotion : MonoBehaviour
    {
        [Header("Path following")]
        [Tooltip("Re-issue the NavMesh destination only when the requested point has moved at least this far (m). Cheap-out for a target that barely drifts.")]
        [SerializeField] private float repathThreshold = 0.3f;

        [Header("Progress check (read by CreatureProbe's give-up only, never by movement)")]
        [Tooltip("How often to sample whether the distance to the destination is still shrinking, in seconds.")]
        [SerializeField] private float progressCheckInterval = 0.5f;
        [Tooltip("The distance to the destination must drop by at least this much (m) per check to count as 'still progressing'.")]
        [SerializeField] private float progressEpsilon = 0.1f;

        [Header("State (read-only, for debugging)")]
        [Tooltip("A usable NavMesh path drove the last MoveToward (false = straight-line fallback).")]
        [SerializeField] private bool navActive;
        [Tooltip("The distance to the destination shrank over the last check interval.")]
        [SerializeField] private bool progressing;
        [Tooltip("Last sampled distance to the destination (path length when navigating, else straight-line).")]
        [SerializeField] private float remaining;

        private NavMeshAgent _agent;
        private Vector3 _lastRequestedDest;
        private bool _hasRequest;
        private float _progressTimer;
        private float _distAtLastCheck;

        /// <summary>True only when a baked, on-mesh NavMesh is actually available to path with; false means every <see cref="MoveToward"/> this frame is the straight-line fallback.</summary>
        public bool NavAvailable => _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh;

        /// <summary>
        /// True while the distance to the current destination is still coming down - path length when
        /// navigating, straight-line in fallback, and held true briefly while a fresh path is still
        /// computing. False once it stalls (no path, blocked at a partial-path frontier, or arrived).
        /// Only CreatureProbe's delivery give-up consults this; movement never does.
        /// </summary>
        public bool IsProgressing => progressing;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (_agent != null)
            {
                _agent.updatePosition = false;
                _agent.updateRotation = false;
                if (_agent.isOnNavMesh)
                    _agent.Warp(transform.position);
            }
            ResetProgress();
        }

        /// <summary>
        /// Advance the root one frame-step toward <paramref name="worldDest"/> at <paramref name="speed"/>
        /// m/s - along the NavMesh path when one is available, otherwise straight there (the exact step
        /// this replaced). XZ only; Y is left to CreatureJump. The caller still decides whether to call
        /// this at all this frame and owns its own stop condition.
        /// </summary>
        public void MoveToward(Vector3 worldDest, float speed)
        {
            Vector3 pos = transform.position;
            Vector3 flat = worldDest - pos;
            flat.y = 0f;
            float straight = flat.magnitude;

            bool usedNav = false;
            Vector3 stepDir = Vector3.zero;

            if (NavAvailable)
            {
                // Keep the agent's internal simulation anchored to where we actually are (XZ only;
                // leave its own navmesh-height Y alone).
                Vector3 np = _agent.nextPosition;
                _agent.nextPosition = new Vector3(pos.x, np.y, pos.z);

                if (!_hasRequest ||
                    (worldDest - _lastRequestedDest).sqrMagnitude > repathThreshold * repathThreshold)
                {
                    _agent.SetDestination(worldDest);
                    _lastRequestedDest = worldDest;
                    _hasRequest = true;
                    ResetProgress();
                }

                Vector3 v = _agent.desiredVelocity;
                v.y = 0f;
                if (v.sqrMagnitude > 1e-4f)
                {
                    stepDir = v.normalized;
                    usedNav = true;
                }
            }

            if (!usedNav && straight > 1e-4f)
                stepDir = flat / straight; // straight-line fallback (or nav had nowhere to steer)

            if (stepDir != Vector3.zero)
                transform.position += stepDir * (speed * Time.deltaTime);

            navActive = usedNav;
            TrackProgress(worldDest, usedNav);
        }

        /// <summary>
        /// Distance to <paramref name="worldDest"/>: the NavMesh path length when a COMPLETE path to
        /// that exact point is available right now, otherwise the flat straight-line distance. A
        /// partial / pending / invalid path returns +Infinity so an "arrived" test never passes while
        /// the point is unreachable. Used by CreatureProbe so a wall between the creature and the
        /// player is measured by the route, not the gap.
        /// </summary>
        public float DistanceToDestination(Vector3 worldDest)
        {
            if (NavAvailable)
            {
                // Nav is usable -> judge by the route, and never report a small distance until a
                // COMPLETE path to this exact point has actually been computed. Pending / partial /
                // invalid / mismatched -> +Infinity, so an "arrived" test can't pass through a wall
                // or at a dead-end frontier.
                if (_hasRequest
                    && (_agent.destination - worldDest).sqrMagnitude < 1f
                    && _agent.hasPath && !_agent.pathPending
                    && _agent.pathStatus == NavMeshPathStatus.PathComplete)
                    return _agent.remainingDistance;
                return float.PositiveInfinity;
            }

            Vector3 flat = worldDest - transform.position; // fallback: straight-line, the original meaning
            flat.y = 0f;
            return flat.magnitude;
        }

        private void TrackProgress(Vector3 worldDest, bool usedNav)
        {
            if (usedNav && _agent.pathPending)
            {
                progressing = true; // don't let a give-up fire while a fresh path is still computing
                return;
            }

            float d;
            if (usedNav && _agent.hasPath && _agent.pathStatus == NavMeshPathStatus.PathComplete)
                d = _agent.remainingDistance;
            else
            {
                Vector3 flat = worldDest - transform.position;
                flat.y = 0f;
                d = flat.magnitude;
            }

            remaining = d;
            _progressTimer += Time.deltaTime;
            if (_progressTimer >= progressCheckInterval)
            {
                progressing = (_distAtLastCheck - d) > progressEpsilon;
                _distAtLastCheck = d;
                _progressTimer = 0f;
            }
        }

        private void ResetProgress()
        {
            _progressTimer = 0f;
            _distAtLastCheck = float.PositiveInfinity;
            progressing = true; // a freshly issued destination is never "stalled" on its first frames
        }

        private void OnValidate()
        {
            repathThreshold = Mathf.Max(0f, repathThreshold);
            progressCheckInterval = Mathf.Max(0.05f, progressCheckInterval);
            progressEpsilon = Mathf.Max(0f, progressEpsilon);
        }
    }
}
