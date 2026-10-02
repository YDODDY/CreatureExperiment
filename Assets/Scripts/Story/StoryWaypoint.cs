using UnityEngine;
using CreatureExperiment.DailyLife;

namespace CreatureExperiment.Story
{
    /// <summary>
    /// One point of a <see cref="StoryNpc"/> route (the route is the children of one parent, in hierarchy order).
    /// Optional door handling at this point: <see cref="openDoor"/> - the NPC stops here, opens the door if it is closed
    /// and goes on once it stands open; <see cref="closeDoor"/> - on reaching this point the NPC closes the door behind it,
    /// unless the player is within <see cref="keepOpenPlayerDistance"/> of it (following right behind).
    /// The last point's forward is the way the NPC faces when it arrives.
    /// </summary>
    public class StoryWaypoint : MonoBehaviour
    {
        public SwingDoor openDoor;
        public SwingDoor closeDoor;
        public float keepOpenPlayerDistance = 5f;

        private void OnDrawGizmos()
        {
            Gizmos.color = openDoor != null || closeDoor != null ? new Color(0.3f, 0.8f, 1f) : new Color(0.2f, 1f, 0.4f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.15f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.1f, transform.forward * 0.5f);
        }
    }
}
