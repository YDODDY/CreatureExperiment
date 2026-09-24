using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A hinged door opened and closed with the Interact key. Sits on the hinge pivot and swings it
    /// about local Y between <see cref="closedAngle"/> and <see cref="openAngle"/>. The leaf's collider is
    /// a child, so <see cref="PlayerActivator"/>'s ray finds this via GetComponentInParent. The leaf
    /// collider is off while swinging (the ray passes through, so the door can't be re-used mid-swing)
    /// and comes back once the door has settled and the player isn't standing inside the leaf.
    /// Also an <see cref="IFocusTarget"/>: outline + "open door" / "close door" label while aimed at.
    /// Not a pickup - no <c>Interactable</c>.
    ///
    /// Optional lock: while <see cref="IsLocked"/> and closed, Use() does nothing and the label shows
    /// <see cref="lockedPrompt"/>. An already-open door can always be closed. Who locks / unlocks is
    /// decided elsewhere (e.g. <see cref="WorkplaceAttendance"/>); the door only obeys.
    /// </summary>
    public class SwingDoor : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("References")]
        [Tooltip("The door leaf's collider - disabled while the door is moving.")]
        [SerializeField] private BoxCollider leafCollider;
        [Tooltip("Renderer of the outline child - enabled only while focused. Same setup as FocusableProp's outline.")]
        [SerializeField] private Renderer outlineRenderer;

        [Header("Focus label")]
        [SerializeField] private string openPrompt = "문 열기";
        [SerializeField] private string closePrompt = "문 닫기";

        [Header("Swing")]
        [SerializeField] private float closedAngle = 0f;
        [SerializeField] private float openAngle = 90f;
        [SerializeField] private float swingDuration = 0.8f;

        [Header("Lock")]
        [Tooltip("While locked and closed, Use() does nothing. Off by default - ordinary doors never lock.")]
        [SerializeField] private bool locked;
        [SerializeField] private string lockedPrompt = "잠겨 있습니다";

        private bool _isOpen;
        private bool _isMoving;
        private bool _colliderPending;
        private float _fromAngle;
        private float _toAngle;
        private float _elapsed;

        public bool IsOpen => _isOpen;
        public bool IsMoving => _isMoving;
        public bool IsLocked => locked;

        // --- IFocusTarget: same outline + label feedback as FocusableProp, but the label names the
        // action the next Use() will do. The collider is off mid-swing, so focus drops and is picked
        // up again (with the new label) once the door settles.
        public string FocusName => locked && !_isOpen ? lockedPrompt : (_isOpen ? closePrompt : openPrompt);
        public Transform FocusTransform => leafCollider != null ? leafCollider.transform : transform;

        public void SetFocused(bool focused)
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = focused;
        }

        private void Awake()
        {
            float y = transform.localEulerAngles.y;
            _isOpen = Mathf.Abs(Mathf.DeltaAngle(y, openAngle)) < Mathf.Abs(Mathf.DeltaAngle(y, closedAngle));
            if (outlineRenderer != null)
                outlineRenderer.enabled = false;
        }

        public void SetLocked(bool value) => locked = value;

        /// <summary>
        /// Jump straight to closed, cancelling any swing. For resets while nobody is near the door (e.g. a
        /// new day). The label goes back to the open prompt; a leaf collider switched off by an interrupted
        /// swing comes back through the usual "no character inside the leaf" check in Update.
        /// </summary>
        public void SnapClosed()
        {
            _isMoving = false;
            _isOpen = false;
            SetAngle(closedAngle);
            if (leafCollider != null && !leafCollider.enabled)
                _colliderPending = true;
        }

        public void Use()
        {
            if (_isMoving || _colliderPending)
                return;
            if (locked && !_isOpen)
                return;

            _fromAngle = _isOpen ? openAngle : closedAngle;
            _toAngle = _isOpen ? closedAngle : openAngle;
            _elapsed = 0f;
            _isMoving = true;
            if (leafCollider != null)
                leafCollider.enabled = false;
        }

        private void Update()
        {
            if (_isMoving)
            {
                _elapsed += Time.deltaTime;
                float t = swingDuration > 0f ? Mathf.Clamp01(_elapsed / swingDuration) : 1f;
                SetAngle(Mathf.Lerp(_fromAngle, _toAngle, Mathf.SmoothStep(0f, 1f, t)));
                if (t < 1f)
                    return;

                SetAngle(_toAngle);
                _isMoving = false;
                _isOpen = Mathf.Approximately(_toAngle, openAngle);
                _colliderPending = true;
            }

            if (_colliderPending && !LeafBlockedByCharacter())
            {
                if (leafCollider != null)
                    leafCollider.enabled = true;
                _colliderPending = false;
            }
        }

        private void SetAngle(float y)
        {
            transform.localRotation = Quaternion.Euler(0f, y, 0f);
        }

        // Don't turn the leaf back on around a CharacterController standing where it settled.
        private bool LeafBlockedByCharacter()
        {
            if (leafCollider == null)
                return false;

            Transform leaf = leafCollider.transform;
            Vector3 center = leaf.TransformPoint(leafCollider.center);
            Vector3 halfExtents = Vector3.Scale(leafCollider.size * 0.5f, leaf.lossyScale);
            var hits = Physics.OverlapBox(center, halfExtents, leaf.rotation, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit is CharacterController)
                    return true;
            }
            return false;
        }
    }
}
