namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// A receiver that only cares about SOME held items (a packing box takes a work item / tape /
    /// sticker, a wall only takes a sticker). For any other held item it is not a receiver at all:
    /// <c>PlayerInteractor</c> treats it as a plain object - the press falls through to Pickup / Swap /
    /// Place, and it may be placed on. Plain <see cref="IHeldItemReceiver"/>s (a GarbageDump) keep
    /// the old rule: they own the aim for every held item.
    /// </summary>
    public interface IOptionalHeldItemReceiver : IHeldItemReceiver
    {
        /// <summary>True if this receiver handles <paramref name="item"/> at all (accept or reject). Null = empty hands.</summary>
        bool AppliesTo(Interactable item);

        /// <summary>How far away it can be handed to, in metres (at least the normal pickup reach is always used).</summary>
        float MaxReach { get; }
    }
}
