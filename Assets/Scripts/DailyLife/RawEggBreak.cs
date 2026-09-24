using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A raw egg breaks when it hits something hard enough: the egg object is gone and an
    /// <see cref="EggSplat"/> is left at the contact, lying on the surface. Purely physical and the same
    /// for any surface or thrower - floor, wall, ceiling, later a creature. Only the actual impact speed
    /// (along the contact normal) decides; how it was thrown does not. A placed or gently dropped egg
    /// stays whole; a cooked egg never breaks this way.
    ///
    /// The stain is attached to the surface it landed on (<see cref="SurfaceMount"/>), so it moves with a door.
    /// On a body with a Rigidbody (items, the creature) only the burst plays - no stain, for now.
    /// </summary>
    [RequireComponent(typeof(FoodItem))]
    public class RawEggBreak : MonoBehaviour
    {
        /// <summary>Raised as a raw egg breaks (the egg is destroyed right after): egg, what it hit, contact point. Nothing listens yet.</summary>
        public static event Action<RawEggBreak, Collider, Vector3> Broke;

        [Tooltip("Inactive EggSplat object cloned at the impact.")]
        [SerializeField] private EggSplat splatTemplate;
        [Tooltip("Impact speed along the contact normal (m/s) that breaks it. ~0.8 m free fall is 4 m/s.")]
        [SerializeField] private float breakSpeed = 4.5f;
        [Tooltip("Lift off the surface, against z-fighting.")]
        [SerializeField] private float surfaceOffset = 0.004f;

        private FoodItem _food;
        private Vector3 _lastVelocity;
        private bool _broken;

        public Interactable Interactable => Food.Interactable;
        private FoodItem Food => _food != null ? _food : (_food = GetComponent<FoodItem>());

        // Velocity before the solver resolves a contact (OnCollisionEnter already sees the bounce).
        private void FixedUpdate()
        {
            var body = Interactable.Body;
            if (!body.isKinematic)
                _lastVelocity = body.linearVelocity;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_broken || Food.Kind != FoodKind.Egg || Food.State != FoodState.Raw)
                return;
            if (Interactable.IsHeld || Food.Slot != null || Food.Plate != null)
                return;
            if (collision.collider is CharacterController)
                return; // the player's own body at release is not a surface

            ContactPoint contact = collision.GetContact(0);
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, _lastVelocity) > 0f)
                normal = -normal; // face back toward where the egg came from
            float impact = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
            if (impact < breakSpeed)
                return;

            _broken = true;
            if (splatTemplate != null)
            {
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up);
                EggSplat splat = Instantiate(splatTemplate, contact.point + normal * surfaceOffset, rot);
                splat.name = "EggSplat";
                splat.gameObject.SetActive(true);
                bool stain = collision.rigidbody == null;
                if (stain)
                    SurfaceMount.Attach(splat.transform, collision.collider);
                splat.Play(leaveStain: stain);
            }
            Broke?.Invoke(this, collision.collider, contact.point);
            Destroy(gameObject);
        }
    }
}
