using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A one-shot drink (bottle or can). Full / unopened until it is drunk or burst, then it stays the same object
    /// as its empty container - still an ordinary Interactable (place, F / Right Click throw, trash) - only its name,
    /// id, mass and a few visuals change. Nothing is destroyed or respawned, so the empty stays in the hand.
    ///
    /// Drink: Left Click while held (<see cref="IHeldPrimaryAction"/>, routed by <c>MealEater</c>).
    /// Burst: the first impact of a player throw (F or Right Click, <see cref="Interactable.ClaimThrowOutcome"/>) at
    /// <see cref="burstSpeed"/> or faster empties a full drink with a short splash (<see cref="EggSplat"/> bits, no
    /// stain). One throw claims one impact, and an empty container never bursts - it just bounces.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class DrinkContainer : MonoBehaviour, IHeldPrimaryAction
    {
        [Header("State")]
        [SerializeField] private bool full = true;

        [Header("Empty container identity")]
        [SerializeField] private string emptyName = "빈 캔";
        [SerializeField] private string emptyItemId = "EmptyCan";
        [SerializeField] private float emptyMass = 0.04f;

        [Header("Visuals")]
        [Tooltip("Shown only while full (the liquid inside a clear bottle). Optional.")]
        [SerializeField] private GameObject contents;
        [Tooltip("Shown only while unopened (a bottle cap, a closed can top). Optional.")]
        [SerializeField] private GameObject seal;
        [Tooltip("Shown only once opened (the dark hole of an opened can). Optional.")]
        [SerializeField] private GameObject openedMark;

        [Header("Burst")]
        [Tooltip("Pre-impact speed (m/s) of the first impact after a player throw that bursts a full drink. F throws at 8 m/s, Right Click at 16.")]
        [SerializeField] private float burstSpeed = 6f;
        [Tooltip("How long after the throw its first impact still counts.")]
        [SerializeField] private float maxThrowAge = 3f;
        [Tooltip("Inactive splash object cloned on a burst (EggSplat with bits in the drink's colour, no stain).")]
        [SerializeField] private EggSplat splashTemplate;

        private Interactable _item;

        public bool IsFull => full;
        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        private void Awake() => ApplyVisuals();

        // --- Drink (Left Click in hand)
        public bool PrimaryPress(Ray aim)
        {
            if (!full)
                return false; // an empty container has nothing to do; the press falls through (not edible either)
            BecomeEmpty();
            return true;
        }

        // --- Burst (first impact of a player throw)
        private void OnCollisionEnter(Collision collision)
        {
            if (!full || Item.IsHeld || collision.collider is CharacterController)
                return;
            if (Item.LastThrowMode == ThrowMode.None)
                return;
            // Claim before the speed check: only the first impact of each throw is judged, later bounces never burst.
            if (!Item.ClaimThrowOutcome(maxThrowAge) || Item.PreImpactSpeed < burstSpeed)
                return;

            ContactPoint contact = collision.GetContact(0);
            BecomeEmpty();
            Splash(contact);
        }

        private void BecomeEmpty()
        {
            full = false;
            Item.SetDisplayName(emptyName);
            Item.SetItemId(emptyItemId);
            var body = GetComponent<Rigidbody>();
            if (body != null && emptyMass > 0f)
                body.mass = emptyMass;
            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            if (contents != null) contents.SetActive(full);
            if (seal != null) seal.SetActive(full);
            if (openedMark != null) openedMark.SetActive(!full);
        }

        private void Splash(ContactPoint contact)
        {
            if (splashTemplate == null)
                return;
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, Item.PreImpactVelocity) > 0f)
                normal = -normal; // face back toward where the drink came from
            EggSplat splash = Instantiate(splashTemplate, contact.point + normal * 0.02f, Quaternion.FromToRotation(Vector3.up, normal));
            splash.name = "DrinkSplash";
            splash.gameObject.SetActive(true);
            splash.Play(leaveStain: false);
        }
    }
}
