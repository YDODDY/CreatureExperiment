namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// What an item is like for the things it runs into - a physical property, not identity (<see cref="ItemTag"/>)
    /// and not intent. Read by the thing that is hit: a raw egg / an egg carton asks "was I hit by something hard?",
    /// a full can asks "was I hit by something sharp?". Sharp is a plain physical property (it can puncture),
    /// never "weapon" or "attack" - interpreting a throw is separate, later work.
    /// </summary>
    public enum ImpactClass
    {
        /// <summary>Not set yet (migration fallback): the old identity rule decides - see <c>ThrownImpact</c>.</summary>
        Unset = 0,
        /// <summary>Food, paper, cloth, soft packaging, a cigarette: never breaks what it hits.</summary>
        Soft = 1,
        /// <summary>An ordinary solid object (pan, plate, can, jar, torch): breaks fragile things it hits.</summary>
        Hard = 2,
        /// <summary>Hard, and can also puncture (knife): everything Hard does, plus puncture reactions.</summary>
        Sharp = 3,
    }
}
