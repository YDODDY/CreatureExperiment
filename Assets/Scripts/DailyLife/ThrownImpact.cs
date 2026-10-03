using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// "What hit me?" checks for things that react to being struck - a loose raw egg / the eggs in a carton (thrown
    /// hard object), a breakable dish (hard body), a full can (sharp body).
    ///
    /// Hardness is the projectile's <see cref="ImpactClass"/> (Item Architecture v1): Hard / Sharp break fragile
    /// things, Soft never does. Only an item still <see cref="ImpactClass.Unset"/> falls back to the old identity
    /// guess (not Food / Ingredient = hard), and an item with neither (no tags either) is never treated as hard -
    /// a forgotten setting makes the rule not fire, rather than fire by accident.
    /// </summary>
    public static class ThrownImpact
    {
        /// <summary>How long after the throw a hit still counts as "thrown at it".</summary>
        public const float MaxThrowAge = 2f;
        /// <summary>The projectile must actually be moving into it (not a thrown item already lying still).</summary>
        public const float MinProjectileSpeed = 1f;
        /// <summary>Relative speed (m/s) at which a sharp body punctures a can / plastic bottle.</summary>
        public const float MinPunctureSpeed = 2.5f;

        /// <summary>The body on the other side of <paramref name="collision"/> is a thrown, hard, moving item.</summary>
        public static bool IsFragileBreakingHit(Collision collision)
        {
            if (collision == null || collision.rigidbody == null)
                return false;
            var projectile = collision.rigidbody.GetComponent<Interactable>();
            return IsFragileBreakingProjectile(projectile);
        }

        public static bool IsFragileBreakingProjectile(Interactable projectile)
        {
            if (projectile == null || projectile.IsHeld || projectile.LastThrowMode == ThrowMode.None)
                return false;
            if (Time.time - projectile.LastThrowTime > MaxThrowAge || projectile.PreImpactSpeed < MinProjectileSpeed)
                return false;
            return IsHard(projectile);
        }

        /// <summary>Hard or Sharp; for an <see cref="ImpactClass.Unset"/> item, the old rule (tagged, and not Food / Ingredient).</summary>
        public static bool IsHard(Interactable item)
        {
            if (item == null)
                return false;
            switch (item.Impact)
            {
                case ImpactClass.Hard:
                case ImpactClass.Sharp:
                    return true;
                case ImpactClass.Soft:
                    return false;
                default:
                    if (item.Tags == ItemTag.None)
                        return false; // unknown identity: never counts as a hard object
                    return !item.HasAny(ItemTag.Food | ItemTag.Ingredient);
            }
        }

        /// <summary>
        /// A sharp item, nobody holding it, meeting this body at <see cref="MinPunctureSpeed"/> or more (either one
        /// moving - a thrown knife, or a can thrown into a knife stuck in a wall). Physical only, not an attack.
        /// </summary>
        public static bool IsPuncturingHit(Collision collision)
        {
            if (collision == null || collision.rigidbody == null)
                return false;
            var other = collision.rigidbody.GetComponent<Interactable>();
            return other != null && other.Impact == ImpactClass.Sharp && !other.IsHeld
                && collision.relativeVelocity.magnitude >= MinPunctureSpeed;
        }
    }
}
