using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// The creature's first perception -> gaze loop.
    ///
    /// Perception: is the player within <see cref="perceptionRange"/> (flat XZ distance)?
    /// Attention (0.1): among everything perceivable in range - the player plus every
    /// <see cref="Interactable"/> in the scene - pick the one whose flat XZ distance to the
    /// creature is smallest, and gaze at its live position. Nothing in range -> rest gaze.
    /// Distance only: no motion bias, no memory, no per-type weighting, no switch cooldown.
    /// Gaze direction: <see cref="lookPivot"/> eases toward the chosen target (yaw + pitch)
    /// and back to its start forward when there is none. It carries nothing visible; it is
    /// just the smoothed "where the creature wants to look" vector.
    /// Expression: the <see cref="headPivot"/> turns the whole face toward that direction (yaw
    /// unlimited - this is not a human neck; pitch clamped only so the face stays off the capsule
    /// body), and a small <see cref="pupil"/> then slides inside the eye toward the residual
    /// direction the head has not yet covered. Body rotation is still not part of this.
    ///
    /// Deliberately tiny: no field of view, no line of sight, no memory, no rig, no gaze
    /// framework, no target registry. <see cref="IsPlayerPerceived"/> stays a pure
    /// player-in-range test for <c>CreatureMovement</c>; it is unaffected by what the
    /// creature is actually looking at.
    /// </summary>
    public class CreaturePerception : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Empty transform whose rotation represents the gaze direction. Not a parent of the eye.")]
        [SerializeField] private Transform lookPivot;
        [Tooltip("Player root, used for the distance check. Auto-found by the \"Player\" tag if empty.")]
        [SerializeField] private Transform player;
        [Tooltip("Where to look when the chosen target is the player. Defaults to the player's camera, else the player root.")]
        [SerializeField] private Transform playerGazeTarget;

        [Header("Perception")]
        [Tooltip("A target is perceivable when within this flat (XZ) distance.")]
        [SerializeField] private float perceptionRange = 5f;

        [Header("Gaze direction")]
        [Tooltip("How fast the gaze direction turns, in degrees per second.")]
        [SerializeField] private float turnSpeed = 240f;

        [Header("Head follow")]
        [Tooltip("Pivot the head/face turns around. Empty child of the creature root, parent of Face.")]
        [SerializeField] private Transform headPivot;
        [Tooltip("How fast the head turns toward the gaze direction, in degrees per second. Keep below turnSpeed so the eye leads.")]
        [SerializeField] private float headTurnSpeed = 140f;
        [Tooltip("Max downward pitch, in degrees. Not a neck limit - keeps the face off the capsule body when looking down.")]
        [SerializeField] private float maxLookDown = 55f;
        [Tooltip("Max upward pitch, in degrees.")]
        [SerializeField] private float maxLookUp = 80f;

        [Header("Pupil expression")]
        [Tooltip("The moving pupil. Child of the fixed eye; only its local X/Y are driven.")]
        [SerializeField] private Transform pupil;
        [Tooltip("Fixed eye transform. Its local axes define 'straight ahead' (+Z) for the pupil.")]
        [SerializeField] private Transform eyeReference;
        [Tooltip("Max pupil travel from centre, in the eye's local units.")]
        [SerializeField] private float pupilMaxOffset = 0.26f;
        [Tooltip("Maps how far off-axis the gaze is to pupil travel. Higher = pupil reaches the rim sooner.")]
        [SerializeField] private float pupilGain = 0.6f;

        private Quaternion _defaultLocalRotation;
        private Vector3 _pupilRestLocalPos;
        private Interactable[] _interactables;

        /// <summary>Whether the player is currently within perception range. The seam <c>CreatureMovement</c> reads.</summary>
        public bool IsPlayerPerceived { get; private set; }

        /// <summary>The player transform this component tracks, or null. Read-only seam for sibling components (e.g. movement).</summary>
        public Transform Player => player;

        /// <summary>The transform the creature is gazing at this frame, or null when the range is empty. Read-only, for inspection.</summary>
        public Transform CurrentGazeTarget { get; private set; }

        private void Awake()
        {
            if (lookPivot == null)
                lookPivot = transform;
            _defaultLocalRotation = lookPivot.localRotation;

            if (pupil != null)
                _pupilRestLocalPos = pupil.localPosition;

            if (player == null)
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null)
                    player = tagged.transform;
            }

            if (playerGazeTarget == null && player != null)
            {
                var cam = player.GetComponentInChildren<Camera>();
                playerGazeTarget = cam != null ? cam.transform : player;
            }

            // Prototype scope: interactables are never spawned or destroyed at runtime, so one
            // lookup is enough. No registry, no per-frame scene search.
            _interactables = FindObjectsByType<Interactable>(FindObjectsSortMode.None);
        }

        private void Update()
        {
            IsPlayerPerceived = PerceivePlayer();
            CurrentGazeTarget = SelectGazeTarget();
            UpdateGazeDirection(CurrentGazeTarget);
            UpdateHead();
            UpdatePupil();
        }

        private bool PerceivePlayer()
        {
            if (player == null)
                return false;

            Vector3 flat = player.position - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude <= perceptionRange * perceptionRange;
        }

        // Attention 0.1: nearest perceivable candidate by flat XZ distance. The player is one
        // candidate (measured at its root, looked at via playerGazeTarget); every Interactable is
        // a candidate (measured and looked at via its own transform). Null when nothing is in range.
        private Transform SelectGazeTarget()
        {
            float rangeSqr = perceptionRange * perceptionRange;
            float bestSqr = float.MaxValue;
            Transform best = null;

            if (player != null)
            {
                float sqr = FlatSqrDistance(player.position);
                if (sqr <= rangeSqr && sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = playerGazeTarget != null ? playerGazeTarget : player;
                }
            }

            if (_interactables != null)
            {
                foreach (var it in _interactables)
                {
                    if (it == null)
                        continue;

                    float sqr = FlatSqrDistance(it.transform.position);
                    if (sqr <= rangeSqr && sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = it.transform;
                    }
                }
            }

            return best;
        }

        private float FlatSqrDistance(Vector3 worldPos)
        {
            Vector3 flat = worldPos - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude;
        }

        // Eases lookPivot toward the chosen target (or back to default). Unchanged behaviour.
        private void UpdateGazeDirection(Transform target)
        {
            Quaternion desired;

            if (target != null)
            {
                Vector3 dir = target.position - lookPivot.position;
                if (dir.sqrMagnitude < 0.0001f)
                    return; // target essentially on the pivot; hold this frame
                desired = Quaternion.LookRotation(dir);
            }
            else
            {
                desired = lookPivot.parent != null
                    ? lookPivot.parent.rotation * _defaultLocalRotation
                    : _defaultLocalRotation;
            }

            lookPivot.rotation = Quaternion.RotateTowards(
                lookPivot.rotation, desired, turnSpeed * Time.deltaTime);
        }

        // Turns the head/face pivot toward the same desired direction the pupil chases, so the
        // creature's attention is readable from the side and behind. Yaw is unlimited (this is
        // not a human neck); pitch is clamped only so the face does not sink into the capsule.
        private void UpdateHead()
        {
            if (headPivot == null)
                return;

            Vector3 aimDir = lookPivot.forward;

            // Yaw from the flat (XZ) part of the aim. When the target is almost straight up or
            // down that part is ~0 and yaw is meaningless, so hold the head's current yaw.
            Vector3 flat = new Vector3(aimDir.x, 0f, aimDir.z);
            float yaw = flat.sqrMagnitude < 1e-6f
                ? headPivot.eulerAngles.y
                : Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg;

            // Pitch from the vertical part; positive euler X points the face down. Clamp only to
            // keep the face off the body, not as a neck limit.
            float pitch = -Mathf.Asin(Mathf.Clamp(aimDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, -maxLookUp, maxLookDown);

            Quaternion desired = Quaternion.Euler(pitch, yaw, 0f); // world-space aim; body is not rotated
            headPivot.rotation = Quaternion.RotateTowards(
                headPivot.rotation, desired, headTurnSpeed * Time.deltaTime);
        }

        // Slides the pupil within the eye toward the (already smoothed) gaze direction, clamped.
        private void UpdatePupil()
        {
            if (pupil == null || eyeReference == null)
                return;

            Vector3 localDir = eyeReference.InverseTransformDirection(lookPivot.forward);
            Vector2 planar = new Vector2(localDir.x, localDir.y);
            Vector2 planarDir = planar.sqrMagnitude > 1e-6f ? planar.normalized : Vector2.zero;

            // tan(off-axis angle): 0 dead ahead, grows with angle, "infinite" at / behind the eye plane.
            float travel = localDir.z > 0.001f ? planar.magnitude / localDir.z : float.MaxValue;
            Vector2 offset = planarDir * Mathf.Min(travel * pupilGain, pupilMaxOffset);

            pupil.localPosition = new Vector3(
                _pupilRestLocalPos.x + offset.x,
                _pupilRestLocalPos.y + offset.y,
                _pupilRestLocalPos.z);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, perceptionRange);

            Transform pivot = lookPivot != null ? lookPivot : transform;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(pivot.position, pivot.forward * 1.5f);
        }
    }
}
