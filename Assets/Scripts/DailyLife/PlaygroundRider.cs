using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The player's side of a playground ride (<see cref="SeesawRide"/>, <see cref="SwingRide"/>) - one instance per ride,
    /// at most one mounted at a time (<see cref="PlayerRiding"/>).
    ///
    /// Mounting works like hiding in a locker: <see cref="PlayerControlLock"/> (movement / interactor / activator / eater
    /// off) with the mouse look switched back on, the CharacterController off, and the Move + Jump actions enabled here so
    /// the ride can read them as ride input (Space = seesaw push, W/S = swing pump) - the player never walks or jumps
    /// while riding. Each frame the ride hands over its seat's eye point (<see cref="Follow"/>); the body is moved so the
    /// camera sits there, and an optional view pitch offset leans the camera with the motion (restored on dismount).
    /// Dismount puts the feet on the ground under the ride's exit point (never in the ground or the air) and gives
    /// every control back - no ride velocity is carried over.
    /// </summary>
    public sealed class PlaygroundRider
    {
        private const float InputThreshold = 0.1f;
        private const float MinRideTime = 0.25f;

        private static PlaygroundRider s_current;

        /// <summary>True while the player is on any playground ride.</summary>
        public static bool PlayerRiding => s_current != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_current = null;

        private PlayerInteractor _player;
        private CharacterController _controller;
        private PlayerLook _look;
        private InputAction _move;
        private InputAction _jump;
        private Transform _camera;
        private Quaternion _cameraRest;
        private PlayerControlLock _lock;
        private float _eyeOffset;
        private float _mountedAt;
        private bool _exitReleased;

        public bool IsMounted => s_current == this;
        public Vector2 Move => IsMounted && _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
        public bool JumpPressed => IsMounted && _jump != null && _jump.WasPressedThisFrame();

        /// <summary>The player can get on a ride: present, controls not locked (no dialogue), not on a chair / ride / in a locker.</summary>
        public static bool PlayerAvailable(PlayerInteractor player)
            => player != null && player.isActiveAndEnabled && s_current == null
               && SittableChair.Occupied == null && !HideableLocker.PlayerHidden;

        public bool Mount(PlayerInteractor player, Vector3 eye, float yaw, float pitch)
        {
            if (!PlayerAvailable(player))
                return false;
            _player = player;
            _controller = player.GetComponent<CharacterController>();
            _look = player.GetComponent<PlayerLook>();
            var movement = player.GetComponent<PlayerMovement>();
            if (_move == null && movement != null && movement.InputActions != null)
            {
                var map = movement.InputActions.FindActionMap("Player", throwIfNotFound: false);
                _move = map?.FindAction("Move", throwIfNotFound: false);
                _jump = map?.FindAction("Jump", throwIfNotFound: false);
            }

            Camera cam = Camera.main;
            _camera = cam != null ? cam.transform : null;
            _eyeOffset = cam != null ? cam.transform.position.y - player.transform.position.y : 1.6f;
            if (_camera != null)
                _cameraRest = _camera.localRotation;

            _lock = PlayerControlLock.Acquire(player.gameObject);
            if (_look != null)
                _look.enabled = true; // looking around stays
            if (_controller != null)
                _controller.enabled = false;
            // PlayerMovement is off (and switched these off); the ride reads them. Its OnEnable takes them back.
            _move?.Enable();
            _jump?.Enable();

            s_current = this;
            _mountedAt = Time.time;
            _exitReleased = false;
            Follow(eye, 0f);
            _look?.SetLookAngles(yaw, pitch);
            return true;
        }

        /// <summary>Put the camera at <paramref name="eye"/>; <paramref name="viewPitch"/> leans the view (degrees, + = down).</summary>
        public void Follow(Vector3 eye, float viewPitch)
        {
            if (!IsMounted || _player == null)
                return;
            _player.transform.position = eye - Vector3.up * _eyeOffset;
            if (_camera != null)
                _camera.localRotation = _cameraRest * Quaternion.Euler(viewPitch, 0f, 0f);
        }

        /// <summary>
        /// The movement keys ask to get off: <paramref name="sidewaysOnly"/> = only A/D count (W/S are swing input).
        /// A key already held when getting on must be released first.
        /// </summary>
        public bool WantsExit(bool sidewaysOnly)
        {
            if (!IsMounted)
                return false;
            Vector2 m = Move;
            float push = sidewaysOnly ? Mathf.Abs(m.x) : m.magnitude;
            if (push < InputThreshold)
            {
                _exitReleased = true;
                return false;
            }
            return _exitReleased && Time.time > _mountedAt + MinRideTime;
        }

        public void Dismount(Vector3 feet, float yaw)
        {
            if (!IsMounted)
                return;
            if (_camera != null)
                _camera.localRotation = _cameraRest;
            if (_player != null)
                _player.transform.position = GroundUnder(feet); // controller still off - it can't be hit by the ray
            if (_controller != null)
                _controller.enabled = true;
            _lock?.Release();
            _lock = null;
            _look?.SetLookAngles(yaw, 0f);
            s_current = null;
        }

        private static Vector3 GroundUnder(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.02f;
            return p;
        }

        private static GUIStyle s_hintStyle;

        /// <summary>The ride's key hint under the crosshair (OnGUI).</summary>
        public static void DrawHint(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (s_hintStyle == null)
            {
                s_hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                s_hintStyle.normal.textColor = new Color(1f, 0.92f, 0.6f);
            }
            GUI.Label(new Rect(0f, Screen.height * 0.62f, Screen.width, 26f), text, s_hintStyle);
        }
    }
}
