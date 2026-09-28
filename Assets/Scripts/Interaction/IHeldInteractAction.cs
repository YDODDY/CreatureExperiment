using UnityEngine;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// A held item's own answer to Interact (E) for what the camera centre ray is aimed at - a TV remote
    /// changing the channel. <c>PlayerInteractor</c> asks it before its usual chain (World Use &gt; Receiver
    /// &gt; Pickup/Swap &gt; Place): true = the press is used, even if nothing changed; false = the usual chain
    /// runs as if the item had no action. The E counterpart of <see cref="IHeldPrimaryAction"/>.
    /// </summary>
    public interface IHeldInteractAction
    {
        /// <summary>Interact pressed while held. True if the press was used.</summary>
        bool TryInteract(Ray aim);

        /// <summary>
        /// The focus (label anchor) and label text this item wants for <paramref name="aim"/> - e.g. a
        /// control prompt on the TV it points at - or null to keep the normal focus.
        /// </summary>
        IFocusTarget GetAimFocus(Ray aim, out string label)
        {
            label = null;
            return null;
        }
    }
}
