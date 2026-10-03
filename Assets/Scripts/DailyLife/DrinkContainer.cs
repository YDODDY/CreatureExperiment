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
    /// Puncture (any time, thrown or lying still): a full can / plastic bottle met by a Sharp item at
    /// <see cref="ThrownImpact.MinPunctureSpeed"/> or more (a thrown knife, or the drink thrown into a knife stuck in a
    /// wall) leaks - a short spray, a puddle on the surface under it, a pop-and-hiss - and stays as its empty container.
    /// Sharp is a physical property, not an attack. A glass bottle is not punctured (it only shatters as above).
    /// All three outcomes are judged in this one OnCollisionEnter, in a fixed order (puncture, then own throw), so no
    /// cross-component impact resolver is needed here.
    ///
    /// Emptied (drunk, burst, punctured) it is no longer a Drink: its tags lose <see cref="ItemTag.Drink"/> and gain
    /// <see cref="ItemTag.Prop"/>, with the empty name / item id.
    ///
    /// The spill is a flat puddle of <see cref="spillColor"/> (<see cref="spillTemplate"/>, no collider) lying on the hit
    /// surface - only a fixed one (no Rigidbody), riding it via <see cref="SurfaceMount"/>. It is a <see cref="TemporaryMess"/>:
    /// the next day it is gone.
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

        /// <summary>Raised when a full drink is punctured by a sharp item: drink, contact point. Nothing listens yet.</summary>
        public static event System.Action<DrinkContainer, Vector3> Punctured;

        /// <summary>Raised when a glass shatters: the glass (destroyed right after), where. The pub owner listens.</summary>
        public static event System.Action<DrinkContainer, Vector3> Shattered;

        private Interactable _item;
        private bool _shattered;
        private bool _drinking;
        private HeldItemUseMotion _motion;

        public bool IsFull => full;
        public bool PrimaryActive => _drinking;
        public bool IsGlass => glass;
        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        private HotWaterPreparable _prep;
        private bool _prepLooked;

        /// <summary>Has a drink in it right now (full, and - for an instant cup - hot water already poured).</summary>
        public bool HasDrink => full && Ready;

        // A dry instant cup (powder, no water yet) is not a drink until a hot water station prepared it.
        private bool Ready
        {
            get
            {
                if (!_prepLooked) { _prep = GetComponent<HotWaterPreparable>(); _prepLooked = true; }
                return _prep == null || _prep.IsPrepared;
            }
        }

        private void Awake() => ApplyVisuals();

        /// <summary>Re-show the contents look (an instant cup just got its hot water).</summary>
        public void RefreshVisuals() => ApplyVisuals();

        // --- Drink (Left Click in hand)
        public bool PrimaryPress(Ray aim)
        {
            if (_drinking)
                return true; // already at the mouth
            if (!full || !Ready)
                return false; // empty (or a dry instant cup): nothing to drink - the press falls through
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
            if (_shattered || Item.IsHeld || collision.collider is CharacterController || !Ready)
                return; // a dry instant cup has no liquid to lose yet
            // A sharp item meeting a full can / plastic bottle: it leaks, thrown or not.
            if (full && !glass && ThrownImpact.IsPuncturingHit(collision))
            {
                Puncture(collision.GetContact(0).point);
                return;
            }
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
            LiquidSpill.Splash(splashTemplate, contact.point, normal);
            ProceduralSfx.Play(SfxKind.Splash, contact.point, 0.9f);
            if (fixedSurface)
                LiquidSpill.Puddle(spillTemplate, spillColor, spillSize, contact.point, normal, collision.collider, surfaceOffset);
        }

        // Punctured: a short spray, a puddle on the fixed surface right under the drink, pop + hiss, empty container.
        private void Puncture(Vector3 point)
        {
            BecomeEmpty();
            ParticleFx.Burst(point, (point - transform.position).sqrMagnitude > 1e-6f ? point - transform.position : Vector3.up,
                spillColor, 16, 0.03f);
            LiquidSpill.Splash(splashTemplate, point, Vector3.up);
            ProceduralSfx.Play(SfxKind.Puncture, point, 0.95f);
            LiquidSpill.PuddleBelow(transform, spillTemplate, spillColor, spillSize);
            Punctured?.Invoke(this, point);
        }

        private void Shatter(Vector3 point, Vector3 normal, Collider surface, bool fixedSurface)
        {
            _shattered = true;
            if (full)
            {
                LiquidSpill.Splash(splashTemplate, point, normal);
                if (fixedSurface)
                    LiquidSpill.Puddle(spillTemplate, spillColor, spillSize, point, normal, surface, surfaceOffset);
            }
            LiquidSpill.Splash(shardsTemplate, point, normal, "GlassShards");
            ProceduralSfx.Play(SfxKind.GlassBreak, point, 1f);
            Shattered?.Invoke(this, point);
            Destroy(gameObject);
        }

        private void BecomeEmpty()
        {
            full = false;
            Item.SetDisplayName(emptyName);
            Item.SetItemId(emptyItemId);
            Item.SetTags((Item.Tags & ~ItemTag.Drink) | ItemTag.Prop);
            var body = GetComponent<Rigidbody>();
            if (body != null && emptyMass > 0f)
                body.mass = emptyMass;
            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            if (contents != null) contents.SetActive(full && Ready);
            if (seal != null) seal.SetActive(full);
            if (openedMark != null) openedMark.SetActive(!full);
        }
    }
}
