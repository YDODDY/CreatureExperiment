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
        /// <summary>Seat <paramref name="item"/> on <paramref name="point"/>. Returns the food's colliders.</summary>
        public static Collider[] Attach(Interactable item, Transform point)
        {
            item.transform.SetParent(point, worldPositionStays: false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;

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
        /// Only live colliders: a disabled collider can't take the call, and disabling one already drops
        /// its ignore pairs (picking up / placing the host toggles its colliders, so callers re-assert).
        /// </summary>
        public static void IgnoreHost(Component host, Collider[] foodColliders, bool ignore)
        {
            if (foodColliders == null)
                return;
            foreach (var hostCol in host.GetComponents<Collider>())
            {
                if (hostCol == null || !hostCol.enabled || !hostCol.gameObject.activeInHierarchy)
                    continue;
                foreach (var foodCol in foodColliders)
                    if (foodCol != null && foodCol.enabled && foodCol.gameObject.activeInHierarchy)
                        Physics.IgnoreCollision(hostCol, foodCol, ignore);
            }
        }
    }
}
