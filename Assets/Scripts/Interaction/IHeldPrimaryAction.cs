using UnityEngine;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// A held item's own Left Click action (knife, fragile sticker, tape roll). The player's primary-action
    /// router (<c>MealEater</c>) calls <see cref="PrimaryPress"/> on the press frame with the camera centre
    /// ray. One-shot items do their thing there. An item that needs press / hold / release (the tape roll)
    /// returns true and reports <see cref="PrimaryActive"/> until it ends; the router then calls
    /// <see cref="PrimaryHold"/> every frame the button stays down, <see cref="PrimaryRelease"/> on release,
    /// and <see cref="PrimaryCancel"/> if the item leaves the hand or control is taken away.
    /// While <see cref="PrimaryActive"/> is true the interactor ignores E / F / Right Click.
    /// Items without this interface fall back to eating (if edible).
    /// </summary>
    public interface IHeldPrimaryAction
    {
        /// <summary>Left Click pressed while held. True if the press was used.</summary>
        bool PrimaryPress(Ray aim);

        /// <summary>True while a press / hold / release action is in progress.</summary>
        bool PrimaryActive => false;

        void PrimaryHold(Ray aim) { }
        void PrimaryRelease(Ray aim) { }
        void PrimaryCancel() { }
    }
}
