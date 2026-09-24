using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A supply on / by the work table: Interact puts one fresh item (empty packing box, tape, fragile
    /// sticker) at <see cref="outputPoint"/> for the player to pick up. Unlimited for now; everything it
    /// makes is registered with the <see cref="WorkShiftController"/> so a new day clears it.
    /// Used through PlayerInteractor's World Use like any IUsable (held item or not). Not a pickup.
    /// </summary>
    public class WorkSupply : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("References")]
        [SerializeField] private WorkShiftController shift;
        [Tooltip("Inactive object cloned on each use.")]
        [SerializeField] private GameObject template;
        [Tooltip("Where the new item appears.")]
        [SerializeField] private Transform outputPoint;
        [Tooltip("Renderer of the outline child - enabled only while focused.")]
        [SerializeField] private Renderer outlineRenderer;

        [Header("Label")]
        [SerializeField] private string prompt = "빈 상자 꺼내기";

        public string FocusName => prompt;
        public Transform FocusTransform => transform;

        private void Awake()
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = false;
            if (template != null)
                template.SetActive(false);
        }

        public void SetFocused(bool focused)
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = focused;
        }

        public void Use()
        {
            if (template == null)
                return;

            Transform at = outputPoint != null ? outputPoint : transform;
            GameObject made = Instantiate(template, at.position, at.rotation);
            made.name = template.name.Replace("_Template", "");
            made.SetActive(true);
            if (shift != null)
                shift.RegisterRuntimeObject(made);
        }
    }
}
