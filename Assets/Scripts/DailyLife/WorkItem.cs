using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>How a work item must be packed - independent of how it looks.</summary>
    public enum WorkItemCategory
    {
        Normal,  // shown as a Cube   -> box + tape
        Fragile  // shown as a Sphere -> box + tape + fragile sticker
    }

    /// <summary>
    /// One item of a <see cref="WorkShiftController"/> batch. Sits on the same GameObject as an
    /// <c>Interactable</c> (pickup / place / throw unchanged) and only adds the work state:
    /// <see cref="Category"/>, <see cref="ShouldDiscard"/>, <see cref="IsBroken"/>, <see cref="IsResolved"/>.
    ///
    /// The player judges it by looking, following the wall guideline: Cube = Normal, Sphere = Fragile,
    /// red = discard whatever the shape. The label does not give the answer away - it only says
    /// "작업 물품", or "파손품" once broken.
    ///
    /// Only a Fragile item that is not for discard can break: a collision faster than
    /// <see cref="fragileBreakSpeed"/> while nobody holds it. A normal Place (at rest) or a short drop
    /// stays under the threshold; a Throw into a wall or a fall from about a metre does not. Breaking
    /// does not resolve the item - it still has to be submitted or discarded.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class WorkItem : MonoBehaviour
    {
        [Header("Break (Fragile only)")]
        [Tooltip("Relative collision speed (m/s) at or above which a Fragile item breaks. 4.5 ~ a fall of about 1 m; a player throw is 8.")]
        [SerializeField] private float fragileBreakSpeed = 4.5f;

        [Header("Materials")]
        [SerializeField] private Material normalMaterial;
        [Tooltip("Bright red - 'discard this'.")]
        [SerializeField] private Material discardMaterial;
        [Tooltip("Dark red-brown - 'this broke'. Deliberately not the discard red.")]
        [SerializeField] private Material brokenMaterial;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private WorkItemCategory category = WorkItemCategory.Normal;
        [SerializeField] private bool shouldDiscard;
        [SerializeField] private bool isBroken;
        [SerializeField] private bool isResolved;

        private Interactable _interactable;
        private WorkShiftController _shift;

        public WorkItemCategory Category => category;
        public bool ShouldDiscard => shouldDiscard;
        public bool IsBroken => isBroken;
        public bool IsResolved => isResolved;
        public Interactable Interactable => _interactable != null ? _interactable : (_interactable = GetComponent<Interactable>());

        /// <summary>Label shown on focus / while held. Never reveals the category.</summary>
        public string StatusLabel => isBroken ? "파손품" : "작업 물품";

        private void Awake()
        {
            _interactable = GetComponent<Interactable>();
        }

        /// <summary>Called by the shift right after spawning this item.</summary>
        public void Init(WorkShiftController shift, WorkItemCategory itemCategory, bool discard)
        {
            _shift = shift;
            category = itemCategory;
            shouldDiscard = discard;
            isBroken = false;
            isResolved = false;
            Interactable.SetDisplayName(StatusLabel);
            SetMaterial(shouldDiscard ? discardMaterial : normalMaterial);
        }

        /// <summary>Called by the shift when the item is submitted (in a box) or discarded. Once only.</summary>
        public void MarkResolved()
        {
            isResolved = true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (category != WorkItemCategory.Fragile || shouldDiscard || isBroken || isResolved)
                return;
            if (Interactable.IsHeld)
                return;
            if (collision.relativeVelocity.magnitude < fragileBreakSpeed)
                return;
            Break(collision.relativeVelocity.magnitude);
        }

        private void Break(float impactSpeed)
        {
            isBroken = true;
            Interactable.SetDisplayName(StatusLabel);
            SetMaterial(brokenMaterial);

            Debug.Log($"[WorkItem] {name} broke (impact {impactSpeed:0.0} m/s).", this);
            if (_shift != null)
                _shift.ReportBroken(this);
        }

        private void SetMaterial(Material m)
        {
            var r = GetComponent<Renderer>();
            if (r != null && m != null)
                r.sharedMaterial = m;
        }
    }
}
