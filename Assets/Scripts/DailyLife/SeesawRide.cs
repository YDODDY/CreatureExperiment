using UnityEngine;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A playground seesaw. The beam (<see cref="pivot"/>) tilts about its local Z axis; side A is the beam's local +X end,
    /// side B the -X end. Not a Rigidbody joint - a small angular simulation that can't blow up:
    /// - each occupied seat pulls its end down (constant "rider weight" acceleration, A minus B), plus damping;
    /// - the beam stops at ±<see cref="limitAngle"/> (an end on its bumper) and bounces back a little (<see cref="restitution"/>);
    /// - <see cref="Push"/> = a rider's feet kicking off the ground: only while that end is down (within
    ///   <see cref="pushWindow"/> of its bumper) and not again within <see cref="pushCooldown"/> - mashing Space can't pump
    ///   energy in mid-air. The end flies up, the other end slams down, and the rider up top lifts off the seat a bit
    ///   (a "hop" offset on the seated eye, falling back under gravity).
    /// The player gets on through a <see cref="SeesawSeat"/> on either seat (E), pushes with Space and gets off with the
    /// movement keys at that side's exit point (<see cref="PlaygroundRider"/>).
    /// Seats are tracked per side (<see cref="Occupant"/>, <see cref="TryClaim"/>), so a later NPC / creature can sit on
    /// the other end and call <see cref="Push"/> - nothing uses that yet.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SeesawRide : MonoBehaviour
    {
        public const int SideA = 0;
        public const int SideB = 1;

        [Header("Parts")]
        [Tooltip("Rotates about its local Z; side A = local +X end.")]
        [SerializeField] private Transform pivot;
        [Tooltip("Seat surface points (children of the beam), side A then B.")]
        [SerializeField] private Transform seatPointA;
        [SerializeField] private Transform seatPointB;
        [Tooltip("Where the feet go when getting off (beside each seat, on the ground).")]
        [SerializeField] private Transform exitPointA;
        [SerializeField] private Transform exitPointB;
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private PlayerInteractor player;

        [Header("Motion (degrees)")]
        [SerializeField] private float limitAngle = 22f;
        [Tooltip("Angle at start; + = side A down.")]
        [SerializeField] private float startAngle = -22f;
        [Tooltip("Angular acceleration per rider of difference, deg/s².")]
        [SerializeField] private float riderWeight = 320f;
        [SerializeField] private float riddenDamping = 0.3f;
        [SerializeField] private float emptyDamping = 2.5f;
        [Tooltip("Speed a feet push gives the beam, deg/s.")]
        [SerializeField] private float pushSpeed = 235f;
        [Tooltip("A push works only while the end is this close to its bumper, degrees.")]
        [SerializeField] private float pushWindow = 7f;
        [SerializeField] private float pushCooldown = 0.3f;
        [Range(0f, 1f)]
        [SerializeField] private float restitution = 0.28f;

        [Header("Rider")]
        [SerializeField] private float eyeAboveSeat = 0.78f;
        [Tooltip("How much of the seat's speed at a hard stop turns into the top rider's lift-off speed.")]
        [SerializeField] private float hopFactor = 0.55f;
        [SerializeField] private float maxHopSpeed = 2.0f;

        [Header("Labels")]
        [SerializeField] private string sitPrompt = "E · 시소 타기";
        [SerializeField] private string rideHint = "Space · 발 구르기     이동키 · 내리기";

        private readonly object[] _occupants = new object[2];
        private readonly float[] _hop = new float[2];
        private readonly float[] _hopVel = new float[2];
        private readonly float[] _lastPush = { -10f, -10f };
        private readonly PlaygroundRider _rider = new PlaygroundRider();
        private float _angle;
        private float _velocity;
        private int _playerSide = -1;

        /// <summary>Beam angle, degrees; + = side A down.</summary>
        public float Angle => _angle;
        public string SitPrompt => sitPrompt;
        public bool PlayerSeated => _playerSide >= 0;
        public object Occupant(int side) => _occupants[side];

        private void Awake()
        {
            if (player == null)
                player = FindFirstObjectByType<PlayerInteractor>();
            _angle = Mathf.Clamp(startAngle, -limitAngle, limitAngle);
            ApplyPose();
        }

        private void OnDisable()
        {
            if (_playerSide >= 0)
                DismountPlayer();
        }

        // --- Seats ------------------------------------------------------------------------------------------

        public bool CanSeatPlayer(int side) => _occupants[side] == null && PlaygroundRider.PlayerAvailable(player);

        public void SeatPlayer(int side)
        {
            if (!CanSeatPlayer(side))
                return;
            Transform seat = SeatPoint(side);
            Vector3 toCentre = pivot.position - seat.position;
            toCentre.y = 0f;
            float yaw = Quaternion.LookRotation(toCentre.sqrMagnitude > 0.001f ? toCentre : transform.forward).eulerAngles.y;
            if (!_rider.Mount(player, Eye(side), yaw, 12f))
                return;
            _occupants[side] = player;
            _playerSide = side;
            _hop[side] = 0f;
            _hopVel[side] = 0f;
        }

        /// <summary>Seat a non-player rider (future NPC / creature). False if that seat is taken.</summary>
        public bool TryClaim(int side, object rider)
        {
            if (rider == null || _occupants[side] != null)
                return false;
            _occupants[side] = rider;
            return true;
        }

        public void Release(int side, object rider)
        {
            if (_occupants[side] == rider && side != _playerSide)
                _occupants[side] = null;
        }

        /// <summary>A feet push from the ground by the rider on <paramref name="side"/>. False when that end isn't down yet / cooling down.</summary>
        public bool Push(int side)
        {
            if (_occupants[side] == null || Time.time < _lastPush[side] + pushCooldown)
                return false;
            float sign = side == SideA ? 1f : -1f;
            if (_angle * sign < limitAngle - pushWindow)
                return false;
            _lastPush[side] = Time.time;
            _velocity = -sign * pushSpeed; // this end goes up
            return true;
        }

        // --- Loop -------------------------------------------------------------------------------------------

        private void Update()
        {
            if (_playerSide >= 0)
            {
                if (_rider.JumpPressed)
                    Push(_playerSide);
                if (_rider.WantsExit(sidewaysOnly: false))
                    DismountPlayer();
            }

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.005f));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
                Step(h);
            for (int s = 0; s < 2; s++)
            {
                if (_hop[s] <= 0f && _hopVel[s] <= 0f)
                    continue;
                _hopVel[s] -= 9.81f * dt;
                _hop[s] += _hopVel[s] * dt;
                if (_hop[s] < 0f) { _hop[s] = 0f; _hopVel[s] = 0f; }
            }
            ApplyPose();
        }

        private void LateUpdate()
        {
            if (_playerSide >= 0)
                _rider.Follow(Eye(_playerSide), 0f);
        }

        private void Step(float h)
        {
            int weight = (_occupants[SideA] != null ? 1 : 0) - (_occupants[SideB] != null ? 1 : 0);
            bool anyone = _occupants[SideA] != null || _occupants[SideB] != null;
            float acc = riderWeight * weight - (anyone ? riddenDamping : emptyDamping) * _velocity;
            _velocity += acc * h;
            _angle += _velocity * h;

            if (_angle > limitAngle)
                Stop(limitAngle, SideA);
            else if (_angle < -limitAngle)
                Stop(-limitAngle, SideB);
        }

        // The end of groundSide hit its bumper: bounce back a little, the rider at the other (top) end lifts off.
        private void Stop(float angle, int groundSide)
        {
            _angle = angle;
            float impact = Mathf.Abs(_velocity);
            bool intoBumper = groundSide == SideA ? _velocity > 0f : _velocity < 0f;
            if (!intoBumper)
                return;
            _velocity = -_velocity * restitution;
            if (Mathf.Abs(_velocity) < 15f)
                _velocity = 0f;
            int top = 1 - groundSide;
            if (impact > 40f)
            {
                float arm = Vector3.Distance(pivot.position, SeatPoint(top).position);
                _hopVel[top] = Mathf.Max(_hopVel[top], Mathf.Min(impact * Mathf.Deg2Rad * arm * hopFactor, maxHopSpeed));
            }
        }

        private void ApplyPose()
        {
            if (pivot != null)
                pivot.localRotation = Quaternion.Euler(0f, 0f, -_angle);
        }

        private void DismountPlayer()
        {
            int side = _playerSide;
            Transform exit = side == SideA ? exitPointA : exitPointB;
            Vector3 feet = exit != null ? exit.position : SeatPoint(side).position;
            float yaw = exit != null ? exit.eulerAngles.y : transform.eulerAngles.y;
            _rider.Dismount(feet, yaw);
            _occupants[side] = null;
            _playerSide = -1;
        }

        private Transform SeatPoint(int side) => side == SideA ? seatPointA : seatPointB;
        private Vector3 Eye(int side) => SeatPoint(side).position + Vector3.up * (eyeAboveSeat + _hop[side]);

        private void OnGUI()
        {
            if (_playerSide >= 0)
                PlaygroundRider.DrawHint(rideHint);
        }
    }
}
