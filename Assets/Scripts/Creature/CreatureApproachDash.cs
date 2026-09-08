using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// Long-Distance Object Approach Dash (0.1): the ONLY autonomous use of the Dash capability.
    /// When <see cref="CreatureMovement"/> is genuinely running a straight-line Object Approach
    /// (<see cref="CreatureMovement.IsApproaching"/>) toward an <see cref="Interactable"/> that is
    /// still far, this waits a brief beat and then asks <see cref="CreatureDash"/> for exactly one
    /// burst, then leaves it alone for the rest of that Approach.
    ///
    /// Dash decides HOW FAST, never WHERE or WHY. This component reads only CreatureMovement's own
    /// Approach state plus the target's live distance and calls <see cref="CreatureDash.RequestDash"/>
    /// / <see cref="CreatureDash.CancelDash"/>. It never selects a target, never moves the root, never
    /// changes a heading, never touches Attention / Approach / Inspect / the Probes. Retreat, a Probe
    /// approach, Player Observe's hop and Wander are all NOT an Object Approach, so
    /// <see cref="CreatureMovement.IsApproaching"/> is false for them and this can never fire there.
    ///
    /// One dash per Approach EPISODE: an episode is one continuous run of IsApproaching toward one
    /// Interactable. When the target changes, goes null, becomes held / in-flight, the creature
    /// arrives (Inspect starts), or a higher-priority behaviour preempts - IsApproaching drops, the
    /// episode ends, the once-per-episode latch resets, and any dash this component started is
    /// cancelled so it cannot bleed speed into whatever runs next.
    ///
    /// Deliberately tiny: no staging system, no random dash, no Retreat / Player Observe / Wander
    /// dash, no Jump autonomy, no navigation, no target selection, no Brain / Decision. The dev
    /// testKey on <see cref="CreatureDash"/> still works exactly as before.
    /// </summary>
    [RequireComponent(typeof(CreatureMovement))]
    [DefaultExecutionOrder(45)] // after CreatureMovement (0) so IsApproaching / InspectTarget are this frame's
    public class CreatureApproachDash : MonoBehaviour
    {
        [Header("When to dash")]
        [Tooltip("Only dash while the creature is still at least this far (flat XZ) from the Object it is approaching. Below it, the normal walk covers the last stretch.")]
        [SerializeField] private float dashDistanceThreshold = 6f;
        [Tooltip("Let the normal Approach run for at least this long (seconds) before the dash, so it reads as 'decided to go, set off, then dashed' rather than an instant teleport.")]
        [SerializeField] private float preDashDelay = 0.4f;

        [Header("State (read-only, for debugging)")]
        [Tooltip("The Interactable of the Approach episode currently being tracked, or None.")]
        [SerializeField] private Interactable episodeTarget;
        [Tooltip("Seconds the current Approach episode has been running.")]
        [SerializeField] private float episodeApproachTime;
        [Tooltip("True once this episode has already spent its one autonomous dash.")]
        [SerializeField] private bool dashedThisEpisode;

        private CreatureMovement _movement;
        private CreatureDash _dash;   // optional - without it this component simply does nothing
        private bool _weStartedDash;  // the dash currently running (if any) was requested by us, not the dev key

        private void Awake()
        {
            _movement = GetComponent<CreatureMovement>();
            _dash = GetComponent<CreatureDash>();
        }

        private void Update()
        {
            // Our dash finished on its own timer -> stop attributing the (now over) dash to us, so a
            // later dev-key dash is never mistaken for ours and cancelled at episode end.
            if (_weStartedDash && (_dash == null || !_dash.IsDashing))
                _weStartedDash = false;

            // The one thing this component keys off: is CreatureMovement running an Object Approach
            // this frame, and toward what? Anything else (Retreat, Probe, Inspect, idle, Player
            // Observe, Wander) leaves this null.
            Interactable target = _movement.IsApproaching ? _movement.InspectTarget : null;

            if (target != episodeTarget)
            {
                EndEpisode();          // episode boundary: reset the latch, clean up a running dash
                episodeTarget = target;
            }

            if (episodeTarget == null)
                return;

            episodeApproachTime += Time.deltaTime;

            if (dashedThisEpisode || _dash == null || _dash.IsDashing)
                return;
            if (episodeApproachTime < preDashDelay)
                return;
            if (FlatDistance(episodeTarget.transform.position) < dashDistanceThreshold)
                return; // close enough already - let the normal walk finish it

            _dash.RequestDash();
            _weStartedDash = true;
            dashedThisEpisode = true;
        }

        private void EndEpisode()
        {
            // If the dash we started is still running when its Approach ends, stop it now.
            if (_weStartedDash && _dash != null && _dash.IsDashing)
                _dash.CancelDash();
            _weStartedDash = false;
            episodeApproachTime = 0f;
            dashedThisEpisode = false;
        }

        private float FlatDistance(Vector3 world)
        {
            Vector3 f = world - transform.position;
            f.y = 0f;
            return f.magnitude;
        }

        private void OnDisable()
        {
            EndEpisode();
            episodeTarget = null;
        }

        private void OnValidate()
        {
            dashDistanceThreshold = Mathf.Max(0f, dashDistanceThreshold);
            preDashDelay = Mathf.Max(0f, preDashDelay);
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || episodeTarget == null)
                return;
            Gizmos.color = dashedThisEpisode ? new Color(0.5f, 0.5f, 0.5f) : new Color(1f, 0.9f, 0.2f);
            Gizmos.DrawLine(transform.position, episodeTarget.transform.position);
        }
    }
}
