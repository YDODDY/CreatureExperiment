using UnityEngine;

namespace CreatureExperiment.Story
{
    /// <summary>
    /// A box (this transform's unit cube: position / rotation / scale) a story script asks "is the player inside?".
    /// No collider, no events - the story beat that cares checks <see cref="Contains"/> only while it is waiting for it,
    /// so walking in and out at any other time does nothing.
    /// </summary>
    public class StoryZone : MonoBehaviour
    {
        [SerializeField] private Color gizmoColor = new Color(1f, 0.6f, 0.1f, 0.8f);
        [Tooltip("Optional: points inside this zone don't count (e.g. a buffer strip just inside a door).")]
        [SerializeField] private StoryZone exclude;

        public bool Contains(Vector3 worldPoint)
        {
            if (exclude != null && exclude != this && exclude.Contains(worldPoint))
                return false;
            Vector3 p = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(p.x) <= 0.5f && Mathf.Abs(p.y) <= 0.5f && Mathf.Abs(p.z) <= 0.5f;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = gizmoColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
