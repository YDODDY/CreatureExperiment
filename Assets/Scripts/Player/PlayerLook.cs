using UnityEngine;
using UnityEngine.InputSystem;

namespace CreatureExperiment.Player
{
    /// <summary>
    /// First-person mouse look. Yaw rotates the player body, pitch rotates the camera holder.
    /// Reads the "Look" action from the shared Input System asset.
    /// </summary>
    public class PlayerLook : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions;

        [Header("References")]
        [Tooltip("Transform that pivots vertically (pitch). Usually the CameraHolder child.")]
        [SerializeField] private Transform cameraHolder;

        [Header("Settings")]
        [Tooltip("Degrees of rotation per pixel of pointer delta.")]
        [SerializeField] private float sensitivity = 0.1f;
        [SerializeField] private float minPitch = -89f;
        [SerializeField] private float maxPitch = 89f;
        [SerializeField] private bool lockCursor = true;

        private InputAction _lookAction;
        private float _yaw;
        private float _pitch;

        // --- TEMPORARY DEBUG (camera jitter investigation) ------------------------------------------
        // Read-only seams for PlayerCameraJitterDebug, all captured INLINE in Update() at the exact
        // instant each value exists - no external component has to guess or re-derive them via
        // execution order. Delete this whole block + PlayerCameraJitterDebug once the jitter root
        // cause is found and fixed. Nothing here changes behaviour or reads-back-then-reassigns
        // anything; every Debug* value is a plain snapshot of a value already being computed below.
        /// <summary>TEMPORARY DEBUG: the raw Look action value read this Update, before sensitivity is applied.</summary>
        public Vector2 DebugRawLookInput { get; private set; }
        /// <summary>TEMPORARY DEBUG: device + control path that is actually driving the Look action's value this frame (InputAction.activeControl), or "none" if nothing is currently actuating it.</summary>
        public string DebugActiveControlPath { get; private set; }
        /// <summary>TEMPORARY DEBUG: _yaw at the START of this Update, before delta.x * sensitivity is added.</summary>
        public float DebugYawBefore { get; private set; }
        /// <summary>TEMPORARY DEBUG: _yaw AFTER delta.x * sensitivity was added this frame - same value used to build the target rotation below.</summary>
        public float DebugYawAfter { get; private set; }
        /// <summary>TEMPORARY DEBUG: DebugYawAfter - DebugYawBefore.</summary>
        public float DebugYawDelta => DebugYawAfter - DebugYawBefore;
        /// <summary>TEMPORARY DEBUG: _pitch at the START of this Update, before delta.y * sensitivity is applied.</summary>
        public float DebugPitchBefore { get; private set; }
        /// <summary>TEMPORARY DEBUG: _pitch AFTER this frame's clamp/delta - same value used to build cameraHolder's target rotation.</summary>
        public float DebugPitchAfter { get; private set; }
        /// <summary>TEMPORARY DEBUG: DebugPitchAfter - DebugPitchBefore.</summary>
        public float DebugPitchDelta => DebugPitchAfter - DebugPitchBefore;
        /// <summary>TEMPORARY DEBUG: transform.localRotation read BEFORE this frame's assignment (i.e. whatever the end of last frame left it at).</summary>
        public Quaternion DebugLocalRotationBeforeWrite { get; private set; }
        /// <summary>TEMPORARY DEBUG: the exact Quaternion.Euler(0, _yaw, 0) computed this frame, before it is assigned to transform.localRotation.</summary>
        public Quaternion DebugTargetRotation { get; private set; }
        /// <summary>TEMPORARY DEBUG: transform.localRotation read back IMMEDIATELY after this component assigned it this frame.</summary>
        public Quaternion DebugLocalRotationAfterWrite { get; private set; }
        // ----------------------------------------------------------------------------------------------

        private void Awake()
        {
            var playerMap = inputActions.FindActionMap("Player", throwIfNotFound: true);
            _lookAction = playerMap.FindAction("Look", throwIfNotFound: true);

            Vector3 startEuler = transform.eulerAngles;
            _yaw = startEuler.y;
            if (cameraHolder != null)
                _pitch = NormalizePitch(cameraHolder.localEulerAngles.x);
        }

        private void OnEnable() => _lookAction?.Enable();

        private void OnDisable() => _lookAction?.Disable();

        private void Start()
        {
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Update()
        {
            Vector2 delta = _lookAction.ReadValue<Vector2>();
            DebugRawLookInput = delta; // TEMPORARY DEBUG
            var activeControl = _lookAction.activeControl; // TEMPORARY DEBUG
            DebugActiveControlPath = activeControl != null
                ? $"{activeControl.device.displayName} / {activeControl.path}"
                : "none";

            DebugYawBefore = _yaw;     // TEMPORARY DEBUG
            DebugPitchBefore = _pitch; // TEMPORARY DEBUG

            _yaw += delta.x * sensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, minPitch, maxPitch);

            DebugYawAfter = _yaw;     // TEMPORARY DEBUG
            DebugPitchAfter = _pitch; // TEMPORARY DEBUG

            DebugLocalRotationBeforeWrite = transform.localRotation; // TEMPORARY DEBUG - captured right before the write below
            Quaternion targetRotation = Quaternion.Euler(0f, _yaw, 0f);
            DebugTargetRotation = targetRotation; // TEMPORARY DEBUG

            transform.localRotation = targetRotation;
            if (cameraHolder != null)
                cameraHolder.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            DebugLocalRotationAfterWrite = transform.localRotation; // TEMPORARY DEBUG - read back immediately after the write above
        }

        private static float NormalizePitch(float angle)
        {
            if (angle > 180f) angle -= 360f;
            return angle;
        }
    }
}
