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
    /// <see cref="_watchedThrows"/> and watched via <see cref="OnCollisionEnter"/> - this GameObject
    /// already carries the creature's own <c>CapsuleCollider</c> (see the scene), so no extra collider or
    /// component is needed. A HIT is logged the first time a watched object's collider actually touches
    /// the creature, and it is removed from the set at that same moment - both what makes repeated
    /// contact from one throw log only once, and (for now) the entire lifetime of the watch: a throw
    /// that misses just stays watched with no timeout, since deciding when a miss should stop being
    /// watched is the future "final rest position" work this step deliberately does not build. Distance,
    /// trajectory, velocity and closest-approach for a miss are not computed here at all.
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

        private enum DistanceCategory { VeryClose, Near, Far }

        // Player-thrown objects currently being watched for a HIT. Added on THROW, removed (and logged)
        // on the first actual collision with the creature.
        private readonly HashSet<Interactable> _watchedThrows = new HashSet<Interactable>();

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
                _watchedThrows.Add(evt.Interactable);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_watchedThrows.Count == 0)
                return;

            var interactable = collision.collider.GetComponentInParent<Interactable>();
            if (interactable == null || !_watchedThrows.Remove(interactable))
                return;

            // Flight resolved by a hit - it's a normal world object again. A miss ending in a settled
            // rest position is future work and does not clear this yet.
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
        }
    }
}
