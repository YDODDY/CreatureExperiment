using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The shift's incoming box in Receiving. Interact opens it once per day - only after clocking in -
    /// and <see cref="WorkShiftController"/> lays the batch out. Opening again does nothing.
    /// Used through PlayerInteractor's World Use like any IUsable (held item or not). Not a pickup.
    /// </summary>
    public class IncomingWorkBox : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("References")]
        [SerializeField] private WorkShiftController shift;
        [Tooltip("Lid visual, hidden once the box is open.")]
        [SerializeField] private GameObject lid;
        [Tooltip("Renderer of the outline child - enabled only while focused.")]
        [SerializeField] private Renderer outlineRenderer;

        public string FocusName
        {
            get
            {
                if (shift == null || !shift.ShiftActive) return "출근 후 작업 가능";
                return shift.BoxOpened ? "개봉된 입고 상자" : "입고 상자 열기";
            }
        }

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

        public void Use()
        {
            if (shift != null)
                shift.TryOpenIncomingBox();
        }

        /// <summary>Show the box closed (lid on) or open. Driven by the shift.</summary>
        public void SetOpenVisual(bool open)
        {
            if (lid != null)
                lid.SetActive(!open);
        }
    }
}
