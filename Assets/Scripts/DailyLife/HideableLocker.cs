using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A locker the player can hide in (pub backroom, fast-food changing room). Its door is an ordinary
    /// <see cref="SwingDoor"/> (E opens / closes it). This component is the locker's inside (one collider filling the
    /// cabinet - only reachable by the aim while the door is open):
    /// - E (World Use) on the open inside: closes the door. Label "E · 문 닫기 / LMB · 숨기".
    /// - Left Click on the open inside (<see cref="IAimedPrimaryAction"/>, ahead of any held-item action): hide.
    ///   The player's controls are locked like a dialogue (<see cref="PlayerControlLock"/>) except the mouse look,
    ///   which stays live but clamped around the locker's facing; the CharacterController is off; the view sits at
    ///   <see cref="cameraPoint"/> (eye level with the door's vent slit) and the door closes.
    /// - Left Click while hidden (read here - the interactor is off): the door opens, the player steps out at
    ///   <see cref="exitPoint"/>, controls come back; the door stays open.
    /// No scale change, nothing taken from the inventory: a held item stays in the hand, only its renderers are hidden
    /// while inside (<see cref="PlayerInteractor.SetHeldVisualVisible"/>).
    /// For later creature work: <see cref="IsOccupied"/>, <see cref="IsDoorOpen"/>, <see cref="PlayerHidden"/>.
    /// </summary>
    public class HideableLocker : MonoBehaviour, IUsable, IFocusTarget, IAimedPrimaryAction
    {
        [SerializeField] private SwingDoor door;
        [Tooltip("Eye position while hidden; its forward is the view's centre (out through the vent slit).")]
        [SerializeField] private Transform cameraPoint;
        [Tooltip("Where the player's feet go on the way out (clear of the door's swing); its forward = view on exit.")]
        [SerializeField] private Transform exitPoint;
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private PlayerInteractor player;

        [Header("View while hidden")]
        [SerializeField] private float yawLimit = 55f;
        [SerializeField] private float minPitch = -25f;
        [SerializeField] private float maxPitch = 30f;

        [Header("Labels")]
        [SerializeField] private string closeDoorPrompt = "E · 문 닫기";
        [SerializeField] private string hidePrompt = "LMB · 숨기";
        [SerializeField] private string exitHint = "LMB · 나가기";

        private static HideableLocker s_current;

        private PlayerControlLock _lock;
        private CharacterController _controller;
        private PlayerLook _look;
        private InputAction _attack;
        private float _enteredAt;
        private bool _exiting;
        private bool _openStarted;
        private float _exitStarted;
        private GUIStyle _hintStyle;

        /// <summary>The locker the player is hiding in, or null.</summary>
        public static HideableLocker Current => s_current;
        public static bool PlayerHidden => s_current != null;
        public bool IsOccupied => s_current == this;
        public bool IsDoorOpen => door != null && door.IsOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_current = null;

        // --- IUsable (E on the open inside = close the door)
        public bool CanUse => !IsOccupied && door != null && door.IsOpen && !door.IsMoving;
        public void Use()
        {
            if (CanUse)
                door.Use();
        }

        // --- IFocusTarget
        public string FocusName => !IsOccupied && IsDoorOpen && !door.IsMoving ? $"{closeDoorPrompt}\n{hidePrompt}" : "";
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused) { }

        // --- IAimedPrimaryAction (Left Click on the open inside = hide)
        public bool PrimaryEnabled => s_current == null && IsDoorOpen && !door.IsMoving;
        public string PrimaryHint => hidePrompt;
        public bool OverridesHeldItem => true;

        public bool TryAimedPrimary()
        {
            if (!PrimaryEnabled || player == null || cameraPoint == null)
                return false;
            Enter();
            return true;
        }

        private void Awake()
        {
            if (player == null)
                player = FindFirstObjectByType<PlayerInteractor>();
            if (player != null)
            {
                _controller = player.GetComponent<CharacterController>();
                _look = player.GetComponent<PlayerLook>();
                var movement = player.GetComponent<PlayerMovement>();
                if (movement != null && movement.InputActions != null)
                    _attack = movement.InputActions.FindActionMap("Player", throwIfNotFound: false)?.FindAction("Attack", throwIfNotFound: false);
            }
        }

        private void Enter()
        {
            Camera cam = Camera.main;
            float eyeOffset = cam != null ? cam.transform.position.y - player.transform.position.y : 1.6f;

            _lock = PlayerControlLock.Acquire(player.gameObject);
            if (_look != null)
                _look.enabled = true; // looking around stays (clamped below)
            if (_controller != null)
                _controller.enabled = false;
            _attack?.Enable(); // the lock switched the eater (and its Left Click) off; read it here to get out

            Vector3 eye = cameraPoint.position;
            player.transform.position = new Vector3(eye.x, eye.y - eyeOffset, eye.z);
            _look?.SetLookAngles(cameraPoint.eulerAngles.y, 0f);

            player.SetHeldVisualVisible(false); // the held item would poke out in front of the eye; state untouched

            s_current = this;
            _enteredAt = Time.time;
            _exiting = false;
            if (door.IsOpen && !door.IsMoving)
                door.Use(); // close behind
        }

        private void Update()
        {
            if (!IsOccupied)
                return;
            if (!_exiting && _attack != null && _attack.WasPressedThisFrame() && Time.time > _enteredAt + 0.3f)
            {
                _exiting = true;
                _openStarted = false;
                _exitStarted = Time.time;
            }
            if (!_exiting)
                return;
            // Open the door, then step out once it is swinging open (or open). A door still closing behind the player
            // finishes first (never step out through a closing door); a refused open is retried.
            if (!door.IsOpen && !door.IsMoving)
            {
                door.Use();
                _openStarted = door.IsMoving;
            }
            // (SwingDoor still reports IsOpen during a closing swing - "open" only counts at rest.)
            if ((door.IsOpen && !door.IsMoving) || _openStarted || Time.time > _exitStarted + 3f)
                FinishExit();
        }

        private void LateUpdate()
        {
            if (!IsOccupied || _look == null || cameraPoint == null)
                return;
            float centre = cameraPoint.eulerAngles.y;
            float yaw = _look.Yaw, pitch = _look.Pitch;
            float clampedYaw = centre + Mathf.Clamp(Mathf.DeltaAngle(centre, yaw), -yawLimit, yawLimit);
            float clampedPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            if (!Mathf.Approximately(clampedYaw, yaw) || !Mathf.Approximately(clampedPitch, pitch))
                _look.SetLookAngles(clampedYaw, clampedPitch);
        }

        private void FinishExit()
        {
            if (player != null && exitPoint != null)
                player.transform.position = exitPoint.position;
            if (_controller != null)
                _controller.enabled = true;
            player?.SetHeldVisualVisible(true);
            _lock?.Release();
            _lock = null;
            _look?.SetLookAngles(exitPoint != null ? exitPoint.eulerAngles.y : player.transform.eulerAngles.y, 0f);
            if (s_current == this)
                s_current = null;
            _exiting = false;
        }

        private void OnDisable()
        {
            if (IsOccupied)
                FinishExit();
        }

        private void OnGUI()
        {
            if (!IsOccupied || string.IsNullOrEmpty(exitHint))
                return;
            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _hintStyle.normal.textColor = new Color(1f, 0.92f, 0.6f);
            }
            GUI.Label(new Rect(0f, Screen.height * 0.62f, Screen.width, 26f), exitHint, _hintStyle);
        }
    }
}
