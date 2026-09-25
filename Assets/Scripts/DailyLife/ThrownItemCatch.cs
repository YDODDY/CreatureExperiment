using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A held-item receiver that also catches a valid item thrown onto it (F / Right Click) - the frying pan's
    /// food spot, a meal plate. Catching uses the receiver's own <see cref="IHeldItemReceiver.CanReceive"/> and
    /// <see cref="IHeldItemReceiver.Receive"/>, the same calls an E hand-over makes, so the end state is identical.
    /// </summary>
    public interface IThrownItemReceiver : IHeldItemReceiver
    {
        /// <summary>
        /// Catch assist: how far (m, horizontally) beyond the receiver's own collider bounds a thrown item coming
        /// down still counts as landing in it. A gameplay query only - the physical collider is not enlarged.
        /// </summary>
        float CatchMargin => 0.06f;
    }

    /// <summary>
    /// "Skill shot" catch, called from the thrown item's own collision (<see cref="FoodItem"/>, <see cref="RawEggBreak"/>).
    /// Always required: the player threw it (a recorded throw, at most <see cref="MaxThrowAge"/> s old, not caught
    /// yet) and the receiver takes this item right now (<see cref="IHeldItemReceiver.CanReceive"/> - kind, state,
    /// free spot). Then either
    /// - it hit the receiver's top (the contact faces the receiver's up), or
    /// - catch assist: it is coming down (not rising, not rolling) and is - or was one physics step ago - inside the
    ///   receiver's top zone: the receiver's collider bounds widened by <see cref="IThrownItemReceiver.CatchMargin"/>
    ///   horizontally, from a little under the rim up to a short height above it. That covers a rim hit and a
    ///   landing just past the edge (the item never touched the receiver - receivers within a small radius of the
    ///   impact are checked). A side hit (centre below the rim), a far miss, or something flying away does not count.
    /// One catch per throw.
    /// </summary>
    public static class ThrownItemCatch
    {
        public const float MaxThrowAge = 2f;
        private const float MinTopFacing = 0.5f;
        /// <summary>How far under the receiver's top the item's centre may be and still count as "at the rim".</summary>
        private const float RimAllowance = 0.03f;
        /// <summary>How far above the receiver's top the zone reaches (an item just about to land in it).</summary>
        private const float MaxAboveTop = 0.3f;
        /// <summary>Only an item coming down is assisted (m/s, negative = falling) - not one rolling or sliding along.</summary>
        private const float MaxAssistVerticalSpeed = -0.3f;
        /// <summary>Radius around the impact searched for a receiver the item just missed.</summary>
        private const float NearMissRadius = 0.35f;

        private static readonly Collider[] s_nearby = new Collider[16];

        public static bool TryCatch(Interactable item, Collision collision)
        {
            if (item == null || item.IsHeld || item.LastThrowMode == ThrowMode.None || collision == null)
                return false;
            if (Time.time - item.LastThrowTime > MaxThrowAge)
                return false;

            // 1. What it actually hit.
            var direct = collision.collider.GetComponentInParent<IThrownItemReceiver>();
            if (direct != null && TryReceiver(item, direct, collision))
                return true;

            // 2. A receiver it only just missed (landed on the counter right past the pan's rim).
            Vector3 at = item.Body.worldCenterOfMass;
            int n = Physics.OverlapSphereNonAlloc(at, NearMissRadius, s_nearby, ~0, QueryTriggerInteraction.Ignore);
            IThrownItemReceiver best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var candidate = s_nearby[i].GetComponentInParent<IThrownItemReceiver>();
                if (candidate == null || ReferenceEquals(candidate, direct) || !(candidate is Component c))
                    continue;
                float d = (c.transform.position - at).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = candidate; }
            }
            return best != null && TryReceiver(item, best, null);
        }

        private static bool TryReceiver(Interactable item, IThrownItemReceiver receiver, Collision collision)
        {
            if (!(receiver is Component host) || host.transform.IsChildOf(item.transform))
                return false;
            if (!receiver.CanReceive(item))
                return false;
            if (!(collision != null && HitTop(item, host, collision)) && !InCatchZone(item, host, receiver.CatchMargin))
                return false;

            if (!item.ClaimThrowOutcome(MaxThrowAge))
                return false;
            item.SetInFlight(false);
            receiver.Receive(item);
            return true;
        }

        // Landed on its top: the touched surface faces the receiver's up.
        private static bool HitTop(Interactable item, Component host, Collision collision)
        {
            ContactPoint contact = collision.GetContact(0);
            Vector3 normal = contact.normal;
            if (Vector3.Dot(normal, item.Body.worldCenterOfMass - contact.point) < 0f)
                normal = -normal; // from the contact toward the item (the side it came from)
            return Vector3.Dot(normal, host.transform.up) >= MinTopFacing;
        }

        private static bool InCatchZone(Interactable item, Component host, float margin)
        {
            if (item.PreImpactVelocity.y > MaxAssistVerticalSpeed)
                return false; // rising, flying level or rolling - not a landing
            if (!TryGetTopBounds(host, out Bounds top))
                return false;

            Vector3 comOffset = item.Body.worldCenterOfMass - item.Body.position;
            Vector3 now = item.Body.worldCenterOfMass;
            Vector3 before = item.PreImpactPosition + comOffset; // one physics step ago
            bool nowIn = InZone(now, top, margin);
            bool beforeIn = InZone(before, top, margin);
            if (!nowIn && !beforeIn)
                return false;

            // Only in the widened edge right now and heading away from the receiver, without having been over it
            // a step ago: it is passing by the edge, not landing in it.
            if (nowIn && !beforeIn && !InZone(now, top, 0f) && HeadingAway(now, item.PreImpactVelocity, top))
                return false;
            return true;
        }

        private static bool InZone(Vector3 p, Bounds top, float margin)
        {
            if (p.y < top.max.y - RimAllowance || p.y > top.max.y + MaxAboveTop)
                return false;
            float dx = Mathf.Max(Mathf.Max(top.min.x - p.x, p.x - top.max.x), 0f);
            float dz = Mathf.Max(Mathf.Max(top.min.z - p.z, p.z - top.max.z), 0f);
            return dx * dx + dz * dz <= margin * margin;
        }

        private static bool HeadingAway(Vector3 p, Vector3 velocity, Bounds top)
        {
            Vector3 toward = top.center - p;
            toward.y = 0f;
            velocity.y = 0f;
            return Vector3.Dot(toward, velocity) < 0f;
        }

        // The receiver's own solid colliders (not the food already sitting on it).
        private static bool TryGetTopBounds(Component host, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var col in host.GetComponents<Collider>())
            {
                if (col == null || !col.enabled || col.isTrigger)
                    continue;
                if (!any) { bounds = col.bounds; any = true; }
                else bounds.Encapsulate(col.bounds);
            }
            return any;
        }
    }
}
