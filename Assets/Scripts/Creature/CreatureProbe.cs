using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Probe (0.1): the creature's first behaviour change. Once <see cref="CreaturePattern"/> has
    /// latched <see cref="CreaturePattern.RepeatedPlaceNearDetected"/> (the Player kept PLACING an
    /// object in the creature's NEAR band), the creature - after a short deliberation delay - picks up
    /// that same object and then carries it, waiting until the Player is nearby before delivering it
    /// and setting it down with its existing Place capability. One attempt per session.
    ///
    /// Player proximity is NOT a start gate any more: the creature commits to the object first and
    /// holds it. Proximity only decides whether an already-in-progress probe advances to delivery.
    ///
    /// This is NOT a gift, reward, or obedience: it is the creature trying a similar physical action
    /// back at the Player to see what happens. Nothing here decides meaning, and there is no generic
    /// Decision / Hypothesis / Intent / Interest / Search system - just this one hard-wired experiment.
    ///
    /// State flow: Armed -> Fetching -> Holding -> Delivering -> Finishing -> Terminal
    ///   Armed      : pattern detected, rolling / waiting out the deliberation delay
    ///   Fetching   : CreaturePickup is running the grab animation
    ///   Holding    : object in hand, standing by until the Player is perceived and within range.
    ///                No timeout - it will hold indefinitely. Gaze is forced to the Player here on
    ///                (honoured only while the Player is actually perceived).
    ///   Delivering : walking to the Player (CreatureMovement probe approach). Retreat still preempts.
    ///                Falls back to Holding if the Player leaves the deliver condition.
    ///   Finishing  : CreaturePickup is running the Place set-down
    ///   Terminal   : done for this session - Success or Aborted, no retry
    ///
    /// All the doing is reused: grab / carry / Place -> <see cref="CreaturePickup"/>; walking to the
    /// Player -> <see cref="CreatureMovement"/>; looking at the Player -> <see cref="CreaturePerception"/>.
    ///
    /// Object choice: the snapshot of <see cref="CreaturePattern.LastPlaceNearObject"/> taken when the
    /// probe starts. If that object is gone / held / out of reach the probe waits in Armed - it never
    /// falls back to some other nearby object.
    /// </summary>
    [RequireComponent(typeof(CreaturePattern))]
    [RequireComponent(typeof(CreaturePickup))]
    [RequireComponent(typeof(CreatureMovement))]
    [RequireComponent(typeof(CreaturePerception))]
    public class CreatureProbe : MonoBehaviour
    {
        public enum ProbeState { Armed, Fetching, Holding, Delivering, Finishing, Terminal }
        public enum ProbeOutcome { None, Success, Aborted }

        [Header("Start (deliberation)")]
        [Tooltip("After the pattern is detected, wait a random time in this range before grabbing. Keeps 'detected' from reading as instant obedience.")]
        [SerializeField] private float probeDelayMin = 2f;
        [SerializeField] private float probeDelayMax = 5f;

        [Header("Delivery")]
        [Tooltip("From Holding, the probe advances to Delivering once the player is perceived AND within this flat XZ distance. Keep this at or below CreaturePerception.perceptionRange so the player is reliably perceived when delivery starts.")]
        [SerializeField] private float probeDeliverRange = 4.5f;
        [Tooltip("Hysteresis: Delivering falls back to Holding only once the player is beyond probeDeliverRange + this (or stops being perceived). Avoids Holding/Delivering flicker at the boundary.")]
        [SerializeField] private float deliverRangeHysteresis = 0.75f;
        [Tooltip("While Delivering, once the creature is within this flat XZ distance of the player it sets the object down. Keep above CreatureMovement.personalSpace (ideally >= comfortDistance) or Retreat stops the creature before it can reach this.")]
        [SerializeField] private float probeReleaseDistance = 3.5f;
        [Tooltip("Seconds allowed to close the gap once Delivering has started (measured from each Delivering entry). On timeout the object is still set down via the normal Place path, but the outcome is Aborted, not Success.")]
        [SerializeField] private float probeDeliverTimeout = 15f;
        [Tooltip("Seconds to wait for the grab to actually start once fetching. If it does not, drop back to Armed (this is 'not yet', not a spent attempt).")]
        [SerializeField] private float fetchTimeout = 6f;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private ProbeState state = ProbeState.Armed;
        [SerializeField] private ProbeOutcome outcome = ProbeOutcome.None;
        [Tooltip("The object this probe locked onto - snapshot of CreaturePattern.LastPlaceNearObject at start.")]
        [SerializeField] private Interactable probeObject;
        [Tooltip("Seconds spent in the current ProbeState.")]
        [SerializeField] private float stateTimer;

        /// <summary>Current phase of the one-shot probe.</summary>
        public ProbeState State => state;

        /// <summary>None while not finished; Success if the object was delivered toward the Player; Aborted otherwise. Latched once Terminal.</summary>
        public ProbeOutcome Outcome => outcome;

        /// <summary>True while this probe is mid-run (past Armed, not yet Terminal). The sibling throw probe checks this so only one probe engages at a time; a finished (Terminal) probe does NOT block the other.</summary>
        public bool IsEngaged => state != ProbeState.Armed && state != ProbeState.Terminal;

        private CreaturePattern _pattern;
        private CreaturePickup _pickup;
        private CreatureMovement _movement;
        private CreaturePerception _perception;
        private CreatureThrowProbe _throwProbe; // optional sibling; null just means "no throw probe on this creature"

        private float _armDelay;        // this arming's rolled deliberation delay
        private bool _delayRolled;      // has _armDelay been rolled for the current detection?
        private ProbeOutcome _pendingOutcome; // decided just before RequestProbeRelease, committed when the Place finishes

        private void Awake()
        {
            _pattern = GetComponent<CreaturePattern>();
            _pickup = GetComponent<CreaturePickup>();
            _movement = GetComponent<CreatureMovement>();
            _perception = GetComponent<CreaturePerception>();
            _throwProbe = GetComponent<CreatureThrowProbe>();
        }

        private void Update()
        {
            switch (state)
            {
                case ProbeState.Armed:      TickArmed();      break;
                case ProbeState.Fetching:   TickFetching();   break;
                case ProbeState.Holding:    TickHolding();    break;
                case ProbeState.Delivering: TickDelivering(); break;
                case ProbeState.Finishing:  TickFinishing();  break;
                case ProbeState.Terminal:                     break; // one-shot: nothing more this session
            }
        }

        // Waiting for detection, then waiting out the deliberation delay. No player-proximity gate here
        // any more - the creature commits to the object as soon as it can, then holds it.
        private void TickArmed()
        {
            if (_pattern == null || !_pattern.RepeatedPlaceNearDetected)
            {
                _delayRolled = false;
                return;
            }

            if (!_delayRolled)
            {
                _armDelay = Random.Range(probeDelayMin, probeDelayMax);
                stateTimer = 0f;
                _delayRolled = true;
            }

            stateTimer += Time.deltaTime;
            if (stateTimer < _armDelay)
                return;

            // --- commit gate: no player distance, just "can we grab that object right now" ---
            if (_pickup == null || _movement == null || _perception == null)
                return;

            if (!_pickup.ReleaseModeIsPlace)
            {
                Debug.LogWarning("[CreatureProbe] CreaturePickup releaseMode is Throw - probe will not run (Throw is out of scope for the probe).");
                return;
            }

            if (!_pickup.IsIdle)
                return; // don't hijack a pickup/carry the creature started on its own

            if (_throwProbe != null && _throwProbe.IsEngaged)
                return; // the throw probe is mid-run this session - stand down (a finished one does not block)

            Interactable snapshot = _pattern.LastPlaceNearObject;
            if (snapshot == null || snapshot.Body == null || snapshot.IsHeld)
                return; // the object that formed the pattern is not available - wait, no fallback

            probeObject = snapshot;
            if (_pickup.RequestProbeGrab(probeObject))
            {
                state = ProbeState.Fetching;
                stateTimer = 0f;
            }
            // else: not grabbable this frame (e.g. just out of grabRange) - stay Armed, retry while the
            // gate holds. The grab range is small, so in practice the creature must already be beside it.
        }

        // Grab animation is running in CreaturePickup. Wait for it to reach Carrying, then Hold.
        private void TickFetching()
        {
            stateTimer += Time.deltaTime;

            if (_pickup.IsCarrying)
            {
                _perception.SetForcePlayerGaze(true); // from here on the creature wants to watch the player
                state = ProbeState.Holding;
                stateTimer = 0f;
                return;
            }

            // Grab collapsed back to Idle before ever carrying (object vanished mid-reach), or it just
            // never started. Pre-commit -> go back to waiting, re-roll the deliberation delay.
            if ((_pickup.IsIdle && stateTimer > 0.1f) || stateTimer >= fetchTimeout)
                AbortToArmed();
        }

        // Object in hand. Stand by indefinitely until the player is perceived and near enough, then
        // start delivering. Gaze override is already on; CreaturePerception only acts on it while the
        // player is actually perceived, so "holding and waiting" shows normal gaze until the player
        // appears, then snaps to the player.
        private void TickHolding()
        {
            stateTimer += Time.deltaTime;

            if (!_pickup.IsCarrying)
            {
                Terminate(ProbeOutcome.Aborted, "carry ended unexpectedly");
                return;
            }

            Transform player = _perception.Player;
            if (player == null || !_perception.IsPlayerPerceived)
                return; // keep holding

            if (FlatDistance(player.position) <= probeDeliverRange)
            {
                _movement.SetProbeApproachTarget(player);
                state = ProbeState.Delivering;
                stateTimer = 0f;
            }
        }

        // Walking to the player. Retreat may slow / cap the approach - that is fine. If the player
        // leaves the deliver condition, go back to Holding and keep carrying.
        //
        // Navigation 0.1: while CreatureNavLocomotion is actively carrying a valid, still-shortening
        // NavMesh path (_movement.IsNavProgressing), the creature is legitimately detouring around a
        // wall - its STRAIGHT-LINE distance to the player may not fall, or may even rise, without that
        // being failure. So the two give-ups that used straight-line distance / a raw timer are
        // suppressed while that holds, and "arrived" is judged by the actual route length. When Nav is
        // unavailable (no bake / no agent) IsNavProgressing is false and NavDistanceToDestination is
        // the flat straight-line distance, so this behaves exactly as it did before.
        private void TickDelivering()
        {
            stateTimer += Time.deltaTime;

            if (!_pickup.IsCarrying)
            {
                _movement.ClearProbeApproachTarget();
                Terminate(ProbeOutcome.Aborted, "carry ended unexpectedly");
                return;
            }

            Transform player = _perception.Player;
            if (player == null)
            {
                EnterFinishing(ProbeOutcome.Aborted);
                return;
            }

            bool navProgressing = _movement.IsNavProgressing;

            // Player broke the deliver condition (no longer perceived, or straight-line far AND not
            // being reached by a live path): stop approaching, hold, wait for them again.
            if (!_perception.IsPlayerPerceived ||
                (FlatDistance(player.position) > probeDeliverRange + deliverRangeHysteresis && !navProgressing))
            {
                _movement.ClearProbeApproachTarget();
                state = ProbeState.Holding;
                stateTimer = 0f;
                return;
            }

            // Arrived - by the actual route length when navigating, straight-line in fallback. This is
            // why a wall between the creature and the player no longer lets "success" fire through it.
            if (_movement.NavDistanceToDestination(player.position) <= probeReleaseDistance)
            {
                EnterFinishing(ProbeOutcome.Success);
                return;
            }

            // Too long trying - but only counts as failure once the creature is NOT making path
            // progress (a genuinely stuck / no-path delivery), not merely because a detour is long.
            if (stateTimer >= probeDeliverTimeout && !navProgressing)
                EnterFinishing(ProbeOutcome.Aborted);
        }

        // Object set-down is running in CreaturePickup (Releasing -> PlaceTarget -> ReleaseReturning).
        // Commit the outcome once it is back to Idle. Gaze stays forced to the player through this.
        private void TickFinishing()
        {
            stateTimer += Time.deltaTime;

            if (_pickup.IsIdle)
            {
                Terminate(_pendingOutcome,
                    _pendingOutcome == ProbeOutcome.Success ? "object placed toward player" : "object set down on give-up");
                return;
            }

            if (stateTimer >= 5f) // safety net - the Place path is ~0.6s
                Terminate(_pendingOutcome, "finish stalled");
        }

        // Stop steering, ask CreaturePickup to set the object down now via the normal Place path, and
        // remember which outcome this will be once that finishes.
        private void EnterFinishing(ProbeOutcome pending)
        {
            _pendingOutcome = pending;
            _movement.ClearProbeApproachTarget();
            _pickup.RequestProbeRelease();
            state = ProbeState.Finishing;
            stateTimer = 0f;
        }

        // Pre-commit only: nothing is being carried, so this is not a used-up attempt.
        private void AbortToArmed()
        {
            _movement.ClearProbeApproachTarget();
            _perception.SetForcePlayerGaze(false);
            probeObject = null;
            _delayRolled = false; // fresh deliberation delay before the next try
            state = ProbeState.Armed;
            stateTimer = 0f;
        }

        // One-shot end. Success or Aborted both land here and stay - no retry in 0.1.
        private void Terminate(ProbeOutcome result, string reason)
        {
            _movement.ClearProbeApproachTarget();
            _perception.SetForcePlayerGaze(false);
            outcome = result == ProbeOutcome.None ? ProbeOutcome.Aborted : result;
            state = ProbeState.Terminal;
            stateTimer = 0f;
            Debug.Log($"[CreatureProbe] probe {(outcome == ProbeOutcome.Success ? "SUCCESS" : "ABORTED")} - {reason}");
        }

        private float FlatDistance(Vector3 worldPos)
        {
            Vector3 flat = worldPos - transform.position;
            flat.y = 0f;
            return flat.magnitude;
        }

        private void OnValidate()
        {
            probeDelayMin = Mathf.Max(0f, probeDelayMin);
            probeDelayMax = Mathf.Max(probeDelayMin, probeDelayMax);
            probeDeliverRange = Mathf.Max(0f, probeDeliverRange);
            deliverRangeHysteresis = Mathf.Max(0f, deliverRangeHysteresis);
            probeReleaseDistance = Mathf.Max(0f, probeReleaseDistance);
            probeDeliverTimeout = Mathf.Max(1f, probeDeliverTimeout);
            fetchTimeout = Mathf.Max(1f, fetchTimeout);
        }
    }
}
