using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.Creature
{
    /// <summary>
    /// The creature's first perception -> gaze loop.
    ///
    /// Perception: is the player within <see cref="perceptionRange"/> (flat XZ distance)?
    /// Attention (0.1): among everything perceivable in range - the player plus every
    /// <see cref="Interactable"/> the creature can actually SEE right now - pick the one whose flat
    /// XZ distance to the creature is smallest, and gaze at its live position. Nothing perceivable
    /// -> rest gaze. Among the candidates it is still distance only: no motion bias, no memory, no
    /// per-type weighting, no switch cooldown - the only change from 0.1 is which Interactables get
    /// into the candidate set (see below).
    ///
    /// Object visibility (0.2): an <see cref="Interactable"/> is a candidate only if it is within
    /// <see cref="perceptionRange"/>, inside the <see cref="fovAngle"/> cone measured from the
    /// creature's gaze direction (<see cref="facingReference"/> forward, flattened to XZ - see the
    /// anchor note below), AND has a clear line of sight - one ray from the eye to the object's
    /// origin that hits nothing on <see cref="objectOcclusionMask"/> except that object itself. An
    /// object that leaves the cone or goes behind a wall simply drops out of the candidate set that
    /// frame; there is no memory of having seen it (Known Object / permanence is a later step). This
    /// gate is applied ONLY to Interactables - player perception (<see cref="IsPlayerPerceived"/> and
    /// the player as a gaze candidate) is unchanged and still a pure omnidirectional distance test.
    ///
    /// FOV anchor = the gaze, NOT the body: <see cref="facingReference"/> is wired to
    /// <see cref="lookPivot"/>, which this component re-aims every frame toward the current gaze
    /// target (a visible object, else the player, else rest-forward) - fast, in 3D, and completely
    /// independent of whether the creature is translating. Anchoring the cone to BodyVisual instead
    /// self-locks: BodyVisual is only yawed by <c>CreatureBodyExpression</c> WHILE the creature is
    /// moving, and <c>CreatureMovement</c> only moves (Approach / Inspect-slide) when it already has
    /// an attended object - so the moment the creature stops with an empty cone (very easy right
    /// after a Retreat, whose delta points BodyVisual straight away from the player, and on every
    /// Inspect slide, which faces the body tangent to the ring so the watched object sits ~90 deg
    /// off-axis) nothing can ever swing the cone back onto an object, and it never re-acquires.
    /// The gaze anchor tracks the object through the whole Inspect cycle and, when the creature has
    /// lost every object and is staring at the player, keeps the cone pointed where the player is -
    /// so an object the player brings toward the creature re-enters range + FOV + LOS on its own.
    /// Gaze direction: <see cref="lookPivot"/> eases toward the chosen target (yaw + pitch)
    /// and back to its start forward when there is none. It carries nothing visible; it is
    /// just the smoothed "where the creature wants to look" vector.
    /// Expression: the <see cref="headPivot"/> turns the whole face toward that direction (yaw
    /// unlimited - this is not a human neck; pitch clamped only so the face stays off the capsule
    /// body), and a small <see cref="pupil"/> then slides inside the eye toward the residual
    /// direction the head has not yet covered. Body rotation is still not part of this.
    ///
    /// Deliberately still tiny: FOV + LOS for objects only, and no memory, no object familiarity,
    /// no wander / search, no rig, no gaze framework, no target registry, no pathfinding, no
    /// meaning/type judgement. <see cref="IsPlayerPerceived"/> stays a pure player-in-range test for
    /// <c>CreatureMovement</c>; it is unaffected by the object FOV/LOS gate and by what the creature
    /// is actually looking at.
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
        [Tooltip("A target is perceivable when within this flat (XZ) distance. Governs both the player check and the maximum distance an Interactable can be spotted.")]
        [SerializeField] private float perceptionRange = 5f;

        [Header("Object field of view (Interactables only - not the player)")]
        [Tooltip("Transform whose forward (flattened to XZ) is the axis of the object FOV cone. Wire this to LookPivot: the cone then follows the creature's gaze, which is re-aimed every frame and never freezes while the creature stands still (anchoring it to BodyVisual self-locks - see the class summary). Empty falls back to LookPivot, then the creature root.")]
        [SerializeField] private Transform facingReference;
        [Tooltip("Full horizontal cone, in degrees, within which an Interactable can be seen. A candidate must be within half of this angle either side of the facing direction.")]
        [SerializeField] private float fovAngle = 90f;
        [Tooltip("Colliders that block line of sight to an Interactable (walls, floors, other objects). The Interactable's own collider never blocks itself. The creature's own capsule is ignored because the ray starts inside it.")]
        [SerializeField] private LayerMask objectOcclusionMask = ~0;

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

        // CreatureProbe seam: while true AND the player is actually perceived, the creature LOOKS at the
        // player regardless of what SelectGazeTarget picked. It does not change attention selection
        // itself (AttendedInteractable is still computed every frame), only the gaze/head/pupil aim.
        // When the player is not perceived this falls back to the normal gaze - there is no search gaze.
        private bool _forcePlayerGaze;

        /// <summary>Whether the player is currently within perception range. The seam <c>CreatureMovement</c> reads.</summary>
        public bool IsPlayerPerceived { get; private set; }

        /// <summary>The player transform this component tracks, or null. Read-only seam for sibling components (e.g. movement).</summary>
        public Transform Player => player;

        /// <summary>The transform the creature is gazing at this frame, or null when the range is empty. Read-only, for inspection.</summary>
        public Transform CurrentGazeTarget { get; private set; }

        /// <summary>
        /// The <see cref="Interactable"/> the creature is currently attending to (the nearest one that won
        /// <see cref="SelectGazeTarget"/> this frame), or null when the winner is the player or nothing is
        /// in range. This is only a read-only seam - attention and any "action target" stay separate ideas;
        /// <c>CreatureMovement</c> merely borrows this to seed what it walks to and inspects this prototype.
        /// </summary>
        public Interactable AttendedInteractable { get; private set; }

        private void Awake()
        {
            if (lookPivot == null)
                lookPivot = transform;
            _defaultLocalRotation = lookPivot.localRotation;

            // Gaze anchor by default (see class summary): a body-facing anchor self-locks because it
            // only turns while the creature is already moving. lookPivot is guaranteed set just above.
            if (facingReference == null)
                facingReference = lookPivot != null ? lookPivot : transform;

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

        /// <summary>
        /// CreatureProbe only: force the creature's gaze onto the player for the duration of a probe.
        /// Honoured only while <see cref="IsPlayerPerceived"/>; otherwise the normal nearest-target gaze
        /// is used (no search gaze). Attention selection is unaffected either way.
        /// </summary>
        public void SetForcePlayerGaze(bool on) => _forcePlayerGaze = on;

        private void Update()
        {
            IsPlayerPerceived = PerceivePlayer();

            // Selection still runs every frame so AttendedInteractable stays correct for
            // CreatureMovement / CreaturePickup. The override below only changes what the creature
            // looks at, not what it treats as its attention target.
            Transform selected = SelectGazeTarget();

            CurrentGazeTarget = (_forcePlayerGaze && IsPlayerPerceived && player != null)
                ? (playerGazeTarget != null ? playerGazeTarget : player)
                : selected;

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
            Interactable bestInteractable = null;

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

                    // 0.2 addition: the object must be in range, in the FOV cone and not occluded.
                    // Everything below this line is the unchanged 0.1 nearest-by-flat-distance pick,
                    // just over the visible subset.
                    if (!CanPerceiveInteractable(it))
                        continue;

                    float sqr = FlatSqrDistance(it.transform.position);
                    if (sqr <= rangeSqr && sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = it.transform;
                        bestInteractable = it;
                    }
                }
            }

            // bestInteractable is always the one whose transform is 'best', or null when the player
            // (checked first, above) or nothing won. Recorded as a read-only seam; selection itself
            // is unchanged - still pure nearest-by-flat-distance.
            AttendedInteractable = bestInteractable;
            return best;
        }

        private float FlatSqrDistance(Vector3 worldPos)
        {
            Vector3 flat = worldPos - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude;
        }

        /// <summary>
        /// The 0.2 object-visibility gate: range (flat XZ, same limit as the player check) AND inside
        /// the <see cref="fovAngle"/> cone around <see cref="facingReference"/>'s flattened forward
        /// AND a clear line of sight from the eye. Applied to Interactables only; never to the player.
        /// Also used by the gizmos so the Scene view matches what selection actually sees.
        /// </summary>
        private bool CanPerceiveInteractable(Interactable it)
        {
            Vector3 flat = it.transform.position - transform.position;
            flat.y = 0f;
            float distSqr = flat.sqrMagnitude;
            if (distSqr > perceptionRange * perceptionRange)
                return false;

            // FOV: angle between the creature's flattened facing and the flat direction to the target.
            // Skipped only when the target is basically on top of the creature (direction undefined).
            if (distSqr > 1e-4f)
            {
                Transform face = facingReference != null ? facingReference : transform;
                Vector3 facing = face.forward;
                facing.y = 0f;
                if (facing.sqrMagnitude > 1e-6f && Vector3.Angle(facing, flat) > fovAngle * 0.5f)
                    return false;
            }

            // LOS: one ray from the eye to the object's origin. Anything on objectOcclusionMask that
            // is not part of THIS interactable blocks it. The ray starts on the creature's own capsule
            // axis, so Unity never reports the capsule as the blocker.
            Vector3 eye = lookPivot != null ? lookPivot.position : transform.position;
            Vector3 toTarget = it.transform.position - eye;
            float dist = toTarget.magnitude;
            if (dist > 1e-3f &&
                Physics.Raycast(eye, toTarget / dist, out RaycastHit hit, dist + 0.01f,
                                objectOcclusionMask, QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<Interactable>() != it)
                return false;

            return true;
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

        private void OnValidate()
        {
            perceptionRange = Mathf.Max(0f, perceptionRange);
            fovAngle = Mathf.Clamp(fovAngle, 1f, 360f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, perceptionRange);

            Transform pivot = lookPivot != null ? lookPivot : transform;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(pivot.position, pivot.forward * 1.5f);

            // Object FOV cone (flattened) from the facing reference, plus a green/red line to every
            // Interactable showing whether it passes the range + FOV + LOS gate right now.
            Transform face = facingReference != null ? facingReference : transform;
            Vector3 facing = face.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude > 1e-6f)
            {
                facing.Normalize();
                Vector3 origin = transform.position;
                Vector3 left = Quaternion.AngleAxis(-fovAngle * 0.5f, Vector3.up) * facing;
                Vector3 right = Quaternion.AngleAxis(fovAngle * 0.5f, Vector3.up) * facing;
                Gizmos.color = new Color(1f, 0.6f, 0.1f);
                Gizmos.DrawRay(origin, left * perceptionRange);
                Gizmos.DrawRay(origin, right * perceptionRange);

                var list = Application.isPlaying
                    ? _interactables
                    : FindObjectsByType<Interactable>(FindObjectsSortMode.None);
                if (list != null)
                {
                    Vector3 eye = lookPivot != null ? lookPivot.position : transform.position;
                    foreach (var it in list)
                    {
                        if (it == null)
                            continue;
                        Gizmos.color = CanPerceiveInteractable(it) ? Color.green : new Color(1f, 0.25f, 0.2f);
                        Gizmos.DrawLine(eye, it.transform.position);
                    }
                }
            }
        }
    }
}
