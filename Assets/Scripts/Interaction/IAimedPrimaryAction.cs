using UnityEngine;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// A Left Click action on something lying in the world, aimed at through the pick-up-able item it belongs to
    /// (take one egg / strip / slice / cigarette out of a container that sits on a table). The container itself stays
    /// the plain E pickup - one focus, two keys: E = the container, Left Click = one of its contents.
    /// Found with <see cref="Find"/> on the aimed <see cref="Interactable"/> - or on the held one: a container in the hand
    /// answers the same Left Click (the content goes into a free slot, the container stays in the hand).
    /// </summary>
    public interface IAimedPrimaryAction
    {
        /// <summary>This action answers Left Click at all (the component may be in its old E-only mode).</summary>
        bool PrimaryEnabled { get; }

        /// <summary>The focus label line for Left Click ("LMB 계란 꺼내기 (6/6)"), live.</summary>
        string PrimaryHint { get; }

        /// <summary>Left Click while aimed at the item. True if the press was used (even when refused with a notice).</summary>
        bool TryAimedPrimary();

        /// <summary>
        /// A fixed world action that beats whatever is in the hand (hiding in a locker): Left Click on it never eats /
        /// drinks / uses the held item first. Default false (containers: the held item's own Left Click comes first).
        /// </summary>
        bool OverridesHeldItem => false;
    }

    public static class AimedPrimary
    {
        /// <summary>The enabled <see cref="IAimedPrimaryAction"/> on <paramref name="item"/> (or its children), or null.</summary>
        public static IAimedPrimaryAction Find(Interactable item)
        {
            if (item == null)
                return null;
            foreach (var action in item.GetComponentsInChildren<IAimedPrimaryAction>(true))
                if (action as Object != null && action.PrimaryEnabled)
                    return action;
            return null;
        }
    }
}
