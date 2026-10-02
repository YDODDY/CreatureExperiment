using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A one-shot drink (bottle or can). Full / unopened until it is drunk or burst, then it stays the same object
    /// as its empty container - still an ordinary Interactable (place, F / Right Click throw, trash) - only its name,
    /// id, mass and a few visuals change. Nothing is destroyed or respawned, so the empty stays in the hand.
    ///
    /// Drink: Left Click while held (<see cref="IHeldPrimaryAction"/>, routed by <c>MealEater</c>). A store drink not paid
    /// for yet (<see cref="StoreProduct"/>) refuses - vending drinks have no StoreProduct and are simply the player's.
    /// Drinking is shown with the holder's <see cref="HeldItemUseMotion"/>: up to the mouth, tipped, emptied part way,
    /// back down as the empty container. <see cref="PrimaryActive"/> stays up meanwhile (no E / F / Right Click / slot
    /// change, no second drink); cancelled before the sip, nothing is drunk.
    ///
    /// The first impact of a player throw (F or Right Click, <see cref="Interactable.ClaimThrowOutcome"/>) is judged once:
    /// - can / plastic bottle (<see cref="glass"/> off): at <see cref="burstSpeed"/> or faster a full drink bursts open -
    ///   short splash (<see cref="EggSplat"/> bits, no stain) and a spill on the surface it hit - and stays as its empty
    ///   container. An empty one just bounces.
    /// - glass bottle (<see cref="glass"/> on): at <see cref="breakSpeed"/> or faster it shatters - a short burst of
    ///   glass bits (visual only, no colliders) and the bottle is gone. Full: the contents splash and spill too; empty: no spill.
    ///
    /// The spill is a flat puddle of <see cref="spillColor"/> (<see cref="spillTemplate"/>, no collider) lying on the hit
    /// surface - only a fixed one (no Rigidbody), riding it via <see cref="SurfaceMount"/>. It is a <see cref="TemporaryMess"/>:
    /// the next day it is gone.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class DrinkContainer : MonoBehaviour, IHeldPrimaryAction
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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

        [Header("Burst (can / plastic)")]
        [Tooltip("Pre-impact speed (m/s) of the first impact after a player throw that bursts a full drink. F throws at 8 m/s, Right Click at 16.")]
        [SerializeField] private float burstSpeed = 6f;
        [Tooltip("How long after the throw its first impact still counts.")]
        [SerializeField] private float maxThrowAge = 3f;
        [Tooltip("Inactive splash object cloned on a burst (EggSplat with bits in the drink's colour, no stain).")]
        [SerializeField] private EggSplat splashTemplate;

        [Header("Glass (breaks)")]
        [Tooltip("A glass bottle: a hard enough first throw impact shatters it, full or empty.")]
        [SerializeField] private bool glass;
        [Tooltip("Pre-impact speed (m/s) of the first throw impact that shatters the glass.")]
        [SerializeField] private float breakSpeed = 7f;
        [Tooltip("Inactive EggSplat with glass-coloured bits, played where it shatters (no stain).")]
        [SerializeField] private EggSplat shardsTemplate;

        [Header("Spill")]
        [Tooltip("Inactive puddle (no collider, TemporaryMess) cloned on the hit surface when the contents are lost. None = no spill.")]
        [SerializeField] private GameObject spillTemplate;
        [SerializeField] private Color spillColor = new Color(0.25f, 0.12f, 0.05f, 1f);
        [Tooltip("Puddle diameter range (m).")]
        [SerializeField] private Vector2 spillSize = new Vector2(0.22f, 0.34f);
        [Tooltip("Lift off the surface, against z-fighting.")]
        [SerializeField] private float surfaceOffset = 0.004f;

        private Interactable _item;
        private bool _shattered;
        private bool _drinking;
        private HeldItemUseMotion _motion;

        public bool IsFull => full;
        public bool PrimaryActive => _drinking;
        public bool IsGlass => glass;
        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        private void Awake() => ApplyVisuals();

        // --- Drink (Left Click in hand)
        public bool PrimaryPress(Ray aim)
        {
            if (_drinking)
                return true; // already at the mouth
            if (!full)
                return false; // an empty container has nothing to do; the press falls through (not edible either)
            if (StoreProduct.RefuseUnpaidUse(this))
                return true; // a store drink not paid for yet

            _motion = HeldItemUseMotion.For(Item);
            if (_motion != null && _motion.PlayOneShot(Item, HeldUseStyle.Drink, Drink, () => _drinking = false))
            {
                _drinking = true;
                return true;
            }
            Drink();
            return true;
        }

        public void PrimaryCancel()
        {
            if (!_drinking)
                return;
            _drinking = false;
            if (_motion != null)
                _motion.Cancel(Item);
        }

        private void Drink()
        {
            if (!full)
                return;
            ConsumeEvents.Raise(gameObject, ConsumeKind.Drink);
            BecomeEmpty();
        }

        // --- First impact of a player throw
        private void OnCollisionEnter(Collision collision)
        {
            if (_shattered || Item.IsHeld || collision.collider is CharacterController)
                return;
            if (Item.LastThrowMode == ThrowMode.None)
                return;
            if (!full && !glass)
                return; // an empty can / plastic bottle just bounces
            // Claim before the speed check: only the first impact of each throw is judged, later bounces never count.
            if (!Item.ClaimThrowOutcome(maxThrowAge))
                return;

            ContactPoint contact = collision.GetContact(0);
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, Item.PreImpactVelocity) > 0f)
                normal = -normal; // face back toward where the drink came from
            bool fixedSurface = collision.rigidbody == null;

            if (glass)
            {
                if (Item.PreImpactSpeed >= breakSpeed)
                    Shatter(contact.point, normal, collision.collider, fixedSurface);
                return;
            }

            if (Item.PreImpactSpeed < burstSpeed)
                return;
            BecomeEmpty();
            PlayAt(splashTemplate, contact.point, normal, "DrinkSplash");
            if (fixedSurface)
                Spill(contact.point, normal, collision.collider);
        }

        private void Shatter(Vector3 point, Vector3 normal, Collider surface, bool fixedSurface)
        {
            _shattered = true;
            if (full)
            {
                PlayAt(splashTemplate, point, normal, "DrinkSplash");
                if (fixedSurface)
                    Spill(point, normal, surface);
            }
            PlayAt(shardsTemplate, point, normal, "GlassShards");
            Destroy(gameObject);
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

        private void PlayAt(EggSplat template, Vector3 point, Vector3 normal, string objectName)
        {
            if (template == null)
                return;
            EggSplat fx = Instantiate(template, point + normal * 0.02f, Quaternion.FromToRotation(Vector3.up, normal));
            fx.name = objectName;
            fx.gameObject.SetActive(true);
            fx.Play(leaveStain: false);
        }

        // A flat puddle on the fixed surface that was hit, in this drink's colour.
        private void Spill(Vector3 point, Vector3 normal, Collider surface)
        {
            if (spillTemplate == null)
                return;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up);
            GameObject spill = Instantiate(spillTemplate, point + normal * surfaceOffset, rot);
            spill.name = "DrinkSpill";
            float d = Random.Range(spillSize.x, spillSize.y);
            Vector3 s = spillTemplate.transform.localScale;
            spill.transform.localScale = new Vector3(d, s.y, d * Random.Range(0.75f, 1f));
            var block = new MaterialPropertyBlock();
            foreach (var r in spill.GetComponentsInChildren<Renderer>(true))
            {
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, spillColor);
                r.SetPropertyBlock(block);
            }
            spill.SetActive(true);
            SurfaceMount.Attach(spill.transform, surface);
        }
    }
}
