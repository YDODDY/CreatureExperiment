using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Marks an architectural surface (wall / floor / ceiling) a <see cref="FragileStickerItem"/> may be
    /// stuck on, for fun - no score, no work meaning. Only put this on building geometry, never on
    /// doors, items, furniture or creatures.
    ///
    /// It is a receiver only for a held sticker (<see cref="IOptionalHeldItemReceiver"/>): with anything
    /// else in hand the surface stays a normal floor / wall (Place works). The sticker lands where the
    /// camera is aiming on this surface, aligned to its normal; the sticker item is used up.
    /// </summary>
    public class StickerSurface : MonoBehaviour, IOptionalHeldItemReceiver
    {
        [SerializeField] private RuntimeStickerRoot stickerRoot;
        [Tooltip("Reach for sticking, in metres - a little more than the pickup reach so a ceiling works.")]
        [SerializeField] private float maxReach = 2.2f;

        public float MaxReach => maxReach;
        public string FocusName => "취급주의 스티커 붙이기";
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused) { }

        public bool AppliesTo(Interactable item)
        {
            return item != null && item.GetComponent<FragileStickerItem>() != null;
        }

        public bool CanReceive(Interactable item) => AppliesTo(item) && stickerRoot != null;

        public string GetRejectPrompt(Interactable item) => null;

        public void Receive(Interactable item)
        {
            if (item == null)
                return;

            Camera cam = Camera.main;
            if (cam != null && stickerRoot != null)
            {
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, maxReach + 0.5f, ~0, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<StickerSurface>() == this)
                {
                    stickerRoot.Place(hit.point, hit.normal);
                }
            }
            Destroy(item.gameObject);
        }
    }
}
