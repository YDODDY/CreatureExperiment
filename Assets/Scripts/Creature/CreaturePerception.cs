using UnityEngine;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// The creature's first perception -> behavior loop.
    ///
    /// Perception: is the player within <see cref="perceptionRange"/> (flat XZ distance)?
    /// Gaze direction: <see cref="lookPivot"/> eases toward the player's camera (yaw + pitch)
    /// while perceived, and back to its start forward otherwise. It carries nothing visible;
    /// it is just the smoothed "where the creature wants to look" vector.
    /// Expression: a small <see cref="pupil"/> slides inside a fixed eye toward that direction,
    /// clamped to a small range. When the target is past that range the pupil stops at the
    /// rim - the creature does not (yet) turn a head or body to follow further.
    ///
    /// Deliberately tiny: no field of view, no line of sight, no memory, no rig, no gaze
    /// framework. The single seam for later perception work is <see cref="IsPlayerPerceived"/>.
    /// </summary>
    public class CreaturePerception : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Empty transform whose rotation represents the gaze direction. Not a parent of the eye.")]
        [SerializeField] private Transform lookPivot;
        [Tooltip("Player root, used for the distance check. Auto-found by the \"Player\" tag if empty.")]
        [SerializeField] private Transform player;
        [Tooltip("What the gaze looks at. Defaults to the player's camera, else the player root.")]
        [SerializeField] private Transform gazeTarget;

        [Header("Perception")]
        [Tooltip("Player is perceived when within this flat (XZ) distance.")]
        [SerializeField] private float perceptionRange = 5f;

        [Header("Gaze direction")]
        [Tooltip("How fast the gaze direction turns, in degrees per second.")]
        [SerializeField] private float turnSpeed = 240f;

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

        /// <summary>Whether the player is currently within perception range. The seam future perception work reads.</summary>
        public bool IsPlayerPerceived { get; private set; }

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

            if (gazeTarget == null && player != null)
            {
                var cam = player.GetComponentInChildren<Camera>();
                gazeTarget = cam != null ? cam.transform : player;
            }
        }

        private void Update()
        {
            IsPlayerPerceived = PerceivePlayer();
            UpdateGazeDirection(IsPlayerPerceived);
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

        // Eases lookPivot toward the camera (or back to default). Unchanged behaviour, just no visible child.
        private void UpdateGazeDirection(bool perceived)
        {
            Quaternion desired;

            if (perceived && gazeTarget != null)
            {
                Vector3 dir = gazeTarget.position - lookPivot.position;
                if (dir.sqrMagnitude < 0.0001f)
                    return; // player essentially on the pivot; hold this frame
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
