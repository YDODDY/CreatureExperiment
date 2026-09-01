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

            _yaw += delta.x * sensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, minPitch, maxPitch);

            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            if (cameraHolder != null)
                cameraHolder.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private static float NormalizePitch(float angle)
        {
            if (angle > 180f) angle -= 360f;
            return angle;
        }
    }
}
