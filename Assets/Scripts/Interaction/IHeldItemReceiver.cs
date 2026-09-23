namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// Something the player can hand the currently held <see cref="Interactable"/> to by aiming at it and
    /// pressing Interact - right now only a GarbageDump ("버리기"). <c>PlayerInteractor</c> asks it while
    /// carrying; when <see cref="CanReceive"/> is true it gets the focus (outline + <see cref="IFocusTarget.FocusName"/>)
    /// and Interact hands the item over; when false it shows <see cref="GetRejectPrompt"/> and Interact does
    /// nothing. It is never a placement surface. With empty hands it is never a focus.
    /// </summary>
    public interface IHeldItemReceiver : IFocusTarget
    {
        /// <summary>True if <paramref name="item"/> (the item the player is holding) can be handed over right now.</summary>
        bool CanReceive(Interactable item);

        /// <summary>
        /// Prompt shown while the player aims <paramref name="item"/> at this receiver and
        /// <see cref="CanReceive"/> is false (e.g. "버릴 수 없습니다"), or null for no label (e.g. a closed
        /// dump). Either way the receiver still owns the aim: the Interact press does nothing - it
        /// never falls back to placing the item on it.
        /// </summary>
        string GetRejectPrompt(Interactable item);

        /// <summary>
        /// Take <paramref name="item"/>. The caller has already released its hold and cleared its own
        /// reference, so the receiver owns what happens to the object next (a GarbageDump destroys it).
        /// </summary>
        void Receive(Interactable item);
    }
}
