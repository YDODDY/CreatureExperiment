using System;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Food contents in a disposable container (a prepared cup noodle: broth and noodles in a paper cup). The same physical
    /// rules as a drink (<see cref="DrinkContainer"/>), for food that is eaten, not drunk:
    /// - a Sharp item meeting it at <see cref="ThrownImpact.MinPunctureSpeed"/> or more (a thrown knife) punctures the cup;
    /// - its own throw: the first impact at <see cref="spillSpeed"/> or faster (<see cref="Interactable.PreImpactSpeed"/>);
    /// - any other impact that hard along the contact normal (knocked off a shelf, hit by a thrown pan);
    /// - a Soft item running into it never counts.
    /// Spilling: a splash + a few noodle bits (<see cref="ShardFx"/>) + a puddle (<see cref="LiquidSpill"/>, a temporary mess)
    /// and the same object becomes its empty, crushed cup (<see cref="EmptyOut"/>) - nothing is destroyed. A cup (paper) is
    /// never "broken" like ceramic: it just loses its contents.
    ///
    /// Eaten normally (<see cref="ReadyToEatFood"/>) it also becomes the empty cup, without any mess. Empty, it is a light
    /// prop: thrown, it just bounces. Before hot water (<see cref="HotWaterPreparable"/> dry) there is nothing to spill.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class SpillableContents : MonoBehaviour
    {
        /// <summary>Raised when the contents are lost (spilled / punctured) - not when eaten. Nothing listens yet.</summary>
        public static event Action<SpillableContents, Vector3> Spilt;

        [SerializeField] private bool hasContents = true;
        [Tooltip("Shown only while there are contents (broth, noodles).")]
        [SerializeField] private GameObject contentsVisual;

        [Header("Impact")]
        [Tooltip("Impact speed (m/s) that spills it: its own throw's flying speed at the first impact, or an impact along the contact normal.")]
        [SerializeField] private float spillSpeed = 6f;
        [SerializeField] private bool puncturable = true;
        [SerializeField] private float maxThrowAge = 3f;

        [Header("Spill look")]
        [SerializeField] private Color spillColor = new Color(0.72f, 0.32f, 0.12f, 1f);
        [Tooltip("Inactive EggSplat splash (no stain).")]
        [SerializeField] private EggSplat splashTemplate;
        [Tooltip("Inactive puddle template (TemporaryMess).")]
        [SerializeField] private GameObject spillTemplate;
        [SerializeField] private Vector2 spillSize = new Vector2(0.2f, 0.3f);
        [Tooltip("Material of the few bits that fly out (noodles).")]
        [SerializeField] private Material bitsMaterial;
        [SerializeField] private int bitsCount = 5;

        [Header("Empty")]
        [SerializeField] private string emptyName = "빈 용기";
        [SerializeField] private string emptyItemId = "EmptyCup";
        [SerializeField] private float emptyMass = 0.03f;

        private Interactable _item;
        private HotWaterPreparable _prep;
        private bool _prepLooked;

        /// <summary>Has contents right now (and, for an instant item, has had its hot water).</summary>
        public bool HasContents => hasContents && Ready;
        /// <summary>Lost its contents to a spill / puncture (not eaten).</summary>
        public bool Spilled { get; private set; }

        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        private bool Ready
        {
            get
            {
                if (!_prepLooked) { _prep = GetComponent<HotWaterPreparable>(); _prepLooked = true; }
                return _prep == null || _prep.IsPrepared;
            }
        }

        private void Awake() => Refresh();

        /// <summary>Re-show the contents look (hot water just poured).</summary>
        public void Refresh()
        {
            if (contentsVisual != null)
                contentsVisual.SetActive(HasContents);
        }

        /// <summary>
        /// The contents are gone - eaten (<paramref name="spilled"/> false) or lost: the same object is now its empty cup
        /// (name, id, mass; Food / Drink identity dropped, Prop added).
        /// </summary>
        public void EmptyOut(bool spilled)
        {
            if (!hasContents)
                return;
            hasContents = false;
            Spilled = spilled;
            Item.SetDisplayName(emptyName);
            Item.SetItemId(emptyItemId);
            Item.SetTags((Item.Tags & ~(ItemTag.Food | ItemTag.Drink)) | ItemTag.Prop);
            var body = Item.Body;
            if (body != null && emptyMass > 0f)
                body.mass = emptyMass;
            Refresh();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!HasContents || Item.IsHeld || collision.collider is CharacterController)
                return;

            ContactPoint contact = collision.GetContact(0);
            if (puncturable && ThrownImpact.IsPuncturingHit(collision))
            {
                Spill(contact.point, Vector3.up, null, puncture: true);
                return;
            }

            Interactable other = collision.rigidbody != null ? collision.rigidbody.GetComponent<Interactable>() : null;
            if (other != null && !ThrownImpact.IsHard(other))
                return; // soft things bumping into it never count

            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, Item.PreImpactVelocity) > 0f)
                normal = -normal; // face back toward where it came from
            Collider surface = collision.rigidbody == null ? collision.collider : null;

            // Its own throw: the first impact is judged once, by how fast it was flying.
            if (Item.LastThrowMode != ThrowMode.None && Item.ClaimThrowOutcome(maxThrowAge))
            {
                if (Item.PreImpactSpeed >= spillSpeed)
                    Spill(contact.point, normal, surface, puncture: false);
                return;
            }
            if (Mathf.Abs(Vector3.Dot(collision.relativeVelocity, normal)) >= spillSpeed)
                Spill(contact.point, normal, surface, puncture: false);
        }

        private void Spill(Vector3 point, Vector3 normal, Collider surface, bool puncture)
        {
            EmptyOut(spilled: true);
            LiquidSpill.Splash(splashTemplate, point, normal, "FoodSplash");
            ParticleFx.Burst(point, Vector3.up, spillColor, 12, 0.03f);
            if (bitsMaterial != null && bitsCount > 0)
                ShardFx.Spawn(transform, point, bitsMaterial, bitsCount, 0.03f, 2.5f);
            if (surface != null)
                LiquidSpill.Puddle(spillTemplate, spillColor, spillSize, point, normal, surface, objectName: "FoodSpill");
            else
                LiquidSpill.PuddleBelow(transform, spillTemplate, spillColor, spillSize, "FoodSpill");
            ProceduralSfx.Play(puncture ? SfxKind.Puncture : SfxKind.Splash, point, 0.9f);
            Spilt?.Invoke(this, point);
        }
    }
}
