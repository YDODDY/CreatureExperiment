using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A toilet flushed with the Interact key (routed by <c>PlayerInteractor</c> through <see cref="IUsable"/>
    /// - this component reads no input). One press plays one short visual flush: the bowl water shrinks
    /// toward the drain and dips, then comes back, and the flush button sinks for the first moment.
    /// No fluid. A press while a flush is running is ignored, so flushes never stack.
    /// </summary>
    public class ToiletFlush : MonoBehaviour, IUsable, IFocusTarget
    {
        [Tooltip("The bowl water surface animated by the flush.")]
        [SerializeField] private Transform bowlWater;
        [Tooltip("Optional button that sinks briefly when pressed.")]
        [SerializeField] private Transform flushButton;

        [SerializeField] private string prompt = "물 내리기";
        [SerializeField] private float duration = 1.2f;
        [Tooltip("How far the water surface shrinks at the low point (0 = none, 1 = vanishes).")]
        [SerializeField] private float shrink = 0.6f;
        [SerializeField] private float dip = 0.03f;
        [SerializeField] private float buttonTravel = 0.006f;
        [SerializeField] private float buttonTime = 0.25f;

        private bool _flushing;
        private float _elapsed;
        private Vector3 _waterPos, _waterScale, _buttonPos;

        public bool IsFlushing => _flushing;

        public string FocusName => prompt;
        public Transform FocusTransform => transform;

        public void SetFocused(bool focused) { }

        private void Awake()
        {
            if (bowlWater != null)
            {
                _waterPos = bowlWater.localPosition;
                _waterScale = bowlWater.localScale;
            }
            if (flushButton != null)
                _buttonPos = flushButton.localPosition;
        }

        public void Use()
        {
            if (_flushing)
                return;
            _flushing = true;
            _elapsed = 0f;
        }

        private void Update()
        {
            if (!_flushing)
                return;

            _elapsed += Time.deltaTime;
            float t = duration > 0f ? Mathf.Clamp01(_elapsed / duration) : 1f;
            float wave = Mathf.Sin(t * Mathf.PI); // 0 -> 1 -> 0

            if (bowlWater != null)
            {
                float s = 1f - shrink * wave;
                bowlWater.localScale = new Vector3(_waterScale.x * s, _waterScale.y, _waterScale.z * s);
                bowlWater.localPosition = _waterPos + Vector3.down * (dip * wave);
            }
            if (flushButton != null)
                flushButton.localPosition = _buttonPos + Vector3.down * (_elapsed < buttonTime ? buttonTravel : 0f);

            if (t < 1f)
                return;

            if (bowlWater != null)
            {
                bowlWater.localScale = _waterScale;
                bowlWater.localPosition = _waterPos;
            }
            if (flushButton != null)
                flushButton.localPosition = _buttonPos;
            _flushing = false;
        }
    }
}
