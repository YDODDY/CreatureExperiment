namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Something the player can look at and activate with the Interact key - a door, a lid, a card
    /// terminal, a bed - whether or not an item is held. Distinct from <c>Interactable</c> (which is
    /// pick-up-able). <c>PlayerInteractor</c> routes the press; any gameplay condition (e.g. the bed
    /// only works at bedtime) is decided inside <see cref="Use"/>.
    /// </summary>
    public interface IUsable
    {
        void Use();
    }
}
