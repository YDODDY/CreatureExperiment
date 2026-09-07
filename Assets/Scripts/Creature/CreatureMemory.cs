using System.Collections.Generic;
using UnityEngine;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// The creature's raw memory (prototype 0.1): every <see cref="Observation"/> that the sibling
    /// <see cref="CreatureObservation"/> produces, kept in the order it happened.
    ///
    /// Append-only, and nothing else. No importance test (FAR observations are kept too), no
    /// decay / forgetting, no confidence, no semantic label, no pattern / hypothesis / interest /
    /// decision / probe, no query API beyond the read-only list below. This step exists only to
    /// confirm one thing: past observations actually persist on the creature. Reading them,
    /// interpreting them, or acting on them is later, separate work.
    /// </summary>
    public class CreatureMemory : MonoBehaviour
    {
        [Tooltip("Editor aid only: log every observation as it is stored, with the running total.")]
        [SerializeField] private bool logOnRecord = true;

        // Oldest first. Only ever appended to - never reordered, replaced, or trimmed.
        private readonly List<Observation> _observations = new List<Observation>();

        /// <summary>Every stored observation, oldest first. Read-only; the only way in is <see cref="Record"/>.</summary>
        public IReadOnlyList<Observation> Observations => _observations;

        /// <summary>How many observations have been stored so far.</summary>
        public int Count => _observations.Count;

        /// <summary>
        /// Append one observation. Called by <see cref="CreatureObservation"/> for every observation it
        /// makes (PLACE NEAR/FAR, THROW HIT, THROW MISS NEAR/FAR). Never drops or reorders anything.
        /// </summary>
        public void Record(Observation observation)
        {
            _observations.Add(observation);

            if (logOnRecord)
                Debug.Log($"[CreatureMemory] stored #{_observations.Count}: {Describe(observation)}");
        }

        [ContextMenu("Log Memory Contents")]
        private void LogMemoryContents()
        {
            Debug.Log($"[CreatureMemory] {_observations.Count} observation(s), oldest first:");
            for (int i = 0; i < _observations.Count; i++)
                Debug.Log($"  #{i + 1}: {Describe(_observations[i])}");
        }

        private static string Describe(Observation observation)
        {
            string name = observation.Object != null ? observation.Object.DisplayName : "<destroyed>";
            return $"{observation.Action} {name} / contact={observation.ContactedCreature} / " +
                   $"{observation.Proximity} / {observation.DistanceFromCreature:F2}m @ t={observation.Time:F2}";
        }
    }
}
