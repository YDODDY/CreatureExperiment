using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A labelled drop zone. Purely a volume + a <see cref="SortKind"/> tag - no counting, no events,
    /// no membership set. <see cref="DailyLifeDirector"/> asks <see cref="Contains"/> for each item's
    /// current pivot every frame, so the sort count always reflects where things actually are right
    /// now (an item leaving the zone drops the count again).
    ///
    /// Keep the two areas axis-aligned and non-overlapping in the scene; the test is the
    /// <see cref="BoxCollider"/>'s world AABB against the item's pivot point.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class SortingArea : MonoBehaviour
    {
        [Tooltip("Items of this kind, and only this kind, count as sorted while inside this volume.")]
        [SerializeField] private SortKind kind = SortKind.Cube;

        /// <summary>The kind this area accepts.</summary>
        public SortKind Kind => kind;

        private BoxCollider _box;

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
        }

        /// <summary>Is <paramref name="worldPos"/> inside this area's volume?</summary>
        public bool Contains(Vector3 worldPos)
        {
            if (_box == null)
                _box = GetComponent<BoxCollider>();
            return _box != null && _box.bounds.Contains(worldPos);
        }

        private void OnDrawGizmos()
        {
            var box = _box != null ? _box : GetComponent<BoxCollider>();
            if (box == null)
                return;

            Color c = kind == SortKind.Cube ? new Color(0.3f, 0.7f, 1f) : new Color(1f, 0.6f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(c.r, c.g, c.b, 0.15f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = c;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
