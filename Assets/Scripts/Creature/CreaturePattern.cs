using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Pattern (0.1): watches the same <see cref="Observation"/> stream the sibling
    /// <see cref="CreatureObservation"/> feeds to <see cref="CreatureMemory"/>, and looks for two
    /// independent things:
    ///   A. the Player repeatedly PLACING an object in the creature's NEAR band, and
    ///   B. the Player repeatedly THROWING an object that MISSES the creature but lands NEAR.
    ///
    /// Counting only. Each pattern has its own running count, its own Inspector threshold, its own
    /// latched "detected" flag, and its own "last object involved" reference. B is NOT a ratio /
    /// window / weight of A - it is the exact same shape of check on a different observation filter
    /// (Throw + <c>!ContactedCreature</c> excludes HIT; the NEAR check excludes FAR).
    ///
    /// Deliberately nothing else: Time is ignored, the memory list is never scanned, there is no
    /// Update, no reset/decay, no meaning attached (not "Gift", "Attack", "Play" or "Friend"), and no
    /// Hypothesis / Interest / Decision. It changes no creature behaviour on its own - it only exposes
    /// the counts, the flags, and the two "last object" references for inspection and for the probe
    /// components to read.
    ///
    /// <see cref="LastPlaceNearObject"/> / <see cref="LastThrowMissNearObject"/> are running references,
    /// the same spirit as the running count scalars: each just remembers the last object involved in a
    /// qualifying observation of its kind. NOT a memory query - the list is still never scanned.
    /// </summary>
    public class CreaturePattern : MonoBehaviour
    {
        [Header("Repeated Place Near (Pattern A)")]
        [Tooltip("How many PLACE-in-NEAR observations must accumulate before Pattern A is considered detected.")]
        [SerializeField] private int repeatedPlaceNearThreshold = 3;

        [Header("Repeated Throw Miss Near (Pattern B)")]
        [Tooltip("How many THROW-MISS-in-NEAR observations must accumulate before Pattern B is considered detected. HIT throws (ContactedCreature) and FAR throws are not counted.")]
        [SerializeField] private int repeatedThrowMissNearThreshold = 3;

        [Header("State (read-only, shown for debugging)")]
        [Tooltip("Running count of PLACE observations that fell in the creature's NEAR band.")]
        [SerializeField] private int placeNearCount;
        [Tooltip("Latches true once placeNearCount reaches its threshold. Never cleared in 0.1.")]
        [SerializeField] private bool repeatedPlaceNearDetected;
        [Tooltip("The Interactable from the most recent qualifying PLACE-in-NEAR observation. The 'conversation piece' CreatureProbe reaches for. Updated every qualifying observation; not memory-queried.")]
        [SerializeField] private Interactable lastPlaceNearObject;
        [Tooltip("Running count of THROW observations that missed the creature and landed in its NEAR band.")]
        [SerializeField] private int throwMissNearCount;
        [Tooltip("Latches true once throwMissNearCount reaches its threshold. Never cleared in 0.1.")]
        [SerializeField] private bool repeatedThrowMissNearDetected;
        [Tooltip("The Interactable from the most recent qualifying THROW-MISS-in-NEAR observation. CreatureThrowProbe reaches for this.")]
        [SerializeField] private Interactable lastThrowMissNearObject;

        /// <summary>True once the Player has PLACED in NEAR at least <see cref="repeatedPlaceNearThreshold"/> times. Latched.</summary>
        public bool RepeatedPlaceNearDetected => repeatedPlaceNearDetected;

        /// <summary>How many qualifying PLACE-in-NEAR observations have been seen so far.</summary>
        public int PlaceNearCount => placeNearCount;

        /// <summary>
        /// The object involved in the most recent qualifying PLACE-in-NEAR observation, or null if there
        /// has not been one. A running reference only - no scan, no history. <c>CreatureProbe</c> snapshots
        /// this when it starts, so a later change here does not move an in-progress probe.
        /// </summary>
        public Interactable LastPlaceNearObject => lastPlaceNearObject;

        /// <summary>True once the Player has THROWN and MISSED into NEAR at least <see cref="repeatedThrowMissNearThreshold"/> times. Latched.</summary>
        public bool RepeatedThrowMissNearDetected => repeatedThrowMissNearDetected;

        /// <summary>How many qualifying THROW-MISS-in-NEAR observations have been seen so far.</summary>
        public int ThrowMissNearCount => throwMissNearCount;

        /// <summary>
        /// The object involved in the most recent qualifying THROW-MISS-in-NEAR observation, or null if
        /// there has not been one. Running reference only. <c>CreatureThrowProbe</c> snapshots this at start.
        /// </summary>
        public Interactable LastThrowMissNearObject => lastThrowMissNearObject;

        /// <summary>
        /// Fed one observation at a time by <see cref="CreatureObservation"/>, right alongside
        /// <see cref="CreatureMemory.Record"/>. O(1): filter, maybe increment, maybe latch. No scan.
        /// The two patterns are checked independently - an observation can match at most one anyway.
        /// </summary>
        public void Observe(Observation observation)
        {
            bool near = observation.Proximity == Proximity.Near;

            // Pattern A: Player repeatedly PLACING an object in NEAR. (Time unused.)
            if (near && observation.Action == PhysicalEventKind.Place)
            {
                lastPlaceNearObject = observation.Object;
                placeNearCount++;

                if (!repeatedPlaceNearDetected && placeNearCount >= repeatedPlaceNearThreshold)
                {
                    repeatedPlaceNearDetected = true;
                    Debug.Log($"[CreaturePattern] Repeated Place Near detected (count {placeNearCount} >= threshold {repeatedPlaceNearThreshold})");
                }
            }

            // Pattern B: Player repeatedly THROWING an object that misses the creature but lands NEAR.
            // !ContactedCreature excludes THROW HIT; the near check excludes THROW FAR.
            if (near && observation.Action == PhysicalEventKind.Throw && !observation.ContactedCreature)
            {
                lastThrowMissNearObject = observation.Object;
                throwMissNearCount++;

                if (!repeatedThrowMissNearDetected && throwMissNearCount >= repeatedThrowMissNearThreshold)
                {
                    repeatedThrowMissNearDetected = true;
                    Debug.Log($"[CreaturePattern] Repeated Throw Miss Near detected (count {throwMissNearCount} >= threshold {repeatedThrowMissNearThreshold})");
                }
            }
        }

        private void OnValidate()
        {
            repeatedPlaceNearThreshold = Mathf.Max(1, repeatedPlaceNearThreshold);
            repeatedThrowMissNearThreshold = Mathf.Max(1, repeatedThrowMissNearThreshold);
        }
    }
}
