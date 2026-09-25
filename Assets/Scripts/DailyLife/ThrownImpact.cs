using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// "Something thrown at it" check for fragile food (a loose raw egg, the eggs in a carton): the other body in a
    /// collision is an item the player just threw (F / Right Click) that is not itself food. Food / ingredients
    /// (bread, bacon, another carton...) bump without breaking anything; a knife, pan, plate, tape roll, box, can do.
    ///
    /// Identity only (<see cref="ItemTag"/>): no weight / hardness / damage. An item whose tags were never set
    /// (<see cref="ItemTag.None"/>) is not treated as a hard object - a forgotten tag makes the rule not fire,
    /// rather than fire by accident.
    /// </summary>
    public static class ThrownImpact
    {
        /// <summary>How long after the throw a hit still counts as "thrown at it".</summary>
        public const float MaxThrowAge = 2f;
        /// <summary>The projectile must actually be moving into it (not a thrown item already lying still).</summary>
        public const float MinProjectileSpeed = 1f;

        /// <summary>The body on the other side of <paramref name="collision"/> is a thrown, known, non-food item.</summary>
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
            if (projectile.Tags == ItemTag.None)
                return false; // unknown identity: never counts as a hard object
            return !projectile.HasAny(ItemTag.Food | ItemTag.Ingredient);
        }
    }
}
