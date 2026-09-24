using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A packing box from the <see cref="WorkSupply"/>. Carried / placed / thrown like any Interactable,
    /// and while it rests in the world it takes three held things (aim + Interact):
    /// a <see cref="WorkItem"/> (Empty -> HasItem), <see cref="TapeItem"/> (HasItem -> Sealed) and a
    /// <see cref="FragileStickerItem"/> (Sealed -> sticker on). A sticker is allowed on any sealed box -
    /// a wrong sticker is the player's mistake, judged on submission, not prevented here.
    ///
    /// For any other held item (another box, a garbage can...) it is not a receiver, so Swap / Place work
    /// as usual (<see cref="IOptionalHeldItemReceiver"/>). The contained item is kept (inactive, parented
    /// here), never destroyed, so the submission can judge it.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class PackingBox : MonoBehaviour, IOptionalHeldItemReceiver
    {
        [Header("Visuals")]
        [Tooltip("Tape strip shown once sealed.")]
        [SerializeField] private GameObject tapeVisual;
        [Tooltip("Fragile sticker shown once applied.")]
        [SerializeField] private GameObject stickerVisual;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private WorkItem containedItem;
        [SerializeField] private bool isSealed;
        [SerializeField] private bool hasFragileSticker;
        [SerializeField] private bool isSubmitted;

        private Interactable _interactable;
        private Interactable _lastQueried; // the held item the focus label is currently about

        public WorkItem ContainedItem => containedItem;
        public bool HasItem => containedItem != null;
        public bool IsSealed => isSealed;
        public bool HasFragileSticker => hasFragileSticker;
        public bool IsSubmitted => isSubmitted;
        public Interactable Interactable => _interactable != null ? _interactable : (_interactable = GetComponent<Interactable>());

        private void Awake()
        {
            _interactable = GetComponent<Interactable>();
            RefreshVisuals();
        }

        // --- IOptionalHeldItemReceiver
        public float MaxReach => 0f; // normal pickup reach

        public bool AppliesTo(Interactable item)
        {
            return item != null
                && (item.GetComponent<WorkItem>() != null
                    || item.GetComponent<TapeItem>() != null
                    || item.GetComponent<FragileStickerItem>() != null);
        }

        public bool CanReceive(Interactable item)
        {
            _lastQueried = item;
            if (item == null || isSubmitted || Interactable.IsHeld)
                return false;

            var work = item.GetComponent<WorkItem>();
            if (work != null)
                return !HasItem && !work.IsResolved;
            if (item.GetComponent<TapeItem>() != null)
                return HasItem && !isSealed;
            if (item.GetComponent<FragileStickerItem>() != null)
                return isSealed && !hasFragileSticker;
            return false;
        }

        public string GetRejectPrompt(Interactable item)
        {
            if (item == null)
                return null;
            if (item.GetComponent<WorkItem>() != null)
                return HasItem ? "이미 물품이 들어 있습니다" : null;
            if (item.GetComponent<TapeItem>() != null)
                return isSealed ? "이미 테이프를 붙였습니다" : "물품을 먼저 넣으세요";
            if (item.GetComponent<FragileStickerItem>() != null)
                return hasFragileSticker ? "이미 스티커가 붙어 있습니다" : "테이프를 먼저 붙이세요";
            return null;
        }

        public void Receive(Interactable item)
        {
            if (item == null)
                return;

            var work = item.GetComponent<WorkItem>();
            if (work != null)
            {
                containedItem = work;
                work.transform.SetParent(transform, worldPositionStays: false);
                work.transform.localPosition = Vector3.zero;
                work.gameObject.SetActive(false);
            }
            else if (item.GetComponent<TapeItem>() != null)
            {
                isSealed = true;
                Destroy(item.gameObject);
            }
            else if (item.GetComponent<FragileStickerItem>() != null)
            {
                hasFragileSticker = true;
                Destroy(item.gameObject);
            }
            RefreshVisuals();
        }

        // --- IFocusTarget (shown while a work item / tape / sticker is aimed at this box)
        public string FocusName
        {
            get
            {
                if (_lastQueried != null)
                {
                    if (_lastQueried.GetComponent<TapeItem>() != null) return "테이프 붙이기";
                    if (_lastQueried.GetComponent<FragileStickerItem>() != null) return "취급주의 스티커 붙이기";
                }
                return "물품 넣기";
            }
        }

        public Transform FocusTransform => transform;

        public void SetFocused(bool focused)
        {
            Interactable.SetFocused(focused);
        }

        /// <summary>Called by the Completed receiver when this box is handed in.</summary>
        public void MarkSubmitted()
        {
            isSubmitted = true;
        }

        private void RefreshVisuals()
        {
            if (tapeVisual != null) tapeVisual.SetActive(isSealed);
            if (stickerVisual != null) stickerVisual.SetActive(hasFragileSticker);

            string label;
            if (!HasItem) label = "빈 포장상자";
            else if (!isSealed) label = "포장상자 (테이프 전)";
            else label = hasFragileSticker ? "포장 완료 (취급주의)" : "포장 완료";
            Interactable.SetDisplayName(label);
        }
    }
}
