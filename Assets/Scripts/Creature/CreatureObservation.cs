using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Observation (0.1): listens for <see cref="PhysicalEvents"/> and reports two purely physical facts
    /// about Player release actions - no interpretation (Gift/Threat/Attack), no FOV/witness check, no
    /// Memory, no Decision/Intent.
    ///
    /// PLACE (step 2): logs the flat XZ distance between the placed <see cref="Interactable"/> and this
    /// creature's root, plus which spatial distance band it falls in (<see cref="veryCloseMax"/> /
    /// <see cref="nearMax"/>, tunable in the Inspector).
    ///
    /// THROW (step 3): does NOT log anything at throw time. Instead the thrown object is added to
    /// <see cref="_watchedThrows"/> (with a per-object "how long it has been still" timer) and watched
    /// two ways:
    ///   - HIT: <see cref="OnCollisionEnter"/> - this GameObject already carries the creature's own
    ///     <c>CapsuleCollider</c> (see the scene), so no extra collider or component is needed. A HIT is
    ///     logged the first time a watched object's collider actually touches the creature, and it is
    ///     removed from the set at that same moment (so repeated contact from one throw logs only once).
    ///   - MISS: <see cref="FixedUpdate"/> polls each watched object's <see cref="Rigidbody"/>. Once its
    ///     linear AND angular speed have both stayed below <see cref="settleLinearSpeed"/> /
    ///     <see cref="settleAngularSpeed"/> continuously for <see cref="settleTime"/>, the throw is
    ///     considered come-to-rest: it is removed from the set and its final flat XZ distance to the
    ///     creature is logged and classified with the same bands as PLACE.
    /// A watched object that stops being <see cref="Interactable.IsInFlight"/> for any other reason
    /// (the player catches it mid-air, or a HIT already resolved it) is dropped from the set silently -
    /// that is not a miss. There is deliberately no max-watch timeout yet: if throws are seen to never
    /// settle in practice, one gets added then. Closest-approach, trajectory, peak speed, FOV/witness
    /// checks and any semantic reading of the throw are still not computed here at all.
    ///
    /// Only <c>PlayerInteractor</c> raises <see cref="PhysicalEvents"/> right now (see its own class
    /// comment - the creature's own Place/Throw in <see cref="CreaturePickup"/> deliberately does not),
    /// so every event received here is already a Player action; no extra actor filtering added on top of
    /// that for now. That is also why the creature's own throws can never register a HIT: they never
    /// enter <see cref="_watchedThrows"/> in the first place.
    /// </summary>
    public class CreatureObservation : MonoBehaviour
    {
        [Header("Distance bands (metres, flat XZ) - spatial classification only, tune freely")]
        [Tooltip("Raw distance below this is VERY_CLOSE.")]
        [SerializeField] private float veryCloseMax = 1.0f;
        [Tooltip("Raw distance at/above veryCloseMax and below this is NEAR. At/above this is FAR.")]
        [SerializeField] private float nearMax = 2.5f;

        [Header("Throw settle (MISS detection)")]
        [Tooltip("A thrown object counts as 'moving' while its linear speed (m/s) is at/above this.")]
        [SerializeField] private float settleLinearSpeed = 0.05f;
        [Tooltip("...or while its angular speed (rad/s) is at/above this.")]
        [SerializeField] private float settleAngularSpeed = 0.5f;
        [Tooltip("Both speeds must stay below their thresholds continuously for this long (seconds) before the throw is logged as a MISS.")]
        [SerializeField] private float settleTime = 0.3f;

        private enum DistanceCategory { VeryClose, Near, Far }

        // Player-thrown objects currently being watched, mapped to how long (seconds) each has been
        // continuously below the settle speed thresholds. Added on THROW; removed on the first actual
        // collision with the creature (HIT), on coming to rest (MISS), or when it stops being in flight
        // for any other reason (mid-air re-pickup).
        private readonly Dictionary<Interactable, float> _watchedThrows = new Dictionary<Interactable, float>();

        // Reused each FixedUpdate so the watched set can be mutated while iterating it. Never holds
        // state between frames.
        private readonly List<Interactable> _settleWorkList = new List<Interactable>();

        private void OnEnable()
        {
            PhysicalEvents.Raised += OnPhysicalEvent;
        }

        private void OnDisable()
        {
            PhysicalEvents.Raised -= OnPhysicalEvent;
            _watchedThrows.Clear();
        }

        private void OnPhysicalEvent(PhysicalEvent evt)
        {
            if (evt.Interactable == null)
                return;

            if (evt.Kind == PhysicalEventKind.Place)
            {
                float distance = FlatDistance(evt.Interactable.transform.position);
                DistanceCategory category = Classify(distance);
                Debug.Log($"[CreatureObservation] PLACE {evt.Interactable.DisplayName} / Distance: {distance:F2}m / {CategoryLabel(category)}");
            }
            else if (evt.Kind == PhysicalEventKind.Throw)
            {
                _watchedThrows[evt.Interactable] = 0f;
            }
        }

        // MISS detection. Walk every watched throw; a throw resolves here only by coming to rest.
        private void FixedUpdate()
        {
            if (_watchedThrows.Count == 0)
                return;

            float linearSqrThreshold = settleLinearSpeed * settleLinearSpeed;
            float angularSqrThreshold = settleAngularSpeed * settleAngularSpeed;

            _settleWorkList.Clear();
            _settleWorkList.AddRange(_watchedThrows.Keys);

            foreach (Interactable thrown in _settleWorkList)
            {
                // Destroyed, or flight already resolved another way (player caught it mid-air, or a HIT
                // handled it in OnCollisionEnter): stop watching, and do NOT log a miss.
                if (thrown == null || !thrown.IsInFlight)
                {
                    _watchedThrows.Remove(thrown);
                    continue;
                }

                Rigidbody body = thrown.Body;
                bool nearlyStopped =
                    body.linearVelocity.sqrMagnitude < linearSqrThreshold &&
                    body.angularVelocity.sqrMagnitude < angularSqrThreshold;

                if (!nearlyStopped)
                {
                    // Any single frame back above threshold restarts the settle timer.
                    _watchedThrows[thrown] = 0f;
                    continue;
                }

                float stillTime = _watchedThrows[thrown] + Time.fixedDeltaTime;
                if (stillTime < settleTime)
                {
                    _watchedThrows[thrown] = stillTime;
                    continue;
                }

                // Settled: this throw missed the creature and has come to rest.
                _watchedThrows.Remove(thrown);
                thrown.SetInFlight(false);

                float distance = FlatDistance(thrown.transform.position);
                DistanceCategory category = Classify(distance);
                Debug.Log($"[CreatureObservation] THROW {thrown.DisplayName} / MISS / Distance: {distance:F2}m / {CategoryLabel(category)}");
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_watchedThrows.Count == 0)
                return;

            var interactable = collision.collider.GetComponentInParent<Interactable>();
            if (interactable == null || !_watchedThrows.Remove(interactable))
                return;

            // Flight resolved by a hit - it's a normal world object again. (A miss instead resolves in
            // FixedUpdate once the object comes to rest.)
            interactable.SetInFlight(false);

            Debug.Log($"[CreatureObservation] THROW {interactable.DisplayName} / HIT");
        }

        // Same flat XZ convention CreatureMovement/CreaturePickup already use for every other distance
        // check in this project.
        private float FlatDistance(Vector3 worldPos)
        {
            Vector3 flat = worldPos - transform.position;
            flat.y = 0f;
            return flat.magnitude;
        }

        private DistanceCategory Classify(float distance)
        {
            if (distance < veryCloseMax)
                return DistanceCategory.VeryClose;
            if (distance < nearMax)
                return DistanceCategory.Near;
            return DistanceCategory.Far;
        }

        private static string CategoryLabel(DistanceCategory category) => category switch
        {
            DistanceCategory.VeryClose => "VERY_CLOSE",
            DistanceCategory.Near => "NEAR",
            _ => "FAR",
        };

        private void OnValidate()
        {
            veryCloseMax = Mathf.Max(0f, veryCloseMax);
            nearMax = Mathf.Max(veryCloseMax, nearMax);
            settleLinearSpeed = Mathf.Max(0f, settleLinearSpeed);
            settleAngularSpeed = Mathf.Max(0f, settleAngularSpeed);
            settleTime = Mathf.Max(0f, settleTime);
        }
    }
}
