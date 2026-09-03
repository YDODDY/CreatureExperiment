using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// The creature's low-level locomotion. It owns the creature root's XZ position and each frame
    /// runs exactly one of two movement verbs.
    ///
    /// Retreat (0.1): while the player is perceived and closer than <see cref="personalSpace"/>
    /// (flat XZ, root to root), translate straight away from the player at <see cref="retreatSpeed"/>
    /// until the gap re-opens to <see cref="comfortDistance"/>. Hysteresis on <see cref="_retreating"/>
    /// so it does not stutter at the boundary.
    ///
    /// Approach (0.1): when not retreating and still farther than <see cref="approachStopDistance"/>,
    /// walk toward <see cref="approachTarget"/> - one explicitly assigned interactable, never
    /// auto-selected, never read from the gaze target. Pure per-frame pursuit of its current
    /// position: no prediction, no path.
    ///
    /// Inspect (0.1): once inside <see cref="approachStopDistance"/>, hold still and watch for a
    /// random <see cref="inspectDwellMin"/>..<see cref="inspectDwellMax"/> seconds, then slide
    /// around the target (staying on the ring of that radius) by a random angular hop to a new
    /// viewing spot, and dwell again. Repeats. Not a continuous orbit - a "watch / move / watch"
    /// rhythm. Gaze / head / pupil keep tracking it throughout (CreaturePerception, untouched).
    ///
    /// Priority is just Retreat &gt; (Approach | Inspect); one movement per frame, never blended.
    /// A temporary arbitration for the prototype, not an AI. Deliberately tiny: no NavMesh, no
    /// pathfinding, no obstacle avoidance, no body rotation, no acceleration, no state machine, no
    /// vector blending. Walking through walls and chasing thrown objects are accepted this pass.
    /// <see cref="CreaturePerception"/> drives gaze / head / pupil independently and is not touched.
    /// </summary>
    [RequireComponent(typeof(CreaturePerception))]
    public class CreatureMovement : MonoBehaviour
    {
        [Header("Distances (metres, flat XZ)")]
        [Tooltip("Player at or inside this distance makes the creature start backing away.")]
        [SerializeField] private float personalSpace = 2f;
        [Tooltip("Creature stops once the gap has opened back up to this. Kept above personalSpace.")]
        [SerializeField] private float comfortDistance = 3.25f;

        [Header("Movement")]
        [Tooltip("Constant retreat speed. No ease in / ease out this pass.")]
        [SerializeField] private float retreatSpeed = 1.2f;

        [Header("Approach")]
        [Tooltip("The one interactable the creature walks toward. None -> no approach. Independent of what it looks at.")]
        [SerializeField] private Interactable approachTarget;
        [Tooltip("Stop moving once flat XZ distance (root to root) is within this.")]
        [SerializeField] private float approachStopDistance = 1.5f;
        [Tooltip("Constant approach speed. Deliberately separate from retreatSpeed.")]
        [SerializeField] private float approachSpeed = 1.2f;

        [Header("Inspect")]
        [Tooltip("Min seconds to hold still watching the target before moving to a new spot.")]
        [SerializeField] private float inspectDwellMin = 4f;
        [Tooltip("Max seconds to hold still watching the target before moving to a new spot.")]
        [SerializeField] private float inspectDwellMax = 10f;
        [Tooltip("Speed while sliding around the target to the next spot, in metres per second.")]
        [SerializeField] private float inspectMoveSpeed = 0.9f;
        [Tooltip("Smallest angular hop to the next viewing spot, in degrees.")]
        [SerializeField] private float inspectStepMin = 40f;
        [Tooltip("Largest angular hop to the next viewing spot, in degrees.")]
        [SerializeField] private float inspectStepMax = 120f;

        private CreaturePerception _perception;
        private bool _retreating;
        private bool _inspecting;        // currently in an inspect session at approachTarget
        private bool _inspectDwelling;   // true = holding still watching, false = sliding to next spot
        private float _inspectTimer;     // seconds left in the current dwell
        private float _inspectTargetAngle; // bearing around the target we hold / move toward, degrees

        private void Awake()
        {
            _perception = GetComponent<CreaturePerception>();
        }

        private void Update()
        {
            UpdateRetreatState();

            // Retreat always wins and ends any inspect session.
            if (_retreating)
            {
                _inspecting = false;
                RetreatStep();
                return;
            }

            if (approachTarget == null)
            {
                _inspecting = false;
                return;
            }

            Vector3 flat = approachTarget.transform.position - transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;

            if (distance > approachStopDistance + 0.1f)
            {
                // Still closing in -> straight-line Approach; no inspect session yet.
                _inspecting = false;
                ApproachStep();
            }
            else
            {
                // Arrived -> watch / move / watch rhythm around the target.
                InspectStep();
            }
        }

        // Decides whether the player is crowding the creature. Sets _retreating only; never moves.
        // When the player is missing / not perceived it just clears the flag and returns, so the
        // rest of Update() (Approach) can still run.
        private void UpdateRetreatState()
        {
            if (!_perception.IsPlayerPerceived || _perception.Player == null)
            {
                _retreating = false;
                return;
            }

            Vector3 flat = transform.position - _perception.Player.position;
            flat.y = 0f;
            float distance = flat.magnitude;

            // Cross in below personalSpace, cross out above comfortDistance, hold in the band.
            if (!_retreating && distance <= personalSpace)
                _retreating = true;
            else if (_retreating && distance >= comfortDistance)
                _retreating = false;
        }

        // Straight away from the player, flat XZ translation, constant speed. Only reached while
        // _retreating, which implies the player exists.
        private void RetreatStep()
        {
            Vector3 away = transform.position - _perception.Player.position;
            away.y = 0f;
            float distance = away.magnitude;

            // Player essentially on top of the creature: pick any flat direction so it still moves off.
            away = distance > 0.0001f ? away / distance : -transform.right;

            transform.position += away * (retreatSpeed * Time.deltaTime);
        }

        // Straight toward approachTarget's current position, flat XZ, constant speed, stop inside
        // approachStopDistance. Pure pursuit - the target's live transform, no prediction.
        private void ApproachStep()
        {
            if (approachTarget == null)
                return;

            Vector3 toward = approachTarget.transform.position - transform.position;
            toward.y = 0f;
            float distance = toward.magnitude;

            if (distance <= approachStopDistance)
                return;

            toward /= distance;
            transform.position += toward * (approachSpeed * Time.deltaTime);
        }

        // Watch / move / watch around approachTarget. Dwell still for a random time, then slide along
        // the ring (radius approachStopDistance) by a random angular hop to a new spot, then dwell
        // again. Position is always pinned to the ring, flat XZ; Y is never touched. The target's
        // live position is re-read each frame, so a moving object is followed at the same bearing.
        private void InspectStep()
        {
            Vector3 targetPos = approachTarget.transform.position;
            Vector3 flat = transform.position - targetPos;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
                flat = Vector3.forward; // degenerate: creature on top of target, pick any bearing

            float currentAngle = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;

            // First frame of a fresh inspect session: settle where we arrived and start dwelling.
            if (!_inspecting)
            {
                _inspecting = true;
                _inspectDwelling = true;
                _inspectTimer = Random.Range(inspectDwellMin, inspectDwellMax);
                _inspectTargetAngle = currentAngle;
            }

            if (_inspectDwelling)
            {
                _inspectTimer -= Time.deltaTime;
                if (_inspectTimer <= 0f)
                {
                    // Pick the next viewing spot: a random angular hop, random direction.
                    float hop = Random.Range(inspectStepMin, inspectStepMax);
                    if (Random.value < 0.5f)
                        hop = -hop;
                    _inspectTargetAngle = currentAngle + hop;
                    _inspectDwelling = false;
                }
            }
            else
            {
                // Slide along the ring toward the chosen bearing.
                float degPerSec = (inspectMoveSpeed / Mathf.Max(approachStopDistance, 0.01f)) * Mathf.Rad2Deg;
                currentAngle = Mathf.MoveTowardsAngle(currentAngle, _inspectTargetAngle, degPerSec * Time.deltaTime);

                if (Mathf.Abs(Mathf.DeltaAngle(currentAngle, _inspectTargetAngle)) < 0.5f)
                {
                    _inspectDwelling = true;
                    _inspectTimer = Random.Range(inspectDwellMin, inspectDwellMax);
                }
            }

            // Pin to the ring at currentAngle, distance approachStopDistance.
            float rad = currentAngle * Mathf.Deg2Rad;
            Vector3 pinned = targetPos + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * approachStopDistance;
            transform.position = new Vector3(pinned.x, transform.position.y, pinned.z);
        }

        private void OnValidate()
        {
            personalSpace = Mathf.Max(0f, personalSpace);
            comfortDistance = Mathf.Max(personalSpace + 0.1f, comfortDistance);
            retreatSpeed = Mathf.Max(0f, retreatSpeed);
            approachStopDistance = Mathf.Max(0f, approachStopDistance);
            approachSpeed = Mathf.Max(0f, approachSpeed);
            inspectDwellMin = Mathf.Max(0f, inspectDwellMin);
            inspectDwellMax = Mathf.Max(inspectDwellMin, inspectDwellMax);
            inspectMoveSpeed = Mathf.Max(0f, inspectMoveSpeed);
            inspectStepMin = Mathf.Max(0f, inspectStepMin);
            inspectStepMax = Mathf.Max(inspectStepMin, inspectStepMax);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.2f); // personal space
            DrawFlatCircle(personalSpace);
            Gizmos.color = new Color(0.2f, 0.8f, 1f); // comfort distance
            DrawFlatCircle(comfortDistance);

            Gizmos.color = new Color(0.4f, 1f, 0.4f); // approach stop distance
            DrawFlatCircle(approachStopDistance);
            if (approachTarget != null)
                Gizmos.DrawLine(transform.position, approachTarget.transform.position);
        }

        private void DrawFlatCircle(float radius)
        {
            const int segments = 48;
            Vector3 centre = transform.position;
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
