using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A package whose contents break when it is thrown (an egg carton): on the first impact after a player
    /// throw (F or Right Click - <see cref="Interactable.LastThrowMode"/>), every unit still in
    /// <see cref="stock"/> breaks at once - the count goes to 0, the unit visuals go off, and one
    /// <see cref="EggSplat"/> per broken unit is left in a small cluster at the contact, lying on the surface
    /// (<see cref="SurfaceMount"/>, so it rides a door). The package itself stays, empty, as an ordinary item.
    ///
    /// Placing (E) never breaks anything: a place records no throw. Later bumps of the same flight don't count
    /// either - only the first impact of each throw. No loose eggs are spawned (no physics chain reaction).
    /// On a body with a Rigidbody (another item) only the bursts play, no stains - same rule as a single egg.
    ///
    /// The other way round too: a carton lying somewhere that is hit by something the player threw that is not
    /// food (<see cref="ThrownImpact"/>) loses every egg left the same way, the stains lying in the tray.
    /// Both paths go through one BreakRemaining, which empties the count first - one impact never splats twice.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class ThrowBreakContents : MonoBehaviour
    {
        /// <summary>Raised when a thrown package's contents break: package, how many, surface, contact point. Nothing listens yet.</summary>
        public static event Action<ThrowBreakContents, int, Collider, Vector3> Broke;

        [SerializeField] private ConsumableStock stock;
        [Tooltip("Inactive EggSplat object cloned once per broken unit.")]
        [SerializeField] private EggSplat splatTemplate;
        [Tooltip("How far from the contact the splats spread along the surface (m).")]
        [SerializeField] private float spread = 0.09f;
        [Tooltip("Lift off the surface, against z-fighting.")]
        [SerializeField] private float surfaceOffset = 0.004f;
        [Tooltip("Height of the tray floor above the pivot - where stains lie when the carton itself is hit.")]
        [SerializeField] private float trayFloorHeight = 0.012f;

        private Interactable _item;

        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());
        private ConsumableStock Stock => stock != null ? stock : (stock = GetComponent<ConsumableStock>());

        /// <summary>How long after its own throw the first impact still breaks the contents.</summary>
        private const float MaxOwnThrowAge = 10f;

        private void OnCollisionEnter(Collision collision)
        {
            if (Item.IsHeld || collision.collider is CharacterController)
                return; // the player's own body is not a landing / an impact

            // Something the player threw that is not food hit the carton where it lies: the eggs inside break,
            // the stains stay in the tray.
            if (ThrownImpact.IsFragileBreakingHit(collision))
            {
                BreakRemaining(collision, inTray: true);
                return;
            }

            // Its own throw (F / Right Click): the first impact of that throw breaks what is left. Later bumps don't.
            if (Item.ClaimThrowOutcome(MaxOwnThrowAge))
                BreakRemaining(collision, inTray: false);
        }

        /// <summary>
        /// Every egg still in the carton breaks - once: the count goes to 0 first, so a second call in the same
        /// impact (or a later one) finds nothing left. One splat per egg: lying in the tray where the eggs were
        /// (<paramref name="inTray"/>, the carton was hit), or in a small cluster on the surface it hit.
        /// </summary>
        private void BreakRemaining(Collision collision, bool inTray)
        {
            int count = Stock != null ? Stock.Current : 0;
            if (count <= 0)
                return;
            Stock.SetCurrent(0);

            ContactPoint contact = collision.GetContact(0);
            if (splatTemplate != null)
            {
                if (inTray)
                    SplatInTray(count);
                else
                    SplatOnSurface(collision, contact, count);
            }
            Broke?.Invoke(this, count, collision.collider, contact.point);
        }

        // One stain per broken egg across the tray floor, riding the carton (it stays a movable item).
        private void SplatInTray(int count)
        {
            Transform tray = transform;
            float turn = UnityEngine.Random.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                float angle = turn + i * 360f / count + UnityEngine.Random.Range(-15f, 15f);
                Vector3 local = count > 1
                    ? Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * (spread * UnityEngine.Random.Range(0.4f, 1f))
                    : Vector3.zero;
                Vector3 at = tray.TransformPoint(local + Vector3.up * (trayFloorHeight + surfaceOffset + i * 0.0008f));
                Quaternion rot = tray.rotation * Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up);
                EggSplat splat = Instantiate(splatTemplate, at, rot);
                splat.name = "EggSplat";
                splat.gameObject.SetActive(true);
                splat.transform.SetParent(tray, worldPositionStays: true);
                splat.Play(leaveStain: true);
            }
        }

        // Thrown and landed: an even ring around the contact on the surface it hit (stains only on fixed surfaces).
        private void SplatOnSurface(Collision collision, ContactPoint contact, int count)
        {
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, Item.PreImpactVelocity) > 0f)
                normal = -normal; // face back toward where the package came from

            bool stain = collision.rigidbody == null;
            Quaternion toSurface = Quaternion.FromToRotation(Vector3.up, normal);
            float turn = UnityEngine.Random.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                // An even ring around the contact with a little jitter; one egg lands right on it.
                Vector3 offset = Vector3.zero;
                if (count > 1)
                {
                    float angle = turn + i * 360f / count + UnityEngine.Random.Range(-15f, 15f);
                    float dist = spread * UnityEngine.Random.Range(0.45f, 1f);
                    offset = toSurface * (Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * dist);
                }
                Vector3 at = contact.point + offset + normal * (surfaceOffset + i * 0.0008f);
                Quaternion rot = toSurface * Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up);
                EggSplat splat = Instantiate(splatTemplate, at, rot);
                splat.name = "EggSplat";
                splat.gameObject.SetActive(true);
                if (stain)
                    SurfaceMount.Attach(splat.transform, collision.collider);
                splat.Play(leaveStain: stain);
            }
        }
    }
}
