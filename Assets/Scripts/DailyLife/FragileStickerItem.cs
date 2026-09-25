using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One "취급주의" sticker (one use). Carried like any Interactable (E picks it up / puts it down).
    /// Left Click while held (<see cref="IHeldPrimaryAction"/>) sticks it where the camera centre ray hits
    /// within <see cref="reach"/> - any solid surface: wall, floor, ceiling, door, a box - aligned to the
    /// hit normal and riding that surface (<see cref="RuntimeStickerRoot.Place"/>). On a
    /// <see cref="PackingBox"/> it also marks the box fragile. The sticker item is used up; with nothing in
    /// reach the click does nothing and the sticker stays in hand.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class FragileStickerItem : MonoBehaviour, IHeldPrimaryAction
    {
        [SerializeField] private RuntimeStickerRoot stickerRoot;
        [Tooltip("Reach for sticking, in metres - a little more than the pickup reach so a ceiling works.")]
        [SerializeField] private float reach = 2.2f;
        [SerializeField] private string playerTag = "Player";

        public bool PrimaryPress(Ray aim)
        {
            if (stickerRoot == null)
                return false;
            if (!Physics.Raycast(aim, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
                return false;
            if (!string.IsNullOrEmpty(playerTag) && hit.collider.CompareTag(playerTag))
                return false;

            stickerRoot.Place(hit);
            var box = hit.collider.GetComponentInParent<PackingBox>();
            if (box != null)
                box.ApplyFragileSticker();
            Destroy(gameObject);
            return true;
        }
    }
}
