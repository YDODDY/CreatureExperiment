using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// The creature's low-level locomotion. It owns the creature root's XZ position and each frame
    /// runs exactly one of two movement verbs.
    ///
    /// Retreat (0.1): while the player is perceived and closer than <see cref="personalSpace"/>
    /// (flat XZ, root to root), translate straight away from the player at <see cref="retreatSpeed"/>
    /// until the gap re-opens to <see cref="comfortDistance"/>. Hysteresis on <see cref="_retreating"/>
    /// so it does not stutter at the boundary.
    ///
    /// Approach (0.1): when not retreating and still farther than <see cref="approachStopDistance"/>,
    /// walk toward <see cref="_activeTarget"/> - the interactable <see cref="CreaturePerception"/> is
    /// attending to right now (nearest one; null when it is looking at the player or nothing). Pure
    /// per-frame pursuit of its current position: no prediction, no path. Attention and this "walk
    /// to / inspect" target stay separate concepts - this just borrows the attention seam to pick
    /// what to investigate, and follows it if attention later moves to a different object.
    ///
    /// Inspect (0.1): once inside <see cref="approachStopDistance"/>, hold still and watch for a
    /// random <see cref="inspectDwellMin"/>..<see cref="inspectDwellMax"/> seconds, then slide
    /// around the target (staying on the ring of that radius) by a random angular hop to a new
    /// viewing spot, and dwell again. Repeats. Not a continuous orbit - a "watch / move / watch"
    /// rhythm. Gaze / head / pupil keep tracking it throughout (CreaturePerception, untouched).
    ///
    /// Lost-target persistence (0.1): if an object the creature was actually perceiving drops out of
    /// perception (range / FOV / LOS) for a moment - e.g. a thrown object rolling quickly across the
    /// view - the creature does not blank instantly. Every frame it genuinely perceives a free
    /// object it snapshots that object's position into <see cref="_lastKnownTargetPos"/>; when it
    /// then loses it, it walks to that snapshot for up to <see cref="lostTargetGrace"/> seconds to
    /// check. A Player-thrown object still in flight counts as "perceived" here (it keeps the
    /// snapshot fresh) even though it is never an <see cref="_activeTarget"/> - so watching a fast
    /// throw / roll and then losing it can start a coast. This is deliberately NOT "still seeing it":
    /// <see cref="_activeTarget"/> / <see cref="InspectTarget"/> / <see cref="IsInspecting"/> all
    /// read null/false during the coast, so <see cref="CreaturePickup"/>'s dwell timer and every
    /// other perception-dependent reader stay paused - they only resume if <see cref="CreaturePerception"/>
    /// re-attends to the object, which flips this straight back to live Approach/Inspect. Reaching
    /// the last-known spot, or the grace expiring, ends the coast and the creature falls back to
    /// idle / Wander. During the coast <see cref="CreaturePerception"/> reads <see cref="LostGazePoint"/>
    /// and keeps the head/gaze aimed at that spot, so the persistence is actually visible - still
    /// without treating the object as perceived. Applies to every <see cref="Interactable"/> equally
    /// - there is no per-object logic, no KnownObject registry, no search state machine, and
    /// CreatureMemory is not touched.
    ///
    /// Priority is just Retreat &gt; (Approach | Inspect); one movement per frame, never blended.
    /// Strictly lower-priority siblings (<see cref="CreaturePlayerObserve"/>, then
    /// <see cref="CreatureWander"/>) may translate the root only on frames <see cref="IsDrivingRoot"/>
    /// is false; they take no part in the arbitration here and this component neither reads nor knows
    /// about them.
    /// A temporary arbitration for the prototype, not an AI. Deliberately tiny: no body rotation, no
    /// acceleration, no state machine, no vector blending. Object Approach and Probe approach hand
    /// their per-frame "step toward the target" to <see cref="CreatureNavLocomotion"/> when it is
    /// present (Navigation 0.1) so they route around walls via the sandbox doorways instead of
    /// straight-lining into them; the target point and every decision are still chosen here, Nav only
    /// changes HOW the step is taken, and with no baked NavMesh it falls back to the old straight
    /// line. Retreat / Inspect keep their straight-line writes.
    /// <see cref="CreaturePerception"/> drives gaze / head / pupil independently and is not touched.
    /// </summary>
    [RequireComponent(typeof(CreaturePerception))]
    public class CreatureMovement : MonoBehaviour
    {
        [Header("Distances (metres, flat XZ)")]
        [Tooltip("Player at or inside this distance makes the creature start backing away.")]
        [SerializeField] private float personalSpace = 2f;
        [Tooltip("Creature stops once the gap has opened back up to this. Kept above personalSpace.")]
        [SerializeField] private float comfortDistance = 3.25f;

        [Header("Movement")]
        [Tooltip("Constant retreat speed. No ease in / ease out this pass.")]
        [SerializeField] private float retreatSpeed = 1.2f;

        [Header("Approach")]
        [Tooltip("Stop moving once flat XZ distance (root to root) is within this.")]
        [SerializeField] private float approachStopDistance = 1.5f;
        [Tooltip("Constant approach speed. Deliberately separate from retreatSpeed.")]
        [SerializeField] private float approachSpeed = 1.2f;

        [Header("Inspect")]
        [Tooltip("Min seconds to hold still watching the target before moving to a new spot.")]
        [SerializeField] private float inspectDwellMin = 4f;
        [Tooltip("Max seconds to hold still watching the target before moving to a new spot.")]
        [SerializeField] private float inspectDwellMax = 10f;
        [Tooltip("Speed while sliding around the target to the next spot, in metres per second.")]
        [SerializeField] private float inspectMoveSpeed = 0.9f;
        [Tooltip("Smallest angular hop to the next viewing spot, in degrees.")]
        [SerializeField] private float inspectStepMin = 40f;
        [Tooltip("Largest angular hop to the next viewing spot, in degrees.")]
        [SerializeField] private float inspectStepMax = 120f;

        [Header("Lost-target persistence (object Approach/Inspect only)")]
        [Tooltip("When an attended object drops out of perception (range / FOV / LOS), the creature " +
                 "spends up to this long walking to the LAST position it actually perceived the object " +
                 "at, then gives up. It is NOT treated as still seeing the object - pickup / inspect " +
                 "progress only resumes if perception re-acquires it. 0 = off (instant stop, the old behaviour).")]
        [SerializeField] private float lostTargetGrace = 2f;
        [Tooltip("Flat XZ distance at which the lost-target coast counts as having REACHED the last-known " +
                 "position (and gives up). Separate from Approach's approachStopDistance (1.5 m) on purpose - " +
                 "kept small so the creature actually walks up to the spot to check instead of stopping short. " +
                 "Only LostCoastStep() uses this.")]
        [SerializeField] private float lostArrivalDistance = 0.4f;

        private CreaturePerception _perception;
        private CreatureDash _dash; // optional sibling; null just means "never dashing"
        private CreatureNavLocomotion _nav; // optional sibling; null (or no baked NavMesh) => straight-line, exactly as before

        // Probe seam: while non-null, Approach walks toward THIS transform instead of the attention
        // target, and on arrival the creature just holds (no inspect ring-orbit). Set/cleared by
        // CreatureProbe only. Retreat still preempts it - that is deliberate, it keeps the probe wary.
        private Transform _probeApproach;

        private bool _retreating;
        private bool _approaching;       // ApproachStep() ran this frame (straight-line walk toward _activeTarget, not yet Inspecting)
        private bool _inspecting;        // currently in an inspect session at _activeTarget
        private bool _inspectDwelling;   // true = holding still watching, false = sliding to next spot
        private float _inspectTimer;     // seconds left in the current dwell
        private float _inspectTargetAngle; // bearing around the target we hold / move toward, degrees
        private Interactable _activeTarget; // what we walk to / inspect this frame; mirrored from CreaturePerception.AttendedInteractable

        // Lost-target persistence. _lost is true only while coasting to a lost object's last-known
        // position; it is NOT "the creature can see the object". _activeTarget is null throughout.
        private bool _lost;
        private float _lostTimer;            // seconds spent in the current lost coast
        private Vector3 _lastKnownTargetPos; // last position _lostFrom was ACTUALLY perceived at
        private Interactable _lostFrom;      // the object we lost - kept only to re-check it stays free, and for gizmos
        private bool _lostFromWasInFlight;   // latched: _lostFrom was seen in flight at least once this episode -> this is a moving-object pursuit, not a static-object LOS blip

        /// <summary>True while an inspect session is active at <see cref="_activeTarget"/> (dwelling or sliding).</summary>
        public bool IsInspecting => _inspecting;

        /// <summary>True only on the "holding still and watching" beats of an inspect session, not while sliding to a new spot.</summary>
        public bool IsInspectDwelling => _inspecting && _inspectDwelling;

        /// <summary>The interactable this component walks toward and inspects this frame, or null. Read-only seam for sibling components (e.g. the physical probe).</summary>
        public Interactable InspectTarget => _activeTarget;

        /// <summary>
        /// While the creature is coasting to a lost object's last-known position (<see cref="_lost"/>),
        /// this is that world position; null at every other time. <see cref="CreaturePerception"/>
        /// reads it to keep the head/gaze pointed there for the duration of the coast - a visible
        /// "still checking" beat. It is NOT "the object is perceived": attention selection, Approach,
        /// Inspect, Pickup and the probes all stay off exactly as when perception is simply empty.
        /// </summary>
        public Vector3? LostGazePoint => _lost ? _lastKnownTargetPos : (Vector3?)null;

        /// <summary>
        /// The Interactable this component is currently lost-coasting toward (<see cref="_lost"/>), or
        /// null. <see cref="CreaturePerception"/>'s Object Attention Budget reads it so a brief lost
        /// gap does NOT reset the attention episode timer for that object. Not "perceived" - it is the
        /// object whose last-known position the creature is walking to.
        /// </summary>
        public Interactable LostTargetInteractable => _lost ? _lostFrom : null;

        /// <summary>
        /// True only on frames the straight-line Object <see cref="ApproachStep"/> actually ran - i.e.
        /// the creature is walking toward <see cref="InspectTarget"/> and has not yet arrived to Inspect.
        /// False during Retreat, a Probe approach, Inspect, or idle. The lower-priority
        /// <see cref="CreatureApproachDash"/> reads this (plus the target distance) to decide whether a
        /// long Approach earns one Dash. Valid after this component's <see cref="Update"/> for the frame.
        /// </summary>
        public bool IsApproaching => _approaching;

        /// <summary>
        /// True on any frame this component is driving the creature root - Retreat, a Probe approach,
        /// an Approach/Inspect toward an attention target, or a lost-target coast to a last-known
        /// position. False only when <see cref="Update"/> reached its "nothing to do" fall-through.
        /// The strictly lower-priority <see cref="CreatureWander"/> reads this to know it may move the
        /// creature, and yields the instant it flips back to true. Valid after this component's
        /// <see cref="Update"/> has run for the frame (CreatureWander runs after it by execution order).
        /// </summary>
        public bool IsDrivingRoot => _retreating || _probeApproach != null || _activeTarget != null || _lost;

        /// <summary>
        /// CreatureProbe only: while set, the creature walks toward <paramref name="target"/> (using the
        /// Approach verb) and then holds on arrival - no attention target, no inspect orbit. Retreat
        /// still wins over this. Pass null via <see cref="ClearProbeApproachTarget"/> to restore normal
        /// attention-driven movement.
        /// </summary>
        public void SetProbeApproachTarget(Transform target) => _probeApproach = target;

        /// <summary>Clears the probe approach override set by <see cref="SetProbeApproachTarget"/>.</summary>
        public void ClearProbeApproachTarget() => _probeApproach = null;

        private void Awake()
        {
            _perception = GetComponent<CreaturePerception>();
            _dash = GetComponent<CreatureDash>();
            _nav = GetComponent<CreatureNavLocomotion>();
        }

        /// <summary>
        /// True only while <see cref="CreatureNavLocomotion"/> is actively carrying a valid, still-
        /// shortening NavMesh path this frame. False in straight-line fallback (no agent / no bake)
        /// and when a path has stalled. <see cref="CreatureProbe"/>'s delivery give-ups read this so a
        /// legitimate detour around a wall is not mistaken for a stalled delivery; nothing else uses it.
        /// </summary>
        public bool IsNavProgressing => _nav != null && _nav.NavAvailable && _nav.IsProgressing;

        /// <summary>
        /// Distance to <paramref name="worldDest"/> measured along the NavMesh route when a complete
        /// path exists, else the flat straight-line distance (the fallback meaning). <see cref="CreatureProbe"/>
        /// uses this for its delivery release check so "success" cannot fire through a wall.
        /// </summary>
        public float NavDistanceToDestination(Vector3 worldDest)
        {
            if (_nav != null)
                return _nav.DistanceToDestination(worldDest);
            Vector3 flat = worldDest - transform.position;
            flat.y = 0f;
            return flat.magnitude;
        }

        // Retreat/Approach/Inspect each own their own base speed; Dash just substitutes its flat speed
        // for whichever one is currently in play, for its duration. A dash is started by the dev key OR
        // by CreatureApproachDash (only during a long Object Approach - see IsApproaching); either way
        // this stays a pure HOW-FAST swap, direction is the caller's, Dash never decides where to move.
        private float EffectiveSpeed(float baseSpeed)
        {
            return (_dash != null && _dash.IsDashing) ? _dash.DashSpeed : baseSpeed;
        }

        private void Update()
        {
            UpdateRetreatState();
            _approaching = false; // set true below only if ApproachStep() runs this frame

            // CreaturePerception picks the nearest thing it can see; this component turns that into a
            // walk-to / inspect target, a lost-target snapshot, or nothing.
            Interactable attended = _perception.AttendedInteractable;

            // What CreaturePerception is actually looking at right now, as long as nobody is carrying
            // it. This DELIBERATELY still includes a Player-thrown object that is in flight - the
            // creature can watch it and remember where it is, so losing sight of it later can begin a
            // lost coast. A held object (player's, or this creature's own carry) feeds nothing, same
            // as before - that also stops the ring-orbit feedback loop on a self-held object.
            Interactable perceivedFree = (attended != null && !attended.IsHeld) ? attended : null;

            // What the creature may actually walk up to / orbit / grab this frame. In-flight objects
            // stay excluded here exactly as before (pursuing a flying object broke HIT detection), so
            // Approach / Inspect / Pickup / CreatureApproachDash are unchanged. When this is null
            // every perception-dependent reader (InspectTarget -> CreaturePickup's dwell, the
            // physical probe, the approach dash) sees "nothing".
            _activeTarget = (perceivedFree != null && !perceivedFree.IsInFlight) ? perceivedFree : null;

            if (perceivedFree != null)
            {
                // Genuinely perceiving a free object (resting, rolling, or mid-flight - any of them).
                // Keep a fresh snapshot of where it is so a coast can start from the last place we
                // really saw it. For an in-flight object this snapshot is the ONLY thing it feeds.
                if (perceivedFree != _lostFrom)
                    _lostFromWasInFlight = false;      // new object -> fresh pursuit-kind latch
                if (perceivedFree.IsInFlight)
                    _lostFromWasInFlight = true;       // seen flying at least once this episode
                _lastKnownTargetPos = perceivedFree.transform.position;
                _lostFrom = perceivedFree;
                _lost = false;
                _lostTimer = 0f;
            }
            else if (_lostFrom != null && _perception.AttentionCooldownObject == _lostFrom)
            {
                // The Object Attention Budget just spent its episode on this object and put it on
                // cooldown - it was given up on PURPOSE, not merely lost from view. Do not coast
                // toward it, and drop any coast already heading there.
                if (_lost)
                    ClearLost();
                else
                    _lostFrom = null;
            }
            else
            {
                // Nothing perceived. If we were just watching a free object, coast to the last place
                // we saw it for up to lostTargetGrace seconds. The object may still be physically in
                // flight while we do this - fine, we are walking to a remembered point, not tracking
                // it. Only IsHeld ends it early (someone caught / picked it up).
                if (!_lost && _lostFrom != null && lostTargetGrace > 0f && !_lostFrom.IsHeld)
                {
                    _lost = true;
                    _lostTimer = 0f;
                }

                if (_lost)
                {
                    _lostTimer += Time.deltaTime;
                    if (_lostTimer >= lostTargetGrace || _lostFrom == null || _lostFrom.IsHeld)
                        ClearLost();
                }
            }

            // Retreat always wins and ends any inspect session.
            if (_retreating)
            {
                _inspecting = false;
                RetreatStep();
                return;
            }

            // Probe override: walk to the probe target and hold there. No inspect orbit, no attention
            // target this frame. Sits below Retreat on purpose.
            if (_probeApproach != null)
            {
                _inspecting = false;
                _activeTarget = null;
                ProbeApproachStep();
                return;
            }

            if (_activeTarget == null)
            {
                _inspecting = false;
                // No live target. If we just lost one, spend the grace walking to its last-known
                // position; otherwise this is the plain "nothing to do" fall-through.
                if (_lost)
                    LostCoastStep();
                return;
            }

            Vector3 flat = _activeTarget.transform.position - transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;

            if (distance > approachStopDistance + 0.1f)
            {
                // Still closing in -> straight-line Approach; no inspect session yet.
                _inspecting = false;
                _approaching = true;
                ApproachStep();
            }
            else
            {
                // Arrived -> watch / move / watch rhythm around the target.
                InspectStep();
            }
        }

        // Decides whether the player is crowding the creature. Sets _retreating only; never moves.
        // When the player is missing / not perceived it just clears the flag and returns, so the
        // rest of Update() (Approach) can still run.
        private void UpdateRetreatState()
        {
            if (!_perception.IsPlayerPerceived || _perception.Player == null)
            {
                _retreating = false;
                return;
            }

            Vector3 flat = transform.position - _perception.Player.position;
            flat.y = 0f;
            float distance = flat.magnitude;

            // Cross in below personalSpace, cross out above comfortDistance, hold in the band.
            if (!_retreating && distance <= personalSpace)
                _retreating = true;
            else if (_retreating && distance >= comfortDistance)
                _retreating = false;
        }

        // Straight away from the player, flat XZ translation, constant speed. Only reached while
        // _retreating, which implies the player exists.
        private void RetreatStep()
        {
            Vector3 away = transform.position - _perception.Player.position;
            away.y = 0f;
            float distance = away.magnitude;

            // Player essentially on top of the creature: pick any flat direction so it still moves off.
            away = distance > 0.0001f ? away / distance : -transform.right;

            transform.position += away * (EffectiveSpeed(retreatSpeed) * Time.deltaTime);
        }

        // Toward the probe target's current position, flat XZ, constant speed, stop inside
        // approachStopDistance. Same as ApproachStep but aimed at a plain Transform (the player). The
        // step is routed via CreatureNavLocomotion when present, straight otherwise. On arrival it
        // does nothing - the creature just holds.
        private void ProbeApproachStep()
        {
            if (_probeApproach == null)
                return;

            Vector3 targetPos = _probeApproach.position;
            Vector3 toward = targetPos - transform.position;
            toward.y = 0f;
            float distance = toward.magnitude;

            if (distance <= approachStopDistance)
                return;

            if (_nav != null)
                _nav.MoveToward(targetPos, EffectiveSpeed(approachSpeed));
            else
                transform.position += (toward / distance) * (EffectiveSpeed(approachSpeed) * Time.deltaTime);
        }

        // Toward _activeTarget's current position, flat XZ, constant speed, stop inside
        // approachStopDistance. The step is routed via CreatureNavLocomotion when present (so it goes
        // around walls to a still-visible object across a doorway), straight otherwise. Still pure
        // pursuit of the target's live position - no prediction, and this never picks the target.
        private void ApproachStep()
        {
            if (_activeTarget == null)
                return;

            Vector3 targetPos = _activeTarget.transform.position;
            Vector3 toward = targetPos - transform.position;
            toward.y = 0f;
            float distance = toward.magnitude;

            if (distance <= approachStopDistance)
                return;

            if (_nav != null)
                _nav.MoveToward(targetPos, EffectiveSpeed(approachSpeed));
            else
                transform.position += (toward / distance) * (EffectiveSpeed(approachSpeed) * Time.deltaTime);
        }

        // Lost-target coast: walk toward the LAST position the object was actually perceived at. This
        // is not tracking - the object is not perceived right now and re-acquisition happens on its
        // own back in Update() the moment CreaturePerception attends to it again. Routed via
        // CreatureNavLocomotion when present, straight otherwise, at the normal approach speed. Gives
        // up on arrival; the grace timer in Update() handles the "never arrived" case.
        private void LostCoastStep()
        {
            // Pursuit completion: the creature genuinely coasted after a MOVING object (it was seen
            // in flight at least once this episode), that object has since slowed enough to no longer
            // be in flight, and the coast has now brought the creature within interaction distance
            // (approachStopDistance - the same gap Approach hands off to Inspect at) of where the
            // object ACTUALLY is now. Treat the chase as succeeded: stop coasting and put THIS object
            // on the same short autonomous-attention cooldown a spent budget uses, so it is not
            // re-locked next frame. A still-in-flight object zipping past, a static object (never
            // flagged in-flight), or one the creature never caught up to does not qualify.
            if (_lostFrom != null && _lostFromWasInFlight
                && !_lostFrom.IsHeld && !_lostFrom.IsInFlight)
            {
                Vector3 toObj = _lostFrom.transform.position - transform.position;
                toObj.y = 0f;
                if (toObj.magnitude <= approachStopDistance)
                {
                    _perception.ReportPursuitComplete(_lostFrom);
                    ClearLost();
                    return;
                }
            }

            Vector3 toward = _lastKnownTargetPos - transform.position;
            toward.y = 0f;
            float distance = toward.magnitude;

            if (distance <= lostArrivalDistance) // NOT approachStopDistance - the coast walks right up to the spot
            {
                ClearLost(); // reached where we last saw it and still nothing - give up
                return;
            }

            if (_nav != null)
                _nav.MoveToward(_lastKnownTargetPos, EffectiveSpeed(approachSpeed));
            else
                transform.position += (toward / distance) * (EffectiveSpeed(approachSpeed) * Time.deltaTime);
        }

        // End the lost-target coast and fall back to the plain idle / Wander fall-through. A fresh
        // coast can only start after CreaturePerception attends to a free object again (which re-sets
        // _lostFrom), so this never immediately re-triggers.
        private void ClearLost()
        {
            _lost = false;
            _lostTimer = 0f;
            _lostFrom = null;
            _lostFromWasInFlight = false;
        }

        // Watch / move / watch around _activeTarget. Dwell still for a random time, then slide along
        // the ring (radius approachStopDistance) by a random angular hop to a new spot, then dwell
        // again. Position is always pinned to the ring, flat XZ; Y is never touched. The target's
        // live position is re-read each frame, so a moving object is followed at the same bearing.
        private void InspectStep()
        {
            Vector3 targetPos = _activeTarget.transform.position;
            Vector3 flat = transform.position - targetPos;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
                flat = Vector3.forward; // degenerate: creature on top of target, pick any bearing

            float currentAngle = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;

            // First frame of a fresh inspect session: settle where we arrived and start dwelling.
            if (!_inspecting)
            {
                _inspecting = true;
                _inspectDwelling = true;
                _inspectTimer = Random.Range(inspectDwellMin, inspectDwellMax);
                _inspectTargetAngle = currentAngle;
            }

            if (_inspectDwelling)
            {
                _inspectTimer -= Time.deltaTime;
                if (_inspectTimer <= 0f)
                {
                    // Pick the next viewing spot: a random angular hop, random direction.
                    float hop = Random.Range(inspectStepMin, inspectStepMax);
                    if (Random.value < 0.5f)
                        hop = -hop;
                    _inspectTargetAngle = currentAngle + hop;
                    _inspectDwelling = false;
                }
            }
            else
            {
                // Slide along the ring toward the chosen bearing.
                float degPerSec = (EffectiveSpeed(inspectMoveSpeed) / Mathf.Max(approachStopDistance, 0.01f)) * Mathf.Rad2Deg;
                currentAngle = Mathf.MoveTowardsAngle(currentAngle, _inspectTargetAngle, degPerSec * Time.deltaTime);

                if (Mathf.Abs(Mathf.DeltaAngle(currentAngle, _inspectTargetAngle)) < 0.5f)
                {
                    _inspectDwelling = true;
                    _inspectTimer = Random.Range(inspectDwellMin, inspectDwellMax);
                }
            }

            // Pin to the ring at currentAngle, distance approachStopDistance.
            float rad = currentAngle * Mathf.Deg2Rad;
            Vector3 pinned = targetPos + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * approachStopDistance;
            transform.position = new Vector3(pinned.x, transform.position.y, pinned.z);
        }

        private void OnValidate()
        {
            personalSpace = Mathf.Max(0f, personalSpace);
            comfortDistance = Mathf.Max(personalSpace + 0.1f, comfortDistance);
            retreatSpeed = Mathf.Max(0f, retreatSpeed);
            approachStopDistance = Mathf.Max(0f, approachStopDistance);
            approachSpeed = Mathf.Max(0f, approachSpeed);
            inspectDwellMin = Mathf.Max(0f, inspectDwellMin);
            inspectDwellMax = Mathf.Max(inspectDwellMin, inspectDwellMax);
            inspectMoveSpeed = Mathf.Max(0f, inspectMoveSpeed);
            inspectStepMin = Mathf.Max(0f, inspectStepMin);
            inspectStepMax = Mathf.Max(inspectStepMin, inspectStepMax);
            lostTargetGrace = Mathf.Max(0f, lostTargetGrace);
            lostArrivalDistance = Mathf.Max(0f, lostArrivalDistance);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.2f); // personal space
            DrawFlatCircle(personalSpace);
            Gizmos.color = new Color(0.2f, 0.8f, 1f); // comfort distance
            DrawFlatCircle(comfortDistance);

            Gizmos.color = new Color(0.4f, 1f, 0.4f); // approach stop distance
            DrawFlatCircle(approachStopDistance);
            if (_activeTarget != null)
                Gizmos.DrawLine(transform.position, _activeTarget.transform.position);
            else if (_lost) // lost-target coast: dashed intent line to the last-known position
            {
                Gizmos.color = new Color(1f, 0.55f, 0.2f);
                Gizmos.DrawLine(transform.position, _lastKnownTargetPos);
                Gizmos.DrawWireSphere(_lastKnownTargetPos, 0.2f);
            }
        }

        private void DrawFlatCircle(float radius)
        {
            const int segments = 48;
            Vector3 centre = transform.position;
            Vector3 prev = centre + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
