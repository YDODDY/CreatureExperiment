using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A small hinged part flipped with the Interact key (routed by <c>PlayerInteractor</c> through <see cref="IUsable"/>):
    /// the toilet lid / seat (up / down). Rotates its <see cref="pivot"/> between two local poses over a short time;
    /// a press mid-swing is ignored. Purely visual - the part's own collider rides along (it is what the aim hits).
    /// </summary>
    public class HingeToggle : MonoBehaviour, IUsable, IFocusTarget
    {
        [Tooltip("The rotated transform (the hinge). Empty = this transform.")]
        [SerializeField] private Transform pivot;
        [SerializeField] private Vector3 closedEuler;
        [SerializeField] private Vector3 openEuler = new Vector3(95f, 0f, 0f);
        [SerializeField] private bool startOpen;
        [SerializeField] private float duration = 0.3f;

        [Header("Focus label")]
        [SerializeField] private string openPrompt = "변기 뚜껑 올리기";
        [SerializeField] private string closePrompt = "변기 뚜껑 내리기";

        private bool _open;
        private float _t = -1f; // < 0 = not moving
        private Quaternion _from, _to;

        public bool IsOpen => _open;
        public bool CanUse => _t < 0f;
        public string FocusName => CanUse ? (_open ? closePrompt : openPrompt) : "";
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused) { }

        private Transform Pivot => pivot != null ? pivot : transform;

        private void Awake()
        {
            _open = startOpen;
            Pivot.localRotation = Quaternion.Euler(_open ? openEuler : closedEuler);
        }

        public void Use()
        {
            if (!CanUse)
                return;
            _from = Pivot.localRotation;
            _open = !_open;
            _to = Quaternion.Euler(_open ? openEuler : closedEuler);
            _t = 0f;
        }

        private void Update()
        {
            if (_t < 0f)
                return;
            _t += Time.deltaTime / Mathf.Max(0.01f, duration);
            Pivot.localRotation = Quaternion.Slerp(_from, _to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t)));
            if (_t >= 1f)
                _t = -1f;
        }
    }
}
