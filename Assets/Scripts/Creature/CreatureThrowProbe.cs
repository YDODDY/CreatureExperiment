using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Throw Probe (0.1): sibling experiment to <see cref="CreatureProbe"/>. Once
    /// <see cref="CreaturePattern.RepeatedThrowMissNearDetected"/> latches (the Player kept THROWING an
    /// object that missed the creature but landed NEAR), the creature - after a short deliberation
    /// delay - picks up that same object, holds it, and once the Player is perceived and within
    /// <see cref="throwProbeRange"/> throws it once, aimed at the Player. One attempt per session.
    ///
    /// NOT attack / retaliation / play / gift. It is "the Player did this repeatedly, so the creature
    /// tries a similar physical action back once". No meaning label, no Decision / Hypothesis / Intent /
    /// Interest / Search system.
    ///
    /// Structurally lighter than <see cref="CreatureProbe"/>: a throw needs no approach, so there is no
    /// Delivering phase and this component never touches <see cref="CreatureMovement"/>.
    ///
    /// State flow: Armed -> Fetching -> Holding -> Finishing -> Terminal
    ///   Armed     : pattern detected, waiting out the deliberation delay
    ///   Fetching  : CreaturePickup is running the grab
    ///   Holding   : object in hand, standing by until the Player is perceived and within range.
    ///               No timeout - holds indefinitely. Gaze is forced to the Player here on (honoured
    ///               only while the Player is actually perceived).
    ///   Finishing : CreaturePickup is running the throw (or a give-up set-down)
    ///   Terminal  : done for this session - Success or Aborted, no retry
    ///
    /// Reuse: grab / throw -> <see cref="CreaturePickup"/> (RequestProbeGrab / RequestProbeReleaseAsThrow);
    /// looking at the Player -> <see cref="CreaturePerception"/> (SetForcePlayerGaze). Retreat is
    /// untouched (no approach here). The creature's own throw does not raise PhysicalEvents, so it
    /// cannot re-trigger the pattern.
    /// </summary>
    [RequireComponent(typeof(CreaturePattern))]
    [RequireComponent(typeof(CreaturePickup))]
    [RequireComponent(typeof(CreaturePerception))]
    public class CreatureThrowProbe : MonoBehaviour
    {
        public enum ProbeState { Armed, Fetching, Holding, Finishing, Terminal }
        public enum ProbeOutcome { None, Success, Aborted }

        [Header("Start (deliberation)")]
        [Tooltip("After the pattern is detected, wait a random time in this range before grabbing. Keeps 'detected' from reading as instant obedience.")]
        [SerializeField] private float probeDelayMin = 2f;
        [SerializeField] private float probeDelayMax = 5f;

        [Header("Throw")]
        [Tooltip("From Holding, the probe throws once the player is perceived AND within this flat XZ distance. Loose playtest tuning value, not a firm rule - adjust after playtest. Keep at or below CreaturePerception.perceptionRange.")]
        [SerializeField] private float throwProbeRange = 4f;
        [Tooltip("Seconds to wait for the grab to actually start once fetching. If it does not, drop back to Armed (this is 'not yet', not a spent attempt).")]
        [SerializeField] private float fetchTimeout = 6f;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private ProbeState state = ProbeState.Armed;
        [SerializeField] private ProbeOutcome outcome = ProbeOutcome.None;
        [Tooltip("The object this probe locked onto - snapshot of CreaturePattern.LastThrowMissNearObject at start.")]
        [SerializeField] private Interactable probeObject;
        [Tooltip("Seconds spent in the current ProbeState.")]
        [SerializeField] private float stateTimer;

        /// <summary>Current phase of the one-shot probe.</summary>
        public ProbeState State => state;

        /// <summary>None while not finished; Success if the object was thrown toward the Player; Aborted otherwise. Latched once Terminal.</summary>
        public ProbeOutcome Outcome => outcome;

        /// <summary>True while this probe is mid-run (past Armed, not yet Terminal). CreatureProbe checks this so only one probe engages at a time; a finished (Terminal) probe does NOT block the other.</summary>
        public bool IsEngaged => state != ProbeState.Armed && state != ProbeState.Terminal;

        private CreaturePattern _pattern;
        private CreaturePickup _pickup;
        private CreaturePerception _perception;
        private CreatureProbe _placeProbe; // optional sibling

        private float _armDelay;
        private bool _delayRolled;
        private ProbeOutcome _pendingOutcome;

        private void Awake()
        {
            _pattern = GetComponent<CreaturePattern>();
            _pickup = GetComponent<CreaturePickup>();
            _perception = GetComponent<CreaturePerception>();
            _placeProbe = GetComponent<CreatureProbe>();
        }

        private void Update()
        {
            switch (state)
            {
                case ProbeState.Armed:     TickArmed();     break;
                case ProbeState.Fetching:  TickFetching();  break;
                case ProbeState.Holding:   TickHolding();   break;
                case ProbeState.Finishing: TickFinishing(); break;
                case ProbeState.Terminal:                   break;
            }
        }

        private void TickArmed()
        {
            if (_pattern == null || !_pattern.RepeatedThrowMissNearDetected)
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
            if (_pickup == null || _perception == null)
                return;

            if (!_pickup.IsIdle)
                return; // don't hijack a pickup/carry the creature started on its own

            if (_placeProbe != null && _placeProbe.IsEngaged)
                return; // the place probe is mid-run this session - stand down (a finished one does not block)

            Interactable snapshot = _pattern.LastThrowMissNearObject;
            if (snapshot == null || snapshot.Body == null || snapshot.IsHeld)
                return; // the object that formed the pattern is not available - wait, no fallback

            probeObject = snapshot;
            if (_pickup.RequestProbeGrab(probeObject))
            {
                state = ProbeState.Fetching;
                stateTimer = 0f;
            }
        }

        private void TickFetching()
        {
            stateTimer += Time.deltaTime;

            if (_pickup.IsCarrying)
            {
                _perception.SetForcePlayerGaze(true);
                state = ProbeState.Holding;
                stateTimer = 0f;
                return;
            }

            if ((_pickup.IsIdle && stateTimer > 0.1f) || stateTimer >= fetchTimeout)
                AbortToArmed();
        }

        // Object in hand. Wait indefinitely until the player is perceived and near enough, then throw.
        private void TickHolding()
        {
            stateTimer += Time.deltaTime;

            if (!_pickup.IsCarrying)
            {
                Terminate(ProbeOutcome.Aborted, "carry ended unexpectedly");
                return;
            }

            Transform player = _perception.Player;
            if (player == null)
            {
                EnterFinishing(ProbeOutcome.Aborted); // drop it where it stands, abort
                return;
            }
            if (!_perception.IsPlayerPerceived)
                return; // keep holding

            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.magnitude > throwProbeRange)
                return; // keep holding until the player is close enough for a plausible throw

            _pendingOutcome = ProbeOutcome.Success;
            _pickup.RequestProbeReleaseAsThrow(toPlayer); // CreaturePickup guards + normalizes; adds the arc
            state = ProbeState.Finishing;
            stateTimer = 0f;
        }

        // Throw (or give-up set-down) is running in CreaturePickup. Commit the outcome once it is back
        // to Idle. Gaze stays forced to the player through this.
        private void TickFinishing()
        {
            stateTimer += Time.deltaTime;

            if (_pickup.IsIdle)
            {
                Terminate(_pendingOutcome,
                    _pendingOutcome == ProbeOutcome.Success ? "object thrown toward player" : "object set down on give-up");
                return;
            }

            if (stateTimer >= 5f) // safety net
                Terminate(_pendingOutcome, "finish stalled");
        }

        // Abort while carrying: set the object down via the normal Place path (safe teardown), record Aborted.
        private void EnterFinishing(ProbeOutcome pending)
        {
            _pendingOutcome = pending;
            _pickup.RequestProbeRelease();
            state = ProbeState.Finishing;
            stateTimer = 0f;
        }

        // Pre-commit only: nothing is being carried, so this is not a used-up attempt.
        private void AbortToArmed()
        {
            _perception.SetForcePlayerGaze(false);
            probeObject = null;
            _delayRolled = false;
            state = ProbeState.Armed;
            stateTimer = 0f;
        }

        private void Terminate(ProbeOutcome result, string reason)
        {
            _perception.SetForcePlayerGaze(false);
            outcome = result == ProbeOutcome.None ? ProbeOutcome.Aborted : result;
            state = ProbeState.Terminal;
            stateTimer = 0f;
            Debug.Log($"[CreatureThrowProbe] probe {(outcome == ProbeOutcome.Success ? "SUCCESS" : "ABORTED")} - {reason}");
        }

        private void OnValidate()
        {
            probeDelayMin = Mathf.Max(0f, probeDelayMin);
            probeDelayMax = Mathf.Max(probeDelayMin, probeDelayMax);
            throwProbeRange = Mathf.Max(0f, throwProbeRange);
            fetchTimeout = Mathf.Max(1f, fetchTimeout);
        }
    }
}
