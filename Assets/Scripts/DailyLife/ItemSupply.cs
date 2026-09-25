using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A home supply of an everyday item (plates, bowls): Interact puts one fresh copy of
    /// <see cref="template"/> at <see cref="outputPoint"/> for the player to pick up. Unlimited, one per
    /// press, no count. If something already sits on the output spot the press does nothing and the label
    /// says so - copies never spawn into each other. Same shape as the workplace <c>WorkSupply</c>, without
    /// the shift bookkeeping. A World Use (IUsable), not a pickup.
    ///
    /// <see cref="intoHand"/> (the grocery store's shelves): the copy goes straight into the player's empty hand
    /// instead of onto an output spot - nothing is put in the world, so nothing can block it (hands full: nothing
    /// happens, the label says so). What the shelf shows is separate display dressing; it never changes.
    /// </summary>
    public class ItemSupply : MonoBehaviour, IUsable, IFocusTarget
    {
        [Tooltip("Inactive object cloned on each use.")]
        [SerializeField] private GameObject template;
        [Tooltip("Where the new item's pivot appears (the copy keeps the template's pivot).")]
        [SerializeField] private Transform outputPoint;
        [Tooltip("Optional parent for the copies.")]
        [SerializeField] private Transform spawnParent;
        [Tooltip("Optional shared output tray (vending machine). When set, the supply is blocked only while a loose pick-up-able item (an Interactable nobody holds) overlaps this box - so every button sharing the tray gives the same answer. Box = this Transform's position / rotation / lossyScale.")]
        [SerializeField] private Transform sharedOutputArea;

        [Header("Label")]
        [SerializeField] private string prompt = "접시 꺼내기";
        [SerializeField] private string blockedPrompt = "꺼낸 것을 먼저 치우세요";

        [Header("Into the hand (store shelf)")]
        [Tooltip("Hand the copy straight to the player instead of putting it on the output spot.")]
        [SerializeField] private bool intoHand;
        [SerializeField] private string handsFullPrompt = "손을 비우세요";

        private static PlayerInteractor s_player;

        private static PlayerInteractor Player
        {
            get
            {
                if (s_player == null)
                    s_player = FindFirstObjectByType<PlayerInteractor>();
                return s_player;
            }
        }

        public string FocusName
        {
            get
            {
                if (intoHand)
                    return Player != null && Player.IsHolding ? handsFullPrompt : prompt;
                return IsBlocked() ? blockedPrompt : prompt;
            }
        }
        public Transform FocusTransform => transform;

        public void SetFocused(bool focused) { }

        private void Awake()
        {
            if (template != null)
                template.SetActive(false);
        }

        public void Use()
        {
            if (intoHand)
            {
                GiveToHand();
                return;
            }
            if (template == null || IsBlocked())
                return;

            Transform at = outputPoint != null ? outputPoint : transform;
            GameObject made = Instantiate(template, at.position, at.rotation, spawnParent);
            made.name = template.name.Replace("_Template", "");
            made.SetActive(true);
        }

        private void GiveToHand()
        {
            PlayerInteractor player = Player;
            if (template == null || player == null || player.IsHolding)
                return;

            Transform at = outputPoint != null ? outputPoint : transform;
            GameObject made = Instantiate(template, at.position, at.rotation, spawnParent);
            made.name = template.name.Replace("_Template", "");
            made.SetActive(true);
            var item = made.GetComponent<Interactable>();
            if (item == null || !player.TryHoldNew(item))
                Destroy(made); // not a pick-up-able product: nothing is left lying around
        }

        // Anything solid where the copy's collider would be (lifted a hair so the surface it rests on doesn't count).
        private bool IsBlocked()
        {
            if (template == null)
                return false;
            if (sharedOutputArea != null)
                return IsTrayOccupied();
            var box = template.GetComponent<BoxCollider>();
            Vector3 scale = SpawnedScale();
            Vector3 half = box != null ? Vector3.Scale(box.size, scale) * 0.5f : Vector3.one * 0.1f;
            Vector3 offset = box != null ? Vector3.Scale(box.center, scale) : Vector3.zero;
            Transform at = outputPoint != null ? outputPoint : transform;
            Vector3 center = at.position + at.rotation * offset + Vector3.up * 0.005f;
            return Physics.CheckBox(center, half, at.rotation, ~0, QueryTriggerInteraction.Ignore);
        }

        // Only a loose item lying in the tray counts - the machine's own parts and display dressing never do.
        private bool IsTrayOccupied()
        {
            Collider[] hits = Physics.OverlapBox(sharedOutputArea.position, sharedOutputArea.lossyScale * 0.5f,
                sharedOutputArea.rotation, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                var item = hit.GetComponentInParent<Interactable>();
                if (item != null && !item.IsHeld)
                    return true;
            }
            return false;
        }

        // A copy keeps the template's local scale under spawnParent - not the template's own (possibly scaled) parents.
        private Vector3 SpawnedScale()
        {
            Vector3 local = template.transform.localScale;
            return spawnParent != null ? Vector3.Scale(local, spawnParent.lossyScale) : local;
        }
    }
}
