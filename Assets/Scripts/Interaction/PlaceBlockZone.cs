using UnityEngine;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// Marks a trigger collider on this GameObject as a volume the player may not set an item down in
    /// (e.g. the middle of a sofa, where the player sits). <c>PlayerInteractor</c> rejects a Place - or a
    /// Swap that would leave the held item there - whose item footprint overlaps it. Marker only: no logic,
    /// no physics (keep its collider a trigger - every aim / overlap query in the project ignores triggers).
    /// </summary>
    public class PlaceBlockZone : MonoBehaviour
    {
    }
}
