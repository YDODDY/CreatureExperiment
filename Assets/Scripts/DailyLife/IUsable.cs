namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Something the player can look at and activate with the Interact key - a card terminal, a bed.
    /// Distinct from <c>Interactable</c> (which is pick-up-able): a usable has no Interactable, so
    /// <c>PlayerInteractor</c> and <see cref="PlayerActivator"/> never aim at the same object.
    /// </summary>
    public interface IUsable
    {
        void Use();
    }
}
