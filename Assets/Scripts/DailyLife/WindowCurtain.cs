using UnityEngine;
using UnityEngine.Serialization;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A two-panel window curtain opened and closed with the Interact key (routed by
    /// <c>PlayerInteractor</c> through <see cref="IUsable"/> - this component reads no input). Slides the
    /// left / right panel transforms between a closed pose (meeting in the middle) and an open pose
    /// (bunched at the sides) over <see cref="moveDuration"/>. A press mid-move just reverses from where
    /// the panels are, so rapid presses can't break the state.
    ///
    /// Aim targets: while closed, <see cref="closedCollider"/> covers the whole curtain; while open, only
    /// the two side-stack colliders are on, so an open window has no collider in it - sight lines through
    /// the window (player / creature observation rays) stay clear. The stack colliders are children, which
    /// the interaction ray resolves to this root via GetComponentInParent. Colliders switch to the target
    /// state the moment a move starts, so a reversed move always ends with the matching set enabled.
    /// Also an <see cref="IFocusTarget"/>: "커튼 열기" / "커튼 닫기" label, no outline.
    ///
    /// Starts closed. <see cref="SetOpen"/> lets other systems (e.g. a later story sequence) read or force
    /// the state; nothing calls it yet.
    /// </summary>
    public class WindowCurtain : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("References")]
        [SerializeField] private Transform leftPanel;
        [SerializeField] private Transform rightPanel;
        [Tooltip("Aim target while closed - covers the whole curtain.")]
        [FormerlySerializedAs("useCollider")]
        [SerializeField] private Collider closedCollider;
        [Tooltip("Aim targets while open - one around each side stack.")]
        [SerializeField] private Collider leftOpenCollider;
        [SerializeField] private Collider rightOpenCollider;

        [Header("Focus label")]
        [SerializeField] private string openPrompt = "커튼 열기";
        [SerializeField] private string closePrompt = "커튼 닫기";

        [Header("Motion")]
        [SerializeField] private float moveDuration = 0.35f;
        [SerializeField] private bool startOpen;

        [Header("Left panel pose (local)")]
        [SerializeField] private Vector3 leftClosedPosition = new Vector3(0.11f, 1.575f, -0.86f);
        [SerializeField] private Vector3 leftClosedScale = new Vector3(0.08f, 1.75f, 1.68f);
        [SerializeField] private Vector3 leftOpenPosition = new Vector3(0.11f, 1.575f, -1.475f);
        [SerializeField] private Vector3 leftOpenScale = new Vector3(0.10f, 1.75f, 0.45f);

        [Header("Right panel pose (local)")]
        [SerializeField] private Vector3 rightClosedPosition = new Vector3(0.11f, 1.575f, 0.86f);
        [SerializeField] private Vector3 rightClosedScale = new Vector3(0.08f, 1.75f, 1.68f);
        [SerializeField] private Vector3 rightOpenPosition = new Vector3(0.11f, 1.575f, 1.475f);
        [SerializeField] private Vector3 rightOpenScale = new Vector3(0.10f, 1.75f, 0.45f);

        private bool _isOpen;
        private bool _isMoving;
        private float _elapsed;
        private Vector3 _leftFromPos, _leftFromScale, _rightFromPos, _rightFromScale;

        public bool IsOpen => _isOpen;
        public bool IsMoving => _isMoving;

        public string FocusName => _isOpen ? closePrompt : openPrompt;
        public Transform FocusTransform => transform;

        public void SetFocused(bool focused) { }

        private void Awake()
        {
            SetOpen(startOpen, instant: true);
        }

        public void Use() => SetOpen(!_isOpen);

        /// <summary>Open or close the curtain; <paramref name="instant"/> snaps without the slide.</summary>
        public void SetOpen(bool open, bool instant = false)
        {
            _isOpen = open;
            ApplyColliders();

            if (instant)
            {
                _isMoving = false;
                ApplyPose(1f);
                return;
            }

            if (leftPanel != null)
            {
                _leftFromPos = leftPanel.localPosition;
                _leftFromScale = leftPanel.localScale;
            }
            if (rightPanel != null)
            {
                _rightFromPos = rightPanel.localPosition;
                _rightFromScale = rightPanel.localScale;
            }
            _elapsed = 0f;
            _isMoving = true;
        }

        private void Update()
        {
            if (!_isMoving)
                return;

            _elapsed += Time.deltaTime;
            float t = moveDuration > 0f ? Mathf.Clamp01(_elapsed / moveDuration) : 1f;
            ApplyPose(Mathf.SmoothStep(0f, 1f, t));
            if (t >= 1f)
                _isMoving = false;
        }

        // t = 1 is the target pose; below 1 blends from the pose captured when the move started.
        private void ApplyPose(float t)
        {
            if (leftPanel != null)
            {
                Vector3 pos = _isOpen ? leftOpenPosition : leftClosedPosition;
                Vector3 scale = _isOpen ? leftOpenScale : leftClosedScale;
                leftPanel.localPosition = t >= 1f ? pos : Vector3.Lerp(_leftFromPos, pos, t);
                leftPanel.localScale = t >= 1f ? scale : Vector3.Lerp(_leftFromScale, scale, t);
            }
            if (rightPanel != null)
            {
                Vector3 pos = _isOpen ? rightOpenPosition : rightClosedPosition;
                Vector3 scale = _isOpen ? rightOpenScale : rightClosedScale;
                rightPanel.localPosition = t >= 1f ? pos : Vector3.Lerp(_rightFromPos, pos, t);
                rightPanel.localScale = t >= 1f ? scale : Vector3.Lerp(_rightFromScale, scale, t);
            }
        }

        private void ApplyColliders()
        {
            if (closedCollider != null)
                closedCollider.enabled = !_isOpen;
            if (leftOpenCollider != null)
                leftOpenCollider.enabled = _isOpen;
            if (rightOpenCollider != null)
                rightOpenCollider.enabled = _isOpen;
        }
    }
}
