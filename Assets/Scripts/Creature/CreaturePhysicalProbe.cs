using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Physical Probe (0.1): while the creature is holding still on an Inspect dwell it occasionally
    /// reaches ONE primitive arm pivot out toward the object it is inspecting and, at the moment the
    /// arm is extended, gives that object's Rigidbody a small impulse so it visibly twitches - the
    /// beat reads as "watch ... then a poke". The arm then eases back to its rest pose.
    ///
    /// It never grabs, carries or throws - exactly one <see cref="Rigidbody.AddForce"/> per poke,
    /// then it lets go. If the nudge pushes the object out of reach, <see cref="CreatureMovement"/>'s
    /// own Approach / Inspect follow it as usual; nothing here touches that.
    ///
    /// Deliberately tiny: no hand, no IK, no Animator, no clip. Just a timed world-space slerp of one
    /// arm pivot plus one impulse. It runs in <see cref="LateUpdate"/>, so ONLY while a poke is
    /// playing does it override <see cref="CreatureBodyExpression"/>'s rest-pose write for that single
    /// arm; every other frame all four limbs, plus body orientation, gaze, head and pupil, are left
    /// entirely alone. <see cref="CreatureMovement"/> is read only through its public inspect seams.
    /// </summary>
    [RequireComponent(typeof(CreatureMovement))]
    public class CreaturePhysicalProbe : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Arm pivot that does the poking - an empty transform at a shoulder, child of BodyVisual. Its local -Y (the visual arm) is what gets aimed at the object.")]
        [SerializeField] private Transform probeArm;
        [Tooltip("Sibling CreatureMovement. Read only, for its inspect state and target. Auto-found if left empty.")]
        [SerializeField] private CreatureMovement movement;

        [Header("Poke force")]
        [Tooltip("Impulse magnitude applied to the object's Rigidbody at contact (kg*m/s). Keep it a nudge; above ~6 the object leaves reach fast enough that Approach visibly takes over.")]
        [SerializeField] private float pokeForce = 2f;
        [Tooltip("Extra fraction of the impulse added straight up, so the object hops slightly instead of only sliding.")]
        [Range(0f, 1f)]
        [SerializeField] private float pokeUpFraction = 0.12f;

        [Header("Arm motion")]
        [Tooltip("Seconds for the arm to swing out from rest to full reach.")]
        [SerializeField] private float reachDuration = 0.16f;
        [Tooltip("Seconds the arm stays extended at full reach. The impulse lands as this begins.")]
        [SerializeField] private float holdDuration = 0.06f;
        [Tooltip("Seconds for the arm to ease back from full reach to rest.")]
        [SerializeField] private float returnDuration = 0.28f;
        [Tooltip("How far toward a straight point-at-the-object aim the arm goes. 1 = arm points right at it.")]
        [Range(0f, 1f)]
        [SerializeField] private float reachAmount = 0.85f;

        [Header("Cadence (time only accrues while inspect-dwelling)")]
        [Tooltip("Minimum dwell seconds between pokes.")]
        [SerializeField] private float pokeIntervalMin = 3f;
        [Tooltip("Maximum dwell seconds between pokes.")]
        [SerializeField] private float pokeIntervalMax = 7f;
        [Tooltip("Only poke when the object's flat XZ distance is within this. Keep at or above the movement's approachStopDistance.")]
        [SerializeField] private float maxPokeDistance = 1.9f;

        private enum Phase { Idle, Reaching, Holding, Returning }

        private Quaternion _restLocalRotation;
        private Phase _phase = Phase.Idle;
        private float _phaseTimer;
        private float _cooldown;
        private Interactable _pokeTarget; // captured when a probe starts, cleared when it ends

        private void Awake()
        {
            if (movement == null)
                movement = GetComponent<CreatureMovement>();
            if (probeArm != null)
                _restLocalRotation = probeArm.localRotation;
            _cooldown = Random.Range(pokeIntervalMin, pokeIntervalMax);
        }

        private void Update()
        {
            bool dwelling = movement != null && movement.IsInspectDwelling;

            if (_phase == Phase.Idle)
            {
                // Countdown only runs down while actually holding still and watching.
                if (!dwelling)
                    return;

                _cooldown -= Time.deltaTime;
                if (_cooldown <= 0f && TryPickTarget(out _pokeTarget))
                {
                    _phase = Phase.Reaching;
                    _phaseTimer = 0f;
                }
                return;
            }

            // A probe is playing. If the inspect session was cut short before contact - Retreat, or
            // Attention moving to another object / the player - bail out of the reach without poking.
            // Keep _pokeTarget so the arm still eases back against the object it was reaching for
            // rather than snapping; it is cleared normally when the return finishes. A hold/return
            // already in progress just plays out.
            if (_phase == Phase.Reaching && (movement == null || !movement.IsInspecting))
            {
                _phase = Phase.Returning;
                _phaseTimer = 0f;
                return;
            }

            _phaseTimer += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Reaching:
                    if (_phaseTimer >= reachDuration)
                    {
                        ApplyPoke();
                        _phase = Phase.Holding;
                        _phaseTimer = 0f;
                    }
                    break;

                case Phase.Holding:
                    if (_phaseTimer >= holdDuration)
                    {
                        _phase = Phase.Returning;
                        _phaseTimer = 0f;
                    }
                    break;

                case Phase.Returning:
                    if (_phaseTimer >= returnDuration)
                    {
                        _phase = Phase.Idle;
                        _phaseTimer = 0f;
                        _pokeTarget = null;
                        _cooldown = Random.Range(pokeIntervalMin, pokeIntervalMax);
                    }
                    break;
            }
        }

        // Pose the arm for the current phase. LateUpdate so this wins over CreatureBodyExpression's
        // per-frame rest write, but only for the handful of frames a probe actually runs.
        private void LateUpdate()
        {
            if (probeArm == null || _phase == Phase.Idle)
                return;

            float raw = _phase switch
            {
                Phase.Reaching => reachDuration > 0f ? Mathf.Clamp01(_phaseTimer / reachDuration) : 1f,
                Phase.Holding => 1f,
                Phase.Returning => returnDuration > 0f ? 1f - Mathf.Clamp01(_phaseTimer / returnDuration) : 0f,
                _ => 0f,
            };
            float t = Mathf.SmoothStep(0f, 1f, raw) * reachAmount;

            Quaternion restWorld = probeArm.parent != null
                ? probeArm.parent.rotation * _restLocalRotation
                : _restLocalRotation;

            Quaternion aimWorld = restWorld;
            if (_pokeTarget != null)
            {
                Vector3 dir = _pokeTarget.transform.position - probeArm.position;
                if (dir.sqrMagnitude > 1e-6f)
                {
                    // The arm capsule hangs along the pivot's local -Y; this maps local -Y onto dir.
                    aimWorld = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(-90f, 0f, 0f);
                }
            }

            probeArm.rotation = Quaternion.Slerp(restWorld, aimWorld, t);
        }

        // Simplest possible: the object CreatureMovement is already inspecting, if it is in reach.
        private bool TryPickTarget(out Interactable target)
        {
            target = movement != null ? movement.InspectTarget : null;
            if (target == null || target.Body == null)
                return false;

            Vector3 flat = target.transform.position - transform.position;
            flat.y = 0f;
            return flat.magnitude <= maxPokeDistance;
        }

        private void ApplyPoke()
        {
            if (_pokeTarget == null || _pokeTarget.Body == null)
                return;

            Vector3 flat = _pokeTarget.transform.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
                return;

            Vector3 push = flat.normalized + Vector3.up * pokeUpFraction;
            _pokeTarget.Body.AddForce(push * pokeForce, ForceMode.Impulse);
        }

        private void OnValidate()
        {
            pokeForce = Mathf.Max(0f, pokeForce);
            reachDuration = Mathf.Max(0f, reachDuration);
            holdDuration = Mathf.Max(0f, holdDuration);
            returnDuration = Mathf.Max(0f, returnDuration);
            pokeIntervalMin = Mathf.Max(0f, pokeIntervalMin);
            pokeIntervalMax = Mathf.Max(pokeIntervalMin, pokeIntervalMax);
            maxPokeDistance = Mathf.Max(0f, maxPokeDistance);
        }

        private void OnDrawGizmosSelected()
        {
            if (probeArm == null)
                return;

            Gizmos.color = _phase == Phase.Idle ? new Color(1f, 0.6f, 0.2f, 0.5f) : new Color(1f, 0.6f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, maxPokeDistance);

            Transform t = movement != null && movement.InspectTarget != null ? movement.InspectTarget.transform : null;
            if (t != null)
                Gizmos.DrawLine(probeArm.position, t.position);
        }
    }
}
