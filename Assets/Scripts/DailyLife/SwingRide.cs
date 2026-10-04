using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One swing of a swing set (on the swing's non-moving root; the seat collider is under <see cref="pivot"/>, so the aim
    /// finds this through GetComponentInParent). The ropes + seat are one rigid pendulum: <see cref="pivot"/> rotates about
    /// its local X, + angle = seat toward this root's forward (the rider faces forward). No rope or joint physics - an
    /// angular simulation: θ'' = -(g/L)·sin θ - damping·θ' + pump.
    ///
    /// Pumping (W = forward, S = backward) follows the motion like on a real swing: W pushes forward while the seat
    /// is moving forward (or at rest) and only nudges weakly against a backward swing; S mirrors it. So timing W on the
    /// forward swing and S on the back swing builds the arc up step by step; holding one key still grows it, slower.
    /// The pump fades out between <see cref="pumpFadeStart"/> and <see cref="pumpFadeEnd"/>, extra damping past
    /// <see cref="softLimit"/> brakes an outward swing, and <see cref="hardLimit"/> is a stop (no loop-the-loop).
    ///
    /// E on the seat gets on (only while the swing is nearly still); A / D gets off at <see cref="exitPoint"/> - no
    /// launch, the swing carries on without the rider and settles. The camera rides the seat and leans with the arc.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SwingRide : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("Parts")]
        [Tooltip("Rotates about its local X (ropes + seat under it).")]
        [SerializeField] private Transform pivot;
        [Tooltip("Seat surface point (child of the pivot).")]
        [SerializeField] private Transform seatPoint;
        [Tooltip("Where the feet go when getting off - beside the frame, out of the arc.")]
        [SerializeField] private Transform exitPoint;
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private PlayerInteractor player;

        [Header("Motion")]
        [SerializeField] private float gravity = 9.81f;
        [Tooltip("Pump angular acceleration, rad/s².")]
        [SerializeField] private float pumpAcceleration = 0.35f;
        [Tooltip("Share of the pump that still acts against the swing's current direction.")]
        [SerializeField] private float counterPumpShare = 0.3f;
        [SerializeField] private float riddenDamping = 0.06f;
        [SerializeField] private float emptyDamping = 0.4f;
        [Tooltip("Degrees: the pump starts fading here ...")]
        [SerializeField] private float pumpFadeStart = 65f;
        [Tooltip("... and is gone here.")]
        [SerializeField] private float pumpFadeEnd = 100f;
        [Tooltip("Degrees past which an outward swing is braked.")]
        [SerializeField] private float softLimit = 85f;
        [SerializeField] private float hardLimit = 115f;

        [Header("Getting on")]
        [SerializeField] private float mountMaxAngle = 20f;
        [Tooltip("deg/s")]
        [SerializeField] private float mountMaxSpeed = 60f;

        [Header("Rider")]
        [SerializeField] private float eyeAboveSeat = 0.74f;
        [Tooltip("View lean per degree of swing (looking up on the forward swing).")]
        [SerializeField] private float viewLean = 0.22f;

        [Header("Labels")]
        [SerializeField] private string sitPrompt = "E · 그네 타기";
        [SerializeField] private string rideHint = "W / S · 구르기 (흔들림에 맞춰)     A / D · 내리기";

        private readonly PlaygroundRider _rider = new PlaygroundRider();
        private float _theta;   // rad
        private float _omega;   // rad/s
        private float _length = 2f;
        private object _occupant;

        /// <summary>Swing angle in degrees (+ = forward).</summary>
        public float AngleDegrees => _theta * Mathf.Rad2Deg;
        public bool PlayerSeated => _rider.IsMounted;
        public object Occupant => _occupant;

        private void Awake()
        {
            if (player == null)
                player = FindFirstObjectByType<PlayerInteractor>();
            if (pivot != null && seatPoint != null)
                _length = Mathf.Max(0.5f, Vector3.Distance(pivot.position, seatPoint.position));
            ApplyPose();
        }

        private void OnDisable()
        {
            if (_rider.IsMounted)
                DismountPlayer();
        }

        // --- IUsable / IFocusTarget ----------------------------------------------------------------------------

        public bool CanUse => _occupant == null && PlaygroundRider.PlayerAvailable(player)
                              && Mathf.Abs(_theta) * Mathf.Rad2Deg < mountMaxAngle && Mathf.Abs(_omega) * Mathf.Rad2Deg < mountMaxSpeed;

        public void Use()
        {
            if (!CanUse || seatPoint == null)
                return;
            if (_rider.Mount(player, Eye(), transform.eulerAngles.y, 5f))
                _occupant = player;
        }

        public string FocusName => CanUse ? sitPrompt : "";
        public Transform FocusTransform => seatPoint != null ? seatPoint : transform;
        public void SetFocused(bool focused) { }

        // --- Loop -------------------------------------------------------------------------------------------

        private void Update()
        {
            float pump = 0f;
            if (_rider.IsMounted)
            {
                if (_rider.WantsExit(sidewaysOnly: true))
                    DismountPlayer();
                else
                    pump = _rider.Move.y;
            }

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.005f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
                Step(h, pump);
            ApplyPose();
        }

        private void LateUpdate()
        {
            if (_rider.IsMounted)
                _rider.Follow(Eye(), -AngleDegrees * viewLean);
        }

        private void Step(float h, float pumpInput)
        {
            float deg = Mathf.Abs(_theta) * Mathf.Rad2Deg;
            float acc = -(gravity / _length) * Mathf.Sin(_theta) - (_rider.IsMounted ? riddenDamping : emptyDamping) * _omega;

            if (Mathf.Abs(pumpInput) > 0.1f)
            {
                float dir = Mathf.Sign(pumpInput);
                bool withMotion = _omega * dir >= -0.05f;
                float fade = 1f - Mathf.InverseLerp(pumpFadeStart, pumpFadeEnd, deg);
                acc += dir * pumpAcceleration * fade * (withMotion ? 1f : counterPumpShare);
            }

            // Brake an outward swing past the soft limit (harder the further out).
            if (deg > softLimit && _theta * _omega > 0f)
                acc -= _omega * 5f * Mathf.InverseLerp(softLimit, hardLimit, deg);

            _omega += acc * h;
            _theta += _omega * h;

            float hard = hardLimit * Mathf.Deg2Rad;
            if (Mathf.Abs(_theta) > hard)
            {
                _theta = Mathf.Sign(_theta) * hard;
                _omega = 0f;
            }
        }

        private void ApplyPose()
        {
            if (pivot != null)
                pivot.localRotation = Quaternion.Euler(-_theta * Mathf.Rad2Deg, 0f, 0f);
        }

        // Eye above the seat along the ropes (the rider leans with the swing).
        private Vector3 Eye()
        {
            Vector3 up = pivot != null ? (pivot.position - seatPoint.position).normalized : Vector3.up;
            return seatPoint.position + up * eyeAboveSeat;
        }

        private void DismountPlayer()
        {
            Vector3 feet = exitPoint != null ? exitPoint.position : transform.position;
            float yaw = exitPoint != null ? exitPoint.eulerAngles.y : transform.eulerAngles.y;
            _rider.Dismount(feet, yaw);
            _occupant = null;
        }

        private void OnGUI()
        {
            if (_rider.IsMounted)
                PlaygroundRider.DrawHint(rideHint);
        }
    }
}
