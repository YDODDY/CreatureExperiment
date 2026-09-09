using UnityEngine;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// Something the player's aim can highlight and name: a pick-up-able <see cref="Interactable"/>,
    /// or a non-pickup usable prop (a card terminal, a bed) via a small view component. The Focus
    /// feedback in <c>PlayerInteractor</c> / <c>FocusLabel</c> drives this. It does NOT decide what
    /// pressing Interact does - that stays with each object's own interaction path (pickup, or
    /// <c>IUsable.Use</c>).
    /// </summary>
    public interface IFocusTarget
    {
        /// <summary>Name to show in the focus label.</summary>
        string FocusName { get; }

        /// <summary>Transform the label anchors above; its child renderers give the label its height.</summary>
        Transform FocusTransform { get; }

        /// <summary>Turn this object's focus highlight (outline) on or off.</summary>
        void SetFocused(bool focused);
    }
}
