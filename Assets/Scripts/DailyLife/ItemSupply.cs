using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A home supply of an everyday item (plates, bowls): Interact puts one fresh copy of
    /// <see cref="template"/> at <see cref="outputPoint"/> for the player to pick up. Unlimited, one per
    /// press, no count. If something already sits on the output spot the press does nothing and the label
    /// says so - copies never spawn into each other. Same shape as the workplace <c>WorkSupply</c>, without
    /// the shift bookkeeping. A World Use (IUsable), not a pickup.
    /// </summary>
    public class ItemSupply : MonoBehaviour, IUsable, IFocusTarget
    {
        [Tooltip("Inactive object cloned on each use.")]
        [SerializeField] private GameObject template;
        [Tooltip("Where the new item's pivot appears (the copy keeps the template's pivot).")]
        [SerializeField] private Transform outputPoint;
        [Tooltip("Optional parent for the copies.")]
        [SerializeField] private Transform spawnParent;

        [Header("Label")]
        [SerializeField] private string prompt = "접시 꺼내기";
        [SerializeField] private string blockedPrompt = "꺼낸 것을 먼저 치우세요";

        public string FocusName => IsBlocked() ? blockedPrompt : prompt;
        public Transform FocusTransform => transform;

        public void SetFocused(bool focused) { }

        private void Awake()
        {
            if (template != null)
                template.SetActive(false);
        }

        public void Use()
        {
            if (template == null || IsBlocked())
                return;

            Transform at = outputPoint != null ? outputPoint : transform;
            GameObject made = Instantiate(template, at.position, at.rotation, spawnParent);
            made.name = template.name.Replace("_Template", "");
            made.SetActive(true);
        }

        // Anything solid where the copy's collider would be (lifted a hair so the surface it rests on doesn't count).
        private bool IsBlocked()
        {
            if (template == null)
                return false;
            var box = template.GetComponent<BoxCollider>();
            Vector3 scale = SpawnedScale();
            Vector3 half = box != null ? Vector3.Scale(box.size, scale) * 0.5f : Vector3.one * 0.1f;
            Vector3 offset = box != null ? Vector3.Scale(box.center, scale) : Vector3.zero;
            Transform at = outputPoint != null ? outputPoint : transform;
            Vector3 center = at.position + at.rotation * offset + Vector3.up * 0.005f;
            return Physics.CheckBox(center, half, at.rotation, ~0, QueryTriggerInteraction.Ignore);
        }

        // A copy keeps the template's local scale under spawnParent - not the template's own (possibly scaled) parents.
        private Vector3 SpawnedScale()
        {
            Vector3 local = template.transform.localScale;
            return spawnParent != null ? Vector3.Scale(local, spawnParent.lossyScale) : local;
        }
    }
}
