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
    /// Also: hit by something the player just threw that is not food (<see cref="ThrownImpact"/> - a knife, a pan,
    /// a box...), a loose raw egg breaks whatever its own speed; the splat goes onto the surface it was lying on.
    /// A thrown egg that is caught by a pan (<see cref="ThrownItemCatch"/>) is never broken - the catch is tried first.
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
        private bool _broken;

        public Interactable Interactable => Food.Interactable;
        private FoodItem Food => _food != null ? _food : (_food = GetComponent<FoodItem>());

        private void OnCollisionEnter(Collision collision)
        {
            if (_broken || Food.Kind != FoodKind.Egg || Food.State != FoodState.Raw)
                return;
            if (Interactable.IsHeld || Food.Slot != null || Food.Plate != null)
                return;
            if (collision.collider is CharacterController)
                return; // the player's own body at release is not a surface
            if (ThrownItemCatch.TryCatch(Interactable, collision))
                return; // thrown (near) onto a pan that takes it: cooked there, not splatted (order-safe with FoodItem's own try)

            // Hit by something the player threw that is not food (a knife, a pan, a box): it breaks, however
            // gently the egg itself moves - the splat goes onto whatever the egg was lying on.
            if (ThrownImpact.IsFragileBreakingHit(collision))
            {
                BreakWhereItLies(collision.collider);
                return;
            }

            // Its own fall / throw: breaks only if it hits hard enough along the contact normal.
            ContactPoint contact = collision.GetContact(0);
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, Interactable.PreImpactVelocity) > 0f)
                normal = -normal; // face back toward where the egg came from
            float impact = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal));
            if (impact < breakSpeed)
                return;
            Break(contact.point, normal, collision.collider, stain: collision.rigidbody == null);
        }

        // Knocked by a thrown object: find the fixed surface right under the egg for the stain (none = burst only).
        private void BreakWhereItLies(Collider hitBy)
        {
            Vector3 from = transform.position + Vector3.up * 0.05f;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 0.35f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody != null || hit.collider.transform.IsChildOf(transform))
                    continue; // itself, the projectile, other items
                Break(hit.point, hit.normal, hit.collider, stain: true);
                return;
            }
            Break(transform.position, Vector3.up, hitBy, stain: false);
        }

        /// <summary>The one way an egg breaks: splat (stain on <paramref name="surface"/> if <paramref name="stain"/>) and the egg is gone.</summary>
        private void Break(Vector3 point, Vector3 normal, Collider surface, bool stain)
        {
            _broken = true;
            if (splatTemplate != null)
            {
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up);
                EggSplat splat = Instantiate(splatTemplate, point + normal * surfaceOffset, rot);
                splat.name = "EggSplat";
                splat.gameObject.SetActive(true);
                if (stain)
                    SurfaceMount.Attach(splat.transform, surface);
                splat.Play(leaveStain: stain);
            }
            Broke?.Invoke(this, surface, point);
            Destroy(gameObject);
        }
    }
}
