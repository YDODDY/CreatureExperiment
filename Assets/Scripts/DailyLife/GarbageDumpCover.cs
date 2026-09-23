using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A garbage dump's lid, opened and closed with the Interact key. Same shape as <see cref="SwingDoor"/>:
    /// sits on the hinge pivot and swings it between <see cref="closedAngle"/> and <see cref="openAngle"/>
    /// about <see cref="hingeAxis"/> (local), and the lid's collider is a child so
    /// <see cref="PlayerActivator"/>'s ray finds this via GetComponentInParent. The lid collider is off
    /// while swinging and comes back once the lid has settled. Also an <see cref="IFocusTarget"/>:
    /// outline + "열기" / "닫기" label. The owning <see cref="GarbageDump"/> reads <see cref="IsOpen"/>.
    /// </summary>
    public class GarbageDumpCover : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("References")]
        [Tooltip("The lid's collider - disabled while the lid is moving.")]
        [SerializeField] private BoxCollider lidCollider;
        [Tooltip("Renderer of the outline child - enabled only while focused.")]
        [SerializeField] private Renderer outlineRenderer;

        [Header("Focus label")]
        [SerializeField] private string openPrompt = "열기";
        [SerializeField] private string closePrompt = "닫기";

        [Header("Swing")]
        [Tooltip("Local axis the lid turns about (along the hinge edge).")]
        [SerializeField] private Vector3 hingeAxis = Vector3.forward;
        [SerializeField] private float closedAngle = 0f;
        [SerializeField] private float openAngle = -90f;
        [SerializeField] private float swingDuration = 0.5f;

        private bool _isOpen;
        private bool _isMoving;
        private float _fromAngle;
        private float _toAngle;
        private float _elapsed;

        /// <summary>True only while the lid is fully open (not mid-swing).</summary>
        public bool IsOpen => _isOpen && !_isMoving;
        public bool IsMoving => _isMoving;

        // --- IFocusTarget: the label names the action the next Use() will do.
        public string FocusName => _isOpen ? closePrompt : openPrompt;
        public Transform FocusTransform => lidCollider != null ? lidCollider.transform : transform;

        public void SetFocused(bool focused)
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = focused;
        }

        private void Awake()
        {
            _isOpen = false;
            SetAngle(closedAngle);
            if (outlineRenderer != null)
                outlineRenderer.enabled = false;
        }

        public void Use()
        {
            if (_isMoving)
                return;

            _fromAngle = _isOpen ? openAngle : closedAngle;
            _toAngle = _isOpen ? closedAngle : openAngle;
            _elapsed = 0f;
            _isMoving = true;
            if (lidCollider != null)
                lidCollider.enabled = false;
        }

        private void Update()
        {
            if (!_isMoving)
                return;

            _elapsed += Time.deltaTime;
            float t = swingDuration > 0f ? Mathf.Clamp01(_elapsed / swingDuration) : 1f;
            SetAngle(Mathf.Lerp(_fromAngle, _toAngle, Mathf.SmoothStep(0f, 1f, t)));
            if (t < 1f)
                return;

            SetAngle(_toAngle);
            _isMoving = false;
            _isOpen = Mathf.Approximately(_toAngle, openAngle);
            if (lidCollider != null)
                lidCollider.enabled = true;
        }

        private void SetAngle(float angle)
        {
            transform.localRotation = Quaternion.AngleAxis(angle, hingeAxis);
        }
    }
}
