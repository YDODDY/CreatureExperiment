using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Shared bits for putting a food on a spot of a carried host (the frying pan's <see cref="FoodSlot"/>,
    /// a <see cref="MealPlate"/>): parent it, hold it still, turn its colliders back on (HandOver leaves
    /// them off) and keep it from colliding with the host.
    /// </summary>
    internal static class FoodMount
    {
        /// <summary>
        /// Seat <paramref name="item"/> on <paramref name="point"/>. The food keeps its own world size - a
        /// scaled plate / pan doesn't grow or shrink what is put on it. Returns the food's colliders.
        /// </summary>
        public static Collider[] Attach(Interactable item, Transform point)
        {
            Vector3 worldScale = item.transform.lossyScale;
            item.transform.SetParent(point, worldPositionStays: false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            Vector3 p = point.lossyScale;
            item.transform.localScale = new Vector3(worldScale.x / p.x, worldScale.y / p.y, worldScale.z / p.z);

            var body = item.Body;
            body.isKinematic = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            var list = new List<Collider>();
            foreach (var col in item.GetComponentsInChildren<Collider>(true))
            {
                col.enabled = true;
                list.Add(col);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Ignoring (while seated) is asserted on live colliders only; callers re-assert every frame.
        /// Un-ignoring (on release) covers every pair, enabled or not: an ignore survives a collider being disabled
        /// and re-enabled on this Unity version, and the food is usually picked up - its colliders already off -
        /// when the slot lets go. Skipping those left the food unable to touch that pan / plate for good.
        /// </summary>
        public static void IgnoreHost(Component host, Collider[] foodColliders, bool ignore)
        {
            if (foodColliders == null || host == null)
                return;
            foreach (var hostCol in host.GetComponents<Collider>())
            {
                if (hostCol == null || (ignore && (!hostCol.enabled || !hostCol.gameObject.activeInHierarchy)))
                    continue;
                foreach (var foodCol in foodColliders)
                    if (foodCol != null && (!ignore || (foodCol.enabled && foodCol.gameObject.activeInHierarchy)))
                        Physics.IgnoreCollision(hostCol, foodCol, ignore);
            }
        }
    }
}
