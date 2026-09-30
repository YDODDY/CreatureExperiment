using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Keeps one of a workplace-provided item (the tape roll) on hand for each shift. At every day reset
    /// (<see cref="WorkShiftController.ResetDay"/>, scene start included) it looks inside its area - a box = this Transform's
    /// position / rotation / lossyScale, covering the workplace - for a loose item with <see cref="itemId"/>. Found: nothing
    /// happens (the one left at work is used again). None - taken home, dropped in the street, thrown away: one fresh copy
    /// of <see cref="template"/> appears at <see cref="spawnPoint"/>.
    ///
    /// Items taken away are never recalled or deleted; they stay wherever they are, so stealing one every day leaves
    /// several in the world. That is fine - this only guarantees the workplace is never without one.
    ///
    /// Every enabled restock is listed (<see cref="EnsureAll"/>), so the shift controller needs no references to them.
    /// </summary>
    public class WorkplaceItemRestock : MonoBehaviour
    {
        [Tooltip("Inactive copy of the item, cloned when the workplace has none.")]
        [SerializeField] private GameObject template;
        [Tooltip("Where a fresh copy appears (position / rotation). Its parent becomes the copy's parent.")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Interactable.ItemId that counts as 'one on hand'.")]
        [SerializeField] private string itemId = "TapeRoll";

        private static readonly List<WorkplaceItemRestock> s_all = new List<WorkplaceItemRestock>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_all.Clear();

        /// <summary>Every enabled restock checks its area (called on each day reset, scene start included).</summary>
        public static void EnsureAll()
        {
            for (int i = 0; i < s_all.Count; i++)
                if (s_all[i] != null)
                    s_all[i].EnsureStocked();
        }

        private void Awake()
        {
            if (template != null)
                template.SetActive(false);
        }

        private void OnEnable() => s_all.Add(this);
        private void OnDisable() => s_all.Remove(this);

        /// <summary>One loose <see cref="itemId"/> item is somewhere in the area - or a fresh one is put at the spawn point.</summary>
        public void EnsureStocked()
        {
            if (template == null || spawnPoint == null || HasOneInArea())
                return;

            GameObject made = Instantiate(template, spawnPoint.position, spawnPoint.rotation, spawnPoint.parent);
            made.name = template.name.Replace("_Template", "");
            made.transform.localScale = template.transform.localScale;
            made.SetActive(true);
            Debug.Log($"[WorkplaceItemRestock] No {itemId} at work - restocked one.");
        }

        private bool HasOneInArea()
        {
            Physics.SyncTransforms();
            // Once a day, over a whole building: the allocating query, so a big hit list is never cut short.
            Collider[] hits = Physics.OverlapBox(transform.position, transform.lossyScale * 0.5f, transform.rotation,
                ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                var item = hit.GetComponentInParent<Interactable>();
                if (item != null && !item.IsHeld && item.ItemId == itemId && item.gameObject != template)
                    return true;
            }
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.25f);
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
        }
    }
}
