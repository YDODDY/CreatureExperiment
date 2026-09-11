using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.Creature;

namespace CreatureExperiment.Creature.Brain
{
    /// <summary>
    /// Reaction Episode 0.4 - ties individual sensor values to a single moment in time, and - new in
    /// 0.4 - keeps two separate accounts of what happened: what the WORLD/game state can prove
    /// happened (every Player sensor value, always), and what the CREATURE could actually have known
    /// (only the slice of that a genuinely aware Creature could observe). Reads
    /// <see cref="PlayerObservation"/>, <see cref="CreatureEncounterObservation"/> and
    /// <c>CreaturePerception.IsPlayerPerceived</c> (the ONE piece of existing Creature AI state this
    /// reads - see <see cref="creaturePerception"/>) all read-only; never writes to any of them, never
    /// touches Creature AI/behaviour/decisions.
    ///
    /// Core design principle: the game engine is omniscient, the Creature is not. If the Player
    /// discovers the Creature, stares at it, and quietly backs away while the Creature never perceives
    /// the Player at all, the World Reaction exists in full but the CREATURE OBSERVED side of the same
    /// Episode must show nothing - a Creature that never perceived the Player cannot have "known" the
    /// Player reacted. This is enforced structurally, not by filtering afterward:
    /// <see cref="TickCollectingAfter"/> only ever writes to the Observed* properties INSIDE
    /// <c>if (awareNow)</c> - on a frame where the Creature is not currently perceiving, those
    /// accumulators are simply never touched, so there is no code path for pre-awareness reaction to
    /// leak into what the Creature "observed".
    ///
    /// Two Event kinds now exist (<see cref="ReactionEventType"/>):
    ///  - <b>CreatureVisualAcquired</b>: the PLAYER's camera sustained-acquired the Creature
    ///    (<see cref="CreatureEncounterObservation.VisualAcquiredSerial"/> edge - viewport + LOS, its
    ///    own hysteresis, see that class). Works at ANY distance, not gated by
    ///    <see cref="reactionEnterRange"/>/<c>observationRange</c> - discovery from far away must be
    ///    representable. Origin = <see cref="ReactionEpisodeOrigin.PlayerDiscovery"/>: the Player found
    ///    the Creature; from the Creature's side (if/when it later perceives the Player) this reads as
    ///    "huh, that Player is now looking at / near me", a reactive discovery, never an experiment the
    ///    Creature ran.
    ///  - <b>CreatureNear</b> (unchanged trigger: <see cref="CreatureEncounterObservation.DistanceToCreature"/>
    ///    crossing <see cref="reactionEnterRange"/>, with the same enter/exit hysteresis as before).
    ///    Origin = <see cref="ReactionEpisodeOrigin.SpatialEncounter"/>, deliberately NOT PlayerDiscovery
    ///    - simple proximity says nothing about whether the Player noticed the Creature (it could be
    ///    right behind them). <see cref="ReactionEpisodeOrigin.CreatureInitiated"/> is reserved for a
    ///    future step where the Creature's own Probe experiments feed this recorder - NOT built or
    ///    wired in 0.4; the enum value exists purely so this architecture does not need reshaping later.
    ///
    /// Both trigger kinds are independent edges checked every frame; whichever kind is NOT the one that
    /// started the current Episode, if it also fires while <see cref="EpisodeState.CollectingAfter"/>,
    /// is recorded as a Secondary* event (<see cref="SecondaryVisualAcquiredOccurred"/> /
    /// <see cref="SecondaryNearOccurred"/>) rather than starting - or retyping - a second Episode. A
    /// Creature already sitting 3 m behind the Player (CreatureNear fires, Origin stays SpatialEncounter)
    /// that the Player only notices a few seconds later (a Secondary VisualAcquired) is exactly the
    /// case this is for: "the Creature was already close; the Player found it later."
    ///
    /// Awareness timing (<see cref="CreatureAwareAtEvent"/> / <see cref="CreatureBecameAwareDuringEpisode"/>
    /// / <see cref="CreatureAwarenessLatency"/> / <see cref="CreatureObservableDuration"/>) is computed
    /// purely from <c>CreaturePerception.IsPlayerPerceived</c> sampled each frame - the SAME algorithm
    /// CreaturePerception already uses for everything else that depends on player-perceived-or-not; no
    /// new Creature-side raycast/sense is added here, and CreaturePerception's own perception algorithm
    /// is not modified at all.
    ///
    /// A later PlayerDiscovery becoming a CreatureInitiated retroactively is explicitly NOT done - see
    /// <see cref="Origin"/>: whichever trigger started the Episode fixes its Origin for the Episode's
    /// whole lifetime, even if the Creature notices the Player midway through.
    ///
    /// Before/After window structure, the Before rolling buffer, and the World-Reaction summary
    /// (View/Move/Gaze/Approach-Retreat peaks, High/Low occurrence) are UNCHANGED from 0.3 - see the
    /// per-member docs below for exactly what each still means.
    /// </summary>
    public class ReactionEpisodeRecorder : MonoBehaviour
    {
        public enum EpisodeState { Idle, CollectingAfter, Complete }

        /// <summary>Which sensor edge started the current/last Episode.</summary>
        public enum ReactionEventType { CreatureNear, CreatureVisualAcquired }

        /// <summary>
        /// Who/what set the Episode in motion. PlayerDiscovery = the Player's camera found the
        /// Creature (CreatureVisualAcquired). SpatialEncounter = pure proximity (CreatureNear) - says
        /// nothing about whether the Player noticed. CreatureInitiated is reserved for a future Probe
        /// Experiment step and is never produced in 0.4.
        /// </summary>
        public enum ReactionEpisodeOrigin { PlayerDiscovery, SpatialEncounter, CreatureInitiated }

        [Header("References (read-only consumers)")]
        [Tooltip("Empty = auto-found in the scene.")]
        [SerializeField] private PlayerObservation playerObservation;
        [Tooltip("Empty = auto-found in the scene.")]
        [SerializeField] private CreatureEncounterObservation creatureEncounter;
        [Tooltip("Source of truth for Creature awareness. Only IsPlayerPerceived is ever read - CreaturePerception's own algorithm is never touched. Empty = auto-found in the scene.")]
        [SerializeField] private CreaturePerception creaturePerception;

        [Header("Event: CreatureNear (hysteresis, metres) - Origin: SpatialEncounter")]
        [Tooltip("DistanceToCreature at or below which the CreatureNear event fires, while armed. Must stay below reactionExitRange and at/below CreatureEncounterObservation's own observationRange (10 m in this scene) - a distance the encounter sensor cannot even see is meaningless here.")]
        [SerializeField] private float reactionEnterRange = 5f;
        [Tooltip("DistanceToCreature at or above which CreatureNear re-arms after a trigger (or after firing as a Secondary event). Kept above reactionEnterRange so distance jitter at the boundary cannot fire repeatedly.")]
        [SerializeField] private float reactionExitRange = 7f;

        [Header("Windows (seconds)")]
        [Tooltip("How much sensor history is kept in the always-on Before rolling buffer. Summarized (not cleared) the instant an Event fires.")]
        [SerializeField] private float beforeWindowSeconds = 2f;
        [Tooltip("How long after an Event to keep collecting the After summary before the Episode completes.")]
        [SerializeField] private float afterWindowSeconds = 3f;

        // --- always-on Before rolling buffer (only the fields the Before summary needs) --------------
        private struct BeforeSample
        {
            public float time;
            public float dt;
            public float moveSpeed;
            public float viewDeviation;
            public float shortViewSpeed;
            public float towardSpeed;
            public bool looking;
        }

        private readonly Queue<BeforeSample> _beforeBuffer = new Queue<BeforeSample>();
        private float _beforeSumDt, _beforeSumMove, _beforeSumViewDev, _beforeSumShortView, _beforeSumToward, _beforeSumLookingDt;

        // --- After-window-only accumulators that are not themselves public values ---------------------
        private float _afterSumDt, _afterSumMove, _afterSumViewDev;
        private float _afterCurrentGazeRun;
        private int _highSerialAtEvent, _lowSerialAtEvent;
        private int _lastSeenVisualSerial;

        private bool _nearArmed = true;

        /// <summary>Idle (armed, waiting) / CollectingAfter (an Episode's After window is running) / Complete (last Episode's numbers, frozen, until the next Event).</summary>
        public EpisodeState State { get; private set; } = EpisodeState.Idle;

        /// <summary>Which trigger kind started the current/last Episode.</summary>
        public ReactionEventType EventType { get; private set; }

        /// <summary>Who/what the current/last Episode's Origin is attributed to - fixed for the Episode's whole lifetime, see class summary.</summary>
        public ReactionEpisodeOrigin Origin { get; private set; }

        /// <summary><see cref="Time.time"/> the current/last event fired.</summary>
        public float EventTime { get; private set; }

        /// <summary><see cref="CreatureEncounterObservation.DistanceToCreature"/> at the moment of the event.</summary>
        public float EventDistance { get; private set; }

        /// <summary>The configured <see cref="beforeWindowSeconds"/> - read-only, for HUD labels.</summary>
        public float BeforeWindowSeconds => beforeWindowSeconds;

        /// <summary>The configured <see cref="afterWindowSeconds"/> - read-only, for HUD labels.</summary>
        public float AfterWindowSeconds => afterWindowSeconds;

        // --- Secondary events: the OTHER trigger kind firing during CollectingAfter, recorded but not
        //     starting a second Episode - see class summary. -------------------------------------------
        public bool SecondaryVisualAcquiredOccurred { get; private set; }
        public float SecondaryVisualAcquireTimeFromEvent { get; private set; }
        public bool SecondaryNearOccurred { get; private set; }
        public float SecondaryNearTimeFromEvent { get; private set; }

        // ============================== WORLD REACTION (unchanged from 0.3, always recorded) =========

        // --- Before summary (frozen at the Event) ----------------------------------------------------
        public float BeforeAverageMoveSpeed { get; private set; }
        public float BeforeAverageViewDeviation { get; private set; }
        public float BeforeAverageShortViewSpeed { get; private set; }
        public float BeforeAverageTowardSpeed { get; private set; }
        /// <summary>Fraction (0..1) of the Before window spent with <see cref="CreatureEncounterObservation.IsLookingAtCreature"/> true. 0 is normal if the Creature had not been in gaze range yet.</summary>
        public float BeforeWasLookingRatio { get; private set; }

        // --- After summary: live during CollectingAfter, frozen at Complete ---------------------------
        public float AverageMoveSpeedAfter { get; private set; }
        public float MinMoveSpeedAfter { get; private set; }
        public float MaxMoveSpeedAfter { get; private set; }

        public float AverageViewDeviationAfter { get; private set; }
        public float PeakViewDeviation { get; private set; }
        public float PeakShortViewAngularSpeed { get; private set; }
        /// <summary>True if a NEW High View Episode (per <see cref="PlayerObservation.HighViewEpisodeSerial"/>) started at any point during the After window.</summary>
        public bool HighViewOccurred { get; private set; }
        /// <summary>True if a NEW Low View Episode (per <see cref="PlayerObservation.LowViewEpisodeSerial"/>) was confirmed at any point during the After window.</summary>
        public bool LowViewOccurred { get; private set; }

        /// <summary>True once a gaze-latency value exists for this Episode (either the Player was already looking at the Event, or looked at some point during the After window).</summary>
        public bool HasGazeLatency { get; private set; }
        /// <summary>Seconds from EventTime to the first look at the Creature. 0 if the Player was already looking the instant the Event fired. Meaningless (do not read) while <see cref="HasGazeLatency"/> is false.</summary>
        public float GazeLatency { get; private set; }
        /// <summary>True once any look at the Creature has been observed at/after the Event (including "already looking" at the Event itself).</summary>
        public bool GazeStartedAfterEvent { get; private set; }
        /// <summary>Total seconds looking at the Creature WITHIN the After window only - tracked independently of CreatureEncounterObservation's whole-exposure TotalGazeTime, see class summary.</summary>
        public float AfterTotalGazeTime { get; private set; }
        /// <summary>Longest single continuous gaze WITHIN the After window only.</summary>
        public float AfterLongestGaze { get; private set; }

        /// <summary>Largest positive TowardCreatureSpeed observed during the After window (0 if the Player never closed the gap).</summary>
        public float PeakApproachSpeed { get; private set; }
        /// <summary>Most negative TowardCreatureSpeed observed during the After window (0 if the Player never opened the gap). Both this and PeakApproachSpeed can be non-zero in the same Episode - a mixed reaction is not collapsed to one label.</summary>
        public float PeakRetreatSpeed { get; private set; }

        // --- Deltas: a quick before/after read, NOT a replacement for the peak values above ----------
        public float MoveAverageDelta => AverageMoveSpeedAfter - BeforeAverageMoveSpeed;
        public float ViewDeviationDelta => AverageViewDeviationAfter - BeforeAverageViewDeviation;

        // ============================== CREATURE PERCEPTION / AWARENESS ===============================

        /// <summary>Was CreaturePerception.IsPlayerPerceived already true the instant the Event fired?</summary>
        public bool CreatureAwareAtEvent { get; private set; }

        /// <summary>True only if the Creature was NOT aware at the Event but became aware (IsPlayerPerceived's false-&gt;true edge) at some point before the After window ended.</summary>
        public bool CreatureBecameAwareDuringEpisode { get; private set; }

        /// <summary>True once the Creature has been aware at some point in this Episode (at the Event, or during the After window) - i.e. there is a meaningful CreatureAwarenessLatency value.</summary>
        public bool HasCreatureAwarenessLatency { get; private set; }

        /// <summary>Seconds from EventTime to the Creature first perceiving the Player. 0 if already aware at the Event. Meaningless while <see cref="HasCreatureAwarenessLatency"/> is false (Creature never perceived the Player during the whole Episode).</summary>
        public float CreatureAwarenessLatency { get; private set; }

        /// <summary>Total seconds within the After window during which CreaturePerception.IsPlayerPerceived was true - how much of the 3 s reaction window the Creature could actually have been watching for.</summary>
        public float CreatureObservableDuration { get; private set; }

        /// <summary>Convenience: true once CreatureObservableDuration &gt; 0 - i.e. there is any Creature-observed data at all for this Episode. False means the CREATURE OBSERVED side is empty ("none").</summary>
        public bool HasCreatureObservedData => CreatureObservableDuration > 0f;

        // ============================== CREATURE OBSERVED (world-space-observable subset only, and
        //     ONLY accumulated while CreaturePerception.IsPlayerPerceived was true that frame - see
        //     class summary. Deliberately excludes camera/view-input-derived values (ViewDeviationRatio,
        //     High/Low View) - a Creature has no way to see the Player's mouse input. ===================

        /// <summary>Seconds, within the After window, that the Player was looking at the Creature WHILE the Creature was aware of the Player. Always &lt;= AfterTotalGazeTime.</summary>
        public float ObservedLookingDuration { get; private set; }

        /// <summary>Largest positive TowardCreatureSpeed seen while the Creature was aware. 0 if none (including if the Creature was never aware).</summary>
        public float ObservedPeakApproachSpeed { get; private set; }

        /// <summary>Most negative TowardCreatureSpeed seen while the Creature was aware. 0 if none.</summary>
        public float ObservedPeakRetreatSpeed { get; private set; }

        /// <summary>Slowest Player move speed seen while the Creature was aware. 0 if the Creature was never aware this Episode (no samples).</summary>
        public float ObservedMinMoveSpeed { get; private set; }

        /// <summary>Fastest Player move speed seen while the Creature was aware. 0 if the Creature was never aware this Episode.</summary>
        public float ObservedMaxMoveSpeed { get; private set; }

        private void Awake()
        {
            if (playerObservation == null)
                playerObservation = FindFirstObjectByType<PlayerObservation>();
            if (creatureEncounter == null)
                creatureEncounter = FindFirstObjectByType<CreatureEncounterObservation>();
            if (creaturePerception == null)
                creaturePerception = FindFirstObjectByType<CreaturePerception>();
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            float now = Time.time;

            UpdateBeforeBuffer(dt, now);

            float distance = creatureEncounter != null ? creatureEncounter.DistanceToCreature : float.PositiveInfinity;
            int visualSerial = creatureEncounter != null ? creatureEncounter.VisualAcquiredSerial : _lastSeenVisualSerial;
            bool visualEdge = visualSerial != _lastSeenVisualSerial;
            _lastSeenVisualSerial = visualSerial;

            bool nearCondition = distance <= reactionEnterRange;

            // Visual acquisition: no range gate, no armed flag needed here - CreatureEncounterObservation
            // already debounces VisualAcquiredSerial with its own min-duration/re-arm hysteresis, so any
            // edge here is already a genuine, sustained, non-repeating acquisition.
            if (visualEdge)
            {
                if (State == EpisodeState.Idle || State == EpisodeState.Complete)
                {
                    TriggerEvent(now, distance, ReactionEventType.CreatureVisualAcquired, ReactionEpisodeOrigin.PlayerDiscovery);
                }
                else if (State == EpisodeState.CollectingAfter && !SecondaryVisualAcquiredOccurred)
                {
                    SecondaryVisualAcquiredOccurred = true;
                    SecondaryVisualAcquireTimeFromEvent = now - EventTime;
                }
            }

            // CreatureNear: same enter/exit hysteresis as 0.3, now also usable as a Secondary event.
            if (_nearArmed && nearCondition)
            {
                if (State == EpisodeState.Idle || State == EpisodeState.Complete)
                {
                    TriggerEvent(now, distance, ReactionEventType.CreatureNear, ReactionEpisodeOrigin.SpatialEncounter);
                }
                else if (State == EpisodeState.CollectingAfter && !SecondaryNearOccurred)
                {
                    SecondaryNearOccurred = true;
                    SecondaryNearTimeFromEvent = now - EventTime;
                }
                _nearArmed = false;
            }
            if (!_nearArmed && distance >= reactionExitRange)
                _nearArmed = true;

            if (State == EpisodeState.CollectingAfter)
                TickCollectingAfter(dt, now);
        }

        // Always running, regardless of Episode state - the Recorder does not know when the next Event
        // will happen. Only the few fields the Before summary actually needs (see BeforeSample).
        private void UpdateBeforeBuffer(float dt, float now)
        {
            if (dt <= 0f)
                return;

            float move = playerObservation != null ? playerObservation.CurrentMoveSpeed : 0f;
            float viewDev = playerObservation != null ? playerObservation.ViewDeviationRatio : 0f;
            float shortView = playerObservation != null ? playerObservation.ShortViewAngularSpeed : 0f;
            float toward = creatureEncounter != null ? creatureEncounter.TowardCreatureSpeed : 0f;
            bool looking = creatureEncounter != null && creatureEncounter.IsLookingAtCreature;

            _beforeBuffer.Enqueue(new BeforeSample
            {
                time = now,
                dt = dt,
                moveSpeed = move,
                viewDeviation = viewDev,
                shortViewSpeed = shortView,
                towardSpeed = toward,
                looking = looking,
            });
            _beforeSumDt += dt;
            _beforeSumMove += move * dt;
            _beforeSumViewDev += viewDev * dt;
            _beforeSumShortView += shortView * dt;
            _beforeSumToward += toward * dt;
            if (looking)
                _beforeSumLookingDt += dt;

            while (_beforeBuffer.Count > 0 && now - _beforeBuffer.Peek().time > beforeWindowSeconds)
            {
                BeforeSample old = _beforeBuffer.Dequeue();
                _beforeSumDt -= old.dt;
                _beforeSumMove -= old.moveSpeed * old.dt;
                _beforeSumViewDev -= old.viewDeviation * old.dt;
                _beforeSumShortView -= old.shortViewSpeed * old.dt;
                _beforeSumToward -= old.towardSpeed * old.dt;
                if (old.looking)
                    _beforeSumLookingDt -= old.dt;
            }
        }

        // A trigger edge (either kind): freeze the Before summary from the rolling buffer's CURRENT
        // contents, snapshot everything the After window needs a reference point for (including
        // Creature awareness), and start collecting.
        private void TriggerEvent(float now, float distance, ReactionEventType eventType, ReactionEpisodeOrigin origin)
        {
            EventTime = now;
            EventDistance = distance;
            EventType = eventType;
            Origin = origin;

            SecondaryVisualAcquiredOccurred = false;
            SecondaryVisualAcquireTimeFromEvent = 0f;
            SecondaryNearOccurred = false;
            SecondaryNearTimeFromEvent = 0f;

            BeforeAverageMoveSpeed = _beforeSumDt > 0f ? _beforeSumMove / _beforeSumDt : 0f;
            BeforeAverageViewDeviation = _beforeSumDt > 0f ? _beforeSumViewDev / _beforeSumDt : 0f;
            BeforeAverageShortViewSpeed = _beforeSumDt > 0f ? _beforeSumShortView / _beforeSumDt : 0f;
            BeforeAverageTowardSpeed = _beforeSumDt > 0f ? _beforeSumToward / _beforeSumDt : 0f;
            BeforeWasLookingRatio = _beforeSumDt > 0f ? Mathf.Clamp01(_beforeSumLookingDt / _beforeSumDt) : 0f;

            _afterSumDt = 0f; _afterSumMove = 0f; _afterSumViewDev = 0f;
            _afterCurrentGazeRun = 0f;

            AverageMoveSpeedAfter = 0f;
            MinMoveSpeedAfter = float.PositiveInfinity; // corrected to a real value by TickCollectingAfter this same frame
            MaxMoveSpeedAfter = 0f;
            AverageViewDeviationAfter = 0f;
            PeakViewDeviation = 0f;
            PeakShortViewAngularSpeed = 0f;
            HighViewOccurred = false;
            LowViewOccurred = false;
            AfterTotalGazeTime = 0f;
            AfterLongestGaze = 0f;
            PeakApproachSpeed = 0f;
            PeakRetreatSpeed = 0f;

            _highSerialAtEvent = playerObservation != null ? playerObservation.HighViewEpisodeSerial : 0;
            _lowSerialAtEvent = playerObservation != null ? playerObservation.LowViewEpisodeSerial : 0;

            // "Already looking at the moment of the Event" -> latency 0, per spec.
            bool alreadyLooking = creatureEncounter != null && creatureEncounter.IsLookingAtCreature;
            HasGazeLatency = alreadyLooking;
            GazeLatency = 0f;
            GazeStartedAfterEvent = alreadyLooking;

            // Creature awareness snapshot - CreaturePerception.IsPlayerPerceived, read ONLY, nothing
            // about what the Creature is doing about it.
            bool awareAtEvent = creaturePerception != null && creaturePerception.IsPlayerPerceived;
            CreatureAwareAtEvent = awareAtEvent;
            CreatureBecameAwareDuringEpisode = false;
            HasCreatureAwarenessLatency = awareAtEvent;
            CreatureAwarenessLatency = 0f;
            CreatureObservableDuration = 0f;

            ObservedLookingDuration = 0f;
            ObservedPeakApproachSpeed = 0f;
            ObservedPeakRetreatSpeed = 0f;
            ObservedMinMoveSpeed = float.PositiveInfinity; // corrected to 0 at Complete if the Creature was never aware
            ObservedMaxMoveSpeed = 0f;

            State = EpisodeState.CollectingAfter;
        }

        // Runs every frame of the After window. Writes straight onto the public After*/Observed*
        // properties - see class summary for why (no separate finalize copy). The WORLD REACTION block
        // always runs; the CREATURE OBSERVED block runs ONLY inside `if (awareNow)` - this is the
        // structural guarantee that pre-awareness reaction never becomes "Creature knowledge".
        private void TickCollectingAfter(float dt, float now)
        {
            float elapsed = now - EventTime;

            // --- WORLD REACTION: always recorded, exactly as in 0.3 ------------------------------
            if (playerObservation != null)
            {
                float move = playerObservation.CurrentMoveSpeed;
                float viewDev = playerObservation.ViewDeviationRatio;
                float shortView = playerObservation.ShortViewAngularSpeed;

                _afterSumDt += dt;
                _afterSumMove += move * dt;
                _afterSumViewDev += viewDev * dt;
                AverageMoveSpeedAfter = _afterSumDt > 0f ? _afterSumMove / _afterSumDt : 0f;
                AverageViewDeviationAfter = _afterSumDt > 0f ? _afterSumViewDev / _afterSumDt : 0f;

                if (move < MinMoveSpeedAfter) MinMoveSpeedAfter = move;
                if (move > MaxMoveSpeedAfter) MaxMoveSpeedAfter = move;
                if (viewDev > PeakViewDeviation) PeakViewDeviation = viewDev;
                if (shortView > PeakShortViewAngularSpeed) PeakShortViewAngularSpeed = shortView;

                if (playerObservation.HighViewEpisodeSerial > _highSerialAtEvent) HighViewOccurred = true;
                if (playerObservation.LowViewEpisodeSerial > _lowSerialAtEvent) LowViewOccurred = true;
            }

            bool creatureLooking = creatureEncounter != null && creatureEncounter.IsLookingAtCreature;

            if (creatureEncounter != null)
            {
                float toward = creatureEncounter.TowardCreatureSpeed;
                if (toward > PeakApproachSpeed) PeakApproachSpeed = toward;
                if (toward < PeakRetreatSpeed) PeakRetreatSpeed = toward;

                if (!HasGazeLatency && creatureLooking)
                {
                    HasGazeLatency = true;
                    GazeLatency = elapsed;
                    GazeStartedAfterEvent = true;
                }

                if (creatureLooking)
                {
                    AfterTotalGazeTime += dt;
                    _afterCurrentGazeRun += dt;
                    if (_afterCurrentGazeRun > AfterLongestGaze)
                        AfterLongestGaze = _afterCurrentGazeRun;
                }
                else
                {
                    _afterCurrentGazeRun = 0f;
                }
            }

            // --- CREATURE AWARENESS + CREATURE OBSERVED: gated entirely on IsPlayerPerceived ------
            bool awareNow = creaturePerception != null && creaturePerception.IsPlayerPerceived;

            if (!HasCreatureAwarenessLatency && awareNow)
            {
                HasCreatureAwarenessLatency = true;
                CreatureAwarenessLatency = elapsed;
                if (!CreatureAwareAtEvent)
                    CreatureBecameAwareDuringEpisode = true;
            }

            if (awareNow)
            {
                CreatureObservableDuration += dt;

                if (creatureEncounter != null)
                {
                    float toward = creatureEncounter.TowardCreatureSpeed;
                    if (toward > ObservedPeakApproachSpeed) ObservedPeakApproachSpeed = toward;
                    if (toward < ObservedPeakRetreatSpeed) ObservedPeakRetreatSpeed = toward;
                }
                if (playerObservation != null)
                {
                    float move = playerObservation.CurrentMoveSpeed;
                    if (move < ObservedMinMoveSpeed) ObservedMinMoveSpeed = move;
                    if (move > ObservedMaxMoveSpeed) ObservedMaxMoveSpeed = move;
                }
                // "Player is looking at the Creature" counts as Creature-observable ONLY while the
                // Creature is itself aware of the Player (see class summary, and instruction 21).
                if (creatureLooking)
                    ObservedLookingDuration += dt;
            }

            if (elapsed >= afterWindowSeconds)
            {
                if (float.IsPositiveInfinity(ObservedMinMoveSpeed))
                    ObservedMinMoveSpeed = 0f; // never aware this Episode -> no samples, not "infinitely slow"
                State = EpisodeState.Complete;
            }
        }

        private void OnValidate()
        {
            reactionEnterRange = Mathf.Max(0.1f, reactionEnterRange);
            reactionExitRange = Mathf.Max(reactionEnterRange + 0.01f, reactionExitRange);
            beforeWindowSeconds = Mathf.Max(0.1f, beforeWindowSeconds);
            afterWindowSeconds = Mathf.Max(0.1f, afterWindowSeconds);
        }
    }
}
