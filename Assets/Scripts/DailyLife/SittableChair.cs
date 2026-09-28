using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A chair the player sits on with the Interact key (routed by <c>PlayerInteractor</c> through
    /// <see cref="IUsable"/>) and gets up from with the movement keys.
    ///
    /// Sitting turns off PlayerMovement and the CharacterController, moves the player so the camera sits
    /// at <see cref="sitPoint"/>, and turns the view to sitPoint's forward with <see cref="sitPitch"/> down.
    /// Mouse look stays live, and Interact stays the ordinary aimed interaction (pickup / place / use) -
    /// it never stands up. Pushing Move (WASD) stands up; a Move key already held when sitting down must be
    /// released first, and nothing happens while the player's controls are locked (PlayerInteractor off).
    /// Standing puts the player's feet at <see cref="standPoint"/> and turns movement back on.
    ///
    /// Only one chair can be occupied; while seated, no chair is a World Use target (<see cref="CanUse"/>).
    /// A held item stays in hand (it rides the camera's hold anchor).
    /// </summary>
    public class SittableChair : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("Poses")]
        [Tooltip("Where the camera (eye) goes while seated; its forward is the seated view direction.")]
        [SerializeField] private Transform sitPoint;
        [Tooltip("Where the player's feet go on standing up - clear of the chair and the table.")]
        [SerializeField] private Transform standPoint;
        [Tooltip("Initial downward look while seated, degrees.")]
        [SerializeField] private float sitPitch = 25f;
        [Tooltip("Label anchor while seated (the chair itself is usually out of view). Falls back to this transform.")]
        [SerializeField] private Transform seatedLabelAnchor;

        [Header("Focus label")]
        [SerializeField] private string sitPrompt = "앉기";
        [Tooltip("Short HUD notice on sitting down - shown once per play session.")]
        [SerializeField] private string standHint = "이동키로 일어서기";

        [Header("Player (found in the scene if empty)")]
        [SerializeField] private PlayerInteractor player;

        private const float MoveThreshold = 0.1f;

        private static SittableChair s_occupied;
        private static bool s_standHintShown;

        private CharacterController _controller;
        private PlayerMovement _movement;
        private PlayerLook _look;
        private InputAction _move;
        private bool _moveReleasedSinceSit;

        /// <summary>The chair the player is sitting on, or null.</summary>
        public static SittableChair Occupied => s_occupied;
        public bool IsSeated => s_occupied == this;

        /// <summary>Only an empty chair while standing is a World Use target; seated, every chair is a plain surface for the aim.</summary>
        public bool CanUse => s_occupied == null;

        public string FocusName => s_occupied != null ? "" : sitPrompt;
        public Transform FocusTransform => IsSeated && seatedLabelAnchor != null ? seatedLabelAnchor : transform;

        public void SetFocused(bool focused) { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_occupied = null;
            s_standHintShown = false;
        }

        private void Awake()
        {
            if (player == null)
                player = FindFirstObjectByType<PlayerInteractor>();
            if (player != null)
            {
                _controller = player.GetComponent<CharacterController>();
                _movement = player.GetComponent<PlayerMovement>();
                _look = player.GetComponent<PlayerLook>();
            }
            if (_movement != null && _movement.InputActions != null)
                _move = _movement.InputActions.FindActionMap("Player", throwIfNotFound: false)?.FindAction("Move", throwIfNotFound: false);
        }

        private void Update()
        {
            if (!IsSeated || _move == null)
                return;
            // Controls locked (dialogue): PlayerControlLock switches the interactor off.
            if (player == null || !player.isActiveAndEnabled)
                return;

            bool pushing = _move.ReadValue<Vector2>().sqrMagnitude > MoveThreshold * MoveThreshold;
            if (!pushing)
                _moveReleasedSinceSit = true;
            else if (_moveReleasedSinceSit)
                StandUp();
        }

        private void OnDisable()
        {
            if (IsSeated)
                StandUp();
        }

        public void Use()
        {
            if (s_occupied == null)
                SitDown();
        }

        private void SitDown()
        {
            if (player == null || sitPoint == null)
                return;

            // Camera height above the player's feet right now (standing or crouched).
            Camera cam = Camera.main;
            float eyeOffset = cam != null ? cam.transform.position.y - player.transform.position.y : 1.6f;

            if (_movement != null) _movement.enabled = false;
            if (_controller != null) _controller.enabled = false;
            // PlayerMovement's OnDisable switched Move off; read it here to stand up. Its OnEnable takes it back.
            _move?.Enable();
            _moveReleasedSinceSit = false;

            Vector3 eye = sitPoint.position;
            player.transform.position = new Vector3(eye.x, eye.y - eyeOffset, eye.z);
            if (_look != null)
                _look.SetLookAngles(sitPoint.eulerAngles.y, sitPitch);

            // The chair's box reaches the backrest top, just under the seated eye - it would catch the
            // aim meant for the table. The player's controller is off while seated, so it isn't needed.
            SetChairCollider(false);

            s_occupied = this;

            if (!s_standHintShown && !string.IsNullOrEmpty(standHint))
            {
                var hud = FindFirstObjectByType<ObjectiveHUD>();
                if (hud != null)
                {
                    hud.ShowNotice(standHint);
                    s_standHintShown = true;
                }
            }
        }

        private void StandUp()
        {
            if (player != null)
            {
                if (standPoint != null)
                    player.transform.position = standPoint.position;
                if (_controller != null) _controller.enabled = true;
                if (_movement != null) _movement.enabled = true;
                if (_look != null)
                    _look.SetLookAngles(player.transform.eulerAngles.y, 0f);
            }
            SetChairCollider(true);
            if (s_occupied == this)
                s_occupied = null;
        }

        private void SetChairCollider(bool on)
        {
            foreach (var col in GetComponents<Collider>())
                col.enabled = on;
        }
    }
}
