using System;
using UnityEngine;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// Which physical release action happened. No meaning attached - PLACE is not Gift, THROW is not
    /// Attack. That interpretation is later, separate work.
    /// </summary>
    public enum PhysicalEventKind
    {
        Place,
        Throw
    }

    /// <summary>
    /// One objective physical fact: <see cref="Source"/> released <see cref="Interactable"/> as a
    /// <see cref="Kind"/> action. Nothing more - no distance to anything, no trajectory, no hit
    /// detection, no witness/visibility check. Those are later, separate Observation work.
    /// </summary>
    public readonly struct PhysicalEvent
    {
        public readonly PhysicalEventKind Kind;
        public readonly Interactable Interactable;

        /// <summary>Whoever performed the release - e.g. the <c>PlayerInteractor</c> that raised this. Not a semantic label; just the actual object that did it.</summary>
        public readonly UnityEngine.Object Source;

        public PhysicalEvent(PhysicalEventKind kind, Interactable interactable, UnityEngine.Object source)
        {
            Kind = kind;
            Interactable = interactable;
            Source = source;
        }
    }

    /// <summary>
    /// Minimal world-event bus for physical release actions (Observation 0.1, step 1). A raiser reports
    /// only what it actually, physically did - see <c>PlayerInteractor.Place</c> / <c>PlayerInteractor.Throw</c>,
    /// the only raisers right now; the creature's own Place/Throw (<c>CreaturePickup</c>) deliberately
    /// does not raise this, since these are Player-action events, not "any release" events.
    ///
    /// This does not decide what an event means (Gift / Attack / Threat), whether it was witnessed, or
    /// remember it anywhere - that is future Creature Observation work, not started here. For now the
    /// only consumer is the console log below, just enough to verify events fire correctly and exactly once.
    /// </summary>
    public static class PhysicalEvents
    {
        public static event Action<PhysicalEvent> Raised;

        public static void Raise(PhysicalEventKind kind, Interactable interactable, UnityEngine.Object source)
        {
            if (interactable == null)
                return;

            Debug.Log($"[PhysicalEvent] {(source != null ? source.name : "Unknown")} {kind.ToString().ToUpperInvariant()} {interactable.DisplayName}");
            Raised?.Invoke(new PhysicalEvent(kind, interactable, source));
        }
    }
}
