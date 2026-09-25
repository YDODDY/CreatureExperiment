using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One of the two work-floor destinations, handed to through the same <see cref="IHeldItemReceiver"/>
    /// path as a GarbageDump (aim + Interact; a wrong item shows the reject prompt and the press is
    /// consumed - no Place fallback):
    /// - Completed : takes a <see cref="PackingBox"/> (any state - a badly packed box is still a
    ///               submission, judged by the shift as wrong).
    /// - Discard   : takes a raw <see cref="WorkItem"/> (judged by the shift), and also empty boxes /
    ///               tape / stickers as plain rubbish (no score). A box with an item inside is refused.
    /// Judging and counting live in <see cref="WorkShiftController"/>; this only routes.
    /// </summary>
    public class WorkReceiver : MonoBehaviour, IHeldItemReceiver
    {
        public enum Policy
        {
            Completed,
            Discard
        }

        [Header("References")]
        [SerializeField] private WorkShiftController shift;
        [Tooltip("Renderer of the outline child - enabled only while focused.")]
        [SerializeField] private Renderer outlineRenderer;

        [Header("Setup")]
        [SerializeField] private Policy policy = Policy.Completed;

        public Policy AcceptPolicy => policy;

        // --- IFocusTarget (only ever shown while the player holds something - see IHeldItemReceiver)
        public string FocusName => policy == Policy.Completed ? "분류완료구역에 제출" : "폐기하기";
        public Transform FocusTransform => transform;

        private void Awake()
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = false;
        }

        public void SetFocused(bool focused)
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = focused;
        }

        // --- IHeldItemReceiver
        public bool CanReceive(Interactable item)
        {
            if (item == null || shift == null)
                return false;

            var box = item.GetComponent<PackingBox>();
            var work = item.GetComponent<WorkItem>();

            if (policy == Policy.Completed)
                return box != null && !box.IsSubmitted && shift.CanAcceptWork;

            if (work != null)
                return !work.IsResolved && shift.CanAcceptWork;
            if (box != null)
                return !box.HasItem;
            return item.GetComponent<FragileStickerItem>() != null; // the tape roll is a reusable tool, never rubbish
        }

        public string GetRejectPrompt(Interactable item)
        {
            if (item == null)
                return null;
            var box = item.GetComponent<PackingBox>();
            var work = item.GetComponent<WorkItem>();

            if ((box != null || work != null) && shift != null && !shift.CanAcceptWork)
                return "근무 시간이 아닙니다";

            if (policy == Policy.Completed)
                return "포장한 상자만 제출할 수 있습니다";

            if (box != null && box.HasItem)
                return "포장된 상자는 분류완료구역에 제출하세요";
            return "폐기 대상이 아닙니다";
        }

        public void Receive(Interactable item)
        {
            if (item == null || shift == null)
                return;

            var box = item.GetComponent<PackingBox>();
            var work = item.GetComponent<WorkItem>();

            if (policy == Policy.Completed && box != null)
            {
                shift.SubmitCompleted(box);
                Park(item);
                return;
            }

            if (work != null)
            {
                shift.SubmitDiscard(work);
                Park(item);
                return;
            }

            // Empty box / sticker: plain rubbish.
            Destroy(item.gameObject);
        }

        // Out of the world, but still owned by the shift until the day resets.
        private void Park(Interactable item)
        {
            item.transform.SetParent(transform, worldPositionStays: true);
            item.gameObject.SetActive(false);
        }
    }
}
