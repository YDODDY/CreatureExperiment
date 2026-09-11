using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatureExperiment.Player
{
    /// <summary>
    /// First-person movement on a CharacterController: walk / sprint / jump / hold-to-crouch.
    /// Reads Move, Jump, Sprint and Crouch from the shared Input System asset.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("References")]
        [Tooltip("Camera holder that is lowered while crouching.")]
        [SerializeField] private Transform cameraHolder;

        [Header("Speeds (m/s)")]
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float crouchSpeed = 2f;

        [Header("Jump / Gravity")]
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -19.62f;

        [Header("Stand / Crouch")]
        [SerializeField] private float standHeight = 1.8f;
        [SerializeField] private float crouchHeight = 1.0f;
        [SerializeField] private float standCameraY = 1.6f;
        [SerializeField] private float crouchCameraY = 0.9f;
        [SerializeField] private float crouchLerpSpeed = 10f;
        [Tooltip("Layers that block standing back up. Player's own layer should be excluded.")]
        [SerializeField] private LayerMask ceilingMask = ~0;

        private CharacterController _controller;
        private InputAction _moveAction;
        private InputAction _jumpAction;
        private InputAction _sprintAction;
        private InputAction _crouchAction;

        private Vector3 _velocity;
        private bool _isCrouching;

        /// <summary>True while the Player is in the crouched movement state (Crouch held and not currently blocked from standing). Read-only seam for other systems (e.g. Hearing 0.1's movement sound emitter) - crouch logic itself is unchanged.</summary>
        public bool IsCrouching => _isCrouching;

        /// <summary>True while the Sprint action is held. This project has no separate "Dash" action - Sprint IS the fast-movement tier; see PlayerMovementSoundEmitter for how Hearing 0.1 treats this as its Dash tier.</summary>
        public bool IsSprinting => _sprintAction != null && _sprintAction.IsPressed();

        /// <summary>True for exactly the one Update() frame a jump was actually triggered (mirrors the InputAction.WasPressedThisFrame one-frame-pulse idiom already used below) - a one-shot edge for "a jump just happened", not a continuous "is airborne" state. Movement itself is unaffected by this property; it only mirrors the existing trigger condition for read-only seams.</summary>
        public bool JumpedThisFrame { get; private set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            var playerMap = inputActions.FindActionMap("Player", throwIfNotFound: true);
            _moveAction = playerMap.FindAction("Move", throwIfNotFound: true);
            _jumpAction = playerMap.FindAction("Jump", throwIfNotFound: true);
            _sprintAction = playerMap.FindAction("Sprint", throwIfNotFound: true);
            _crouchAction = playerMap.FindAction("Crouch", throwIfNotFound: true);

            SetHeight(standHeight);
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
            _jumpAction?.Enable();
            _sprintAction?.Enable();
            _crouchAction?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _jumpAction?.Disable();
            _sprintAction?.Disable();
            _crouchAction?.Disable();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            JumpedThisFrame = false; // reset each frame; set true below only if a jump actually triggers this frame

            UpdateCrouchState();
            UpdateHeight(dt);

            Vector2 input = _moveAction.ReadValue<Vector2>();
            Vector3 move = transform.right * input.x + transform.forward * input.y;
            if (move.sqrMagnitude > 1f)
                move.Normalize();

            float speed = _isCrouching
                ? crouchSpeed
                : (_sprintAction.IsPressed() ? sprintSpeed : walkSpeed);

            bool grounded = _controller.isGrounded;
            if (grounded && _velocity.y < 0f)
                _velocity.y = -2f;

            if (grounded && !_isCrouching && _jumpAction.WasPressedThisFrame())
            {
                _velocity.y = Mathf.Sqrt(-2f * gravity * jumpHeight);
                JumpedThisFrame = true;
            }

            _velocity.y += gravity * dt;

            Vector3 motion = move * speed + Vector3.up * _velocity.y;
            _controller.Move(motion * dt);
        }

        private void UpdateCrouchState()
        {
            bool wantsCrouch = _crouchAction.IsPressed();

            if (wantsCrouch)
                _isCrouching = true;
            else if (_isCrouching && CanStandUp())
                _isCrouching = false;
        }

        private bool CanStandUp()
        {
            // Sphere where the head would be when standing; blocked => stay crouched.
            float radius = _controller.radius - 0.05f;
            Vector3 headWhenStanding = transform.position + Vector3.up * (standHeight - _controller.radius);
            return !Physics.CheckSphere(headWhenStanding, radius, ceilingMask, QueryTriggerInteraction.Ignore);
        }

        private void UpdateHeight(float dt)
        {
            float targetHeight = _isCrouching ? crouchHeight : standHeight;
            float newHeight = Mathf.MoveTowards(_controller.height, targetHeight, crouchLerpSpeed * dt);
            SetHeight(newHeight);

            if (cameraHolder != null)
            {
                float t = Mathf.InverseLerp(crouchHeight, standHeight, newHeight);
                float camY = Mathf.Lerp(crouchCameraY, standCameraY, t);
                Vector3 local = cameraHolder.localPosition;
                cameraHolder.localPosition = new Vector3(local.x, camY, local.z);
            }
        }

        private void SetHeight(float height)
        {
            _controller.height = height;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
        }
    }
}
