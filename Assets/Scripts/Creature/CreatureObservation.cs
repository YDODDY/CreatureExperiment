using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Two-band spatial relevance of a thing to the creature. Not visibility - FAR is not "unseen",
    /// only "currently of low relevance to the creature".
    /// </summary>
    public enum Proximity
    {
        /// <summary>Close enough that a relationship to the creature could arise.</summary>
        Near,
        /// <summary>Currently of low relevance to the creature (not necessarily out of sight).</summary>
        Far
    }

    /// <summary>
    /// One observation, as data. Purely the physical facts of a single Player release action as the
    /// creature could register it - no interpretation (Gift/Attack/Friend), no witness/FOV, no memory.
    /// Nothing here is stored, accumulated, or fed into patterns/hypotheses yet; it exists only so the
    /// three log paths below build the same value before printing it.
    /// </summary>
    public readonly struct Observation
    {
        /// <summary>The object that was placed or thrown.</summary>
        public readonly Interactable Object;

        /// <summary>Which physical release action this was (Place / Throw). Not a semantic label.</summary>
        public readonly PhysicalEventKind Action;

        /// <summary>True only when a thrown object actually touched the creature (Throw HIT).</summary>
        public readonly bool ContactedCreature;

        /// <summary>Near / Far band of <see cref="DistanceFromCreature"/>.</summary>
        public readonly Proximity Proximity;

        /// <summary>Flat XZ distance (metres) from the creature root at the moment of observation.</summary>
        public readonly float DistanceFromCreature;

        /// <summary>
        /// When the observation happened (<see cref="UnityEngine.Time.time"/>). Recorded as origin
        /// information only - not stored or analysed anywhere yet.
        /// </summary>
        public readonly float Time;

        public Observation(Interactable obj, PhysicalEventKind action, bool contactedCreature,
            Proximity proximity, float distanceFromCreature, float time)
        {
            Object = obj;
            Action = action;
            ContactedCreature = contactedCreature;
            Proximity = proximity;
            DistanceFromCreature = distanceFromCreature;
            Time = time;
        }
    }

    /// <summary>
    /// Observation (0.1): listens for <see cref="PhysicalEvents"/> and reports two purely physical facts
    /// about Player release actions - no interpretation (Gift/Threat/Attack), no FOV/witness check, no
    /// Memory, no Decision/Intent.
    ///
    /// PLACE (step 2): logs the flat XZ distance between the placed <see cref="Interactable"/> and this
    /// creature's root, plus which spatial band it falls in (NEAR below <see cref="nearMax"/>, FAR at or
    /// above it - tunable in the Inspector).
    ///
    /// THROW (step 3): does NOT log anything at throw time. Instead the thrown object is added to
    /// <see cref="_watchedThrows"/> (with a per-object "how long it has been still" timer) and watched
    /// two ways:
    ///   - HIT: <see cref="OnCollisionEnter"/> - this GameObject already carries the creature's own
    ///     <c>CapsuleCollider</c> (see the scene), so no extra collider or component is needed. A HIT is
    ///     logged the first time a watched object's collider actually touches the creature, and it is
    ///     removed from the set at that same moment (so repeated contact from one throw logs only once).
    ///   - MISS: <see cref="FixedUpdate"/> polls each watched object's <see cref="Rigidbody"/>. Once its
    ///     linear AND angular speed have both stayed below <see cref="settleLinearSpeed"/> /
    ///     <see cref="settleAngularSpeed"/> continuously for <see cref="settleTime"/>, the throw is
    ///     considered come-to-rest: it is removed from the set and its final flat XZ distance to the
    ///     creature is logged and classified with the same band as PLACE.
    /// A watched object that stops being <see cref="Interactable.IsInFlight"/> for any other reason
    /// (the player catches it mid-air, or a HIT already resolved it) is dropped from the set silently -
    /// that is not a miss. There is deliberately no max-watch timeout yet: if throws are seen to never
    /// settle in practice, one gets added then. Closest-approach, trajectory, peak speed, FOV/witness
    /// checks and any semantic reading of the throw are still not computed here at all.
    ///
    /// Each of the three log paths (PLACE, THROW HIT, THROW MISS) first builds one <see cref="Observation"/>
    /// value and then prints from it. Nothing about that value is kept afterwards.
    ///
    /// Only <c>PlayerInteractor</c> raises <see cref="PhysicalEvents"/> right now (see its own class
    /// comment - the creature's own Place/Throw in <see cref="CreaturePickup"/> deliberately does not),
    /// so every event received here is already a Player action; no extra actor filtering added on top of
    /// that for now. That is also why the creature's own throws can never register a HIT: they never
    /// enter <see cref="_watchedThrows"/> in the first place.
    /// </summary>
    [RequireComponent(typeof(CreatureMemory))]
    [RequireComponent(typeof(CreaturePattern))]
    public class CreatureObservation : MonoBehaviour
    {
        [Header("Distance band (metres, flat XZ) - spatial relevance only, tune freely")]
        [Tooltip("Flat XZ distance below this is NEAR (a relationship to the creature could arise here). At or above it is FAR - currently low relevance to the creature, not necessarily out of sight.")]
        [SerializeField] private float nearMax = 2.5f;

        [Header("Throw settle (MISS detection)")]
        [Tooltip("A thrown object counts as 'moving' while its linear speed (m/s) is at/above this.")]
        [SerializeField] private float settleLinearSpeed = 0.05f;
        [Tooltip("...or while its angular speed (rad/s) is at/above this.")]
        [SerializeField] private float settleAngularSpeed = 0.5f;
        [Tooltip("Both speeds must stay below their thresholds continuously for this long (seconds) before the throw is logged as a MISS.")]
        [SerializeField] private float settleTime = 0.3f;

        // Player-thrown objects currently being watched, mapped to how long (seconds) each has been
        // continuously below the settle speed thresholds. Added on THROW; removed on the first actual
        // collision with the creature (HIT), on coming to rest (MISS), or when it stops being in flight
        // for any other reason (mid-air re-pickup).
        private readonly Dictionary<Interactable, float> _watchedThrows = new Dictionary<Interactable, float>();

        // Reused each FixedUpdate so the watched set can be mutated while iterating it. Never holds
        // state between frames.
        private readonly List<Interactable> _settleWorkList = new List<Interactable>();

        // Sibling consumers every observation below is handed to. Required components, always present.
        // _memory stores it; _pattern counts the ones that matter to it. They do not know about each other.
        private CreatureMemory _memory;
        private CreaturePattern _pattern;

        /// <summary>
        /// Monotonically increasing count of every confirmed Player-thrown-object HIT on this creature
        /// (the exact <see cref="OnCollisionEnter"/> edge below - never decreases, never re-fires for
        /// the same throw's continued physics contact, since the object is removed from
        /// <see cref="_watchedThrows"/> on the same call). A read-only seam for a future consumer (e.g.
        /// a Player-salience arbiter) to detect a NEW hit via a simple snapshot-and-compare, the same
        /// idiom as PlayerObservation's HighViewEpisodeSerial. Adds no new detection - this is the
        /// SAME hit edge <see cref="OnCollisionEnter"/> already used for Memory/Pattern.
        /// </summary>
        public int ThrowHitSerial { get; private set; }

        /// <summary>World position of the most recent confirmed throw HIT. Meaningless before the first one.</summary>
        public Vector3 LastHitPosition { get; private set; }

        /// <summary>The Interactable involved in the most recent confirmed throw HIT.</summary>
        public Interactable LastHitInteractable { get; private set; }

        private void Awake()
        {
            _memory = GetComponent<CreatureMemory>();
            _pattern = GetComponent<CreaturePattern>();
        }

        private void OnEnable()
        {
            PhysicalEvents.Raised += OnPhysicalEvent;
        }

        private void OnDisable()
        {
            PhysicalEvents.Raised -= OnPhysicalEvent;
            _watchedThrows.Clear();
        }

        private void OnPhysicalEvent(PhysicalEvent evt)
        {
            if (evt.Interactable == null)
                return;

            if (evt.Kind == PhysicalEventKind.Place)
            {
                float distance = FlatDistance(evt.Interactable.transform.position);
                var observation = new Observation(
                    evt.Interactable,
                    PhysicalEventKind.Place,
                    contactedCreature: false,
                    Classify(distance),
                    distance,
                    Time.time);

                Debug.Log($"[CreatureObservation] PLACE {observation.Object.DisplayName} / Distance: {observation.DistanceFromCreature:F2}m / {ProximityLabel(observation.Proximity)}");
                _memory.Record(observation);
                _pattern.Observe(observation);
            }
            else if (evt.Kind == PhysicalEventKind.Throw)
            {
                _watchedThrows[evt.Interactable] = 0f;
            }
        }

        // MISS detection. Walk every watched throw; a throw resolves here only by coming to rest.
        private void FixedUpdate()
        {
            if (_watchedThrows.Count == 0)
                return;

            float linearSqrThreshold = settleLinearSpeed * settleLinearSpeed;
            float angularSqrThreshold = settleAngularSpeed * settleAngularSpeed;

            _settleWorkList.Clear();
            _settleWorkList.AddRange(_watchedThrows.Keys);

            foreach (Interactable thrown in _settleWorkList)
            {
                // Destroyed, or flight already resolved another way (player caught it mid-air, or a HIT
                // handled it in OnCollisionEnter): stop watching, and do NOT log a miss.
                if (thrown == null || !thrown.IsInFlight)
                {
                    _watchedThrows.Remove(thrown);
                    continue;
                }

                Rigidbody body = thrown.Body;
                bool nearlyStopped =
                    body.linearVelocity.sqrMagnitude < linearSqrThreshold &&
                    body.angularVelocity.sqrMagnitude < angularSqrThreshold;

                if (!nearlyStopped)
                {
                    // Any single frame back above threshold restarts the settle timer.
                    _watchedThrows[thrown] = 0f;
                    continue;
                }

                float stillTime = _watchedThrows[thrown] + Time.fixedDeltaTime;
                if (stillTime < settleTime)
                {
                    _watchedThrows[thrown] = stillTime;
                    continue;
                }

                // Settled: this throw missed the creature and has come to rest.
                _watchedThrows.Remove(thrown);
                thrown.SetInFlight(false);

                float distance = FlatDistance(thrown.transform.position);
                var observation = new Observation(
                    thrown,
                    PhysicalEventKind.Throw,
                    contactedCreature: false,
                    Classify(distance),
                    distance,
                    Time.time);

                Debug.Log($"[CreatureObservation] THROW {observation.Object.DisplayName} / MISS / Distance: {observation.DistanceFromCreature:F2}m / {ProximityLabel(observation.Proximity)}");
                _memory.Record(observation);
                _pattern.Observe(observation);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_watchedThrows.Count == 0)
                return;

            var interactable = collision.collider.GetComponentInParent<Interactable>();
            if (interactable == null || !_watchedThrows.Remove(interactable))
                return;

            // Flight resolved by a hit - it's a normal world object again. (A miss instead resolves in
            // FixedUpdate once the object comes to rest.)
            interactable.SetInFlight(false);

            // The one HIT edge - see ThrowHitSerial's doc comment. Set here, once, same call as
            // everything else below; a multi-frame physics contact from the same throw never reaches
            // this line again (the object was just removed from _watchedThrows above).
            ThrowHitSerial++;
            LastHitPosition = interactable.transform.position;
            LastHitInteractable = interactable;

            float distance = FlatDistance(interactable.transform.position);
            var observation = new Observation(
                interactable,
                PhysicalEventKind.Throw,
                contactedCreature: true,
                Classify(distance),
                distance,
                Time.time);

            Debug.Log($"[CreatureObservation] THROW {observation.Object.DisplayName} / HIT");
            _memory.Record(observation);
            _pattern.Observe(observation);
        }

        // Same flat XZ convention CreatureMovement/CreaturePickup already use for every other distance
        // check in this project.
        private float FlatDistance(Vector3 worldPos)
        {
            Vector3 flat = worldPos - transform.position;
            flat.y = 0f;
            return flat.magnitude;
        }

        private Proximity Classify(float distance) =>
            distance < nearMax ? Proximity.Near : Proximity.Far;

        private static string ProximityLabel(Proximity proximity) =>
            proximity == Proximity.Near ? "NEAR" : "FAR";

        private void OnValidate()
        {
            nearMax = Mathf.Max(0f, nearMax);
            settleLinearSpeed = Mathf.Max(0f, settleLinearSpeed);
            settleAngularSpeed = Mathf.Max(0f, settleAngularSpeed);
            settleTime = Mathf.Max(0f, settleTime);
        }
    }
}
