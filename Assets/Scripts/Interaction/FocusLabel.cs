using UnityEngine;
using TMPro;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// A single reusable world-space name tag. It knows nothing about how focus is decided;
    /// <c>PlayerInteractor</c> owns that and calls <see cref="Show"/> / <see cref="Hide"/>.
    /// While shown it hovers above the target and faces the camera.
    /// </summary>
    public class FocusLabel : MonoBehaviour
    {
        [Tooltip("Text element to write the name into. Found in children if left empty.")]
        [SerializeField] private TMP_Text text;
        [Tooltip("Camera the label turns to face. Defaults to Camera.main.")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("Extra height above the target's top, in world units.")]
        [SerializeField] private float verticalMargin = 0.25f;

        private Transform _target;
        private Renderer[] _targetRenderers;

        private void Awake()
        {
            if (text == null)
                text = GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (targetCamera == null)
                targetCamera = Camera.main;
            Hide();
        }

        public void Show(Interactable focus)
        {
            _target = focus.transform;
            _targetRenderers = focus.GetComponentsInChildren<Renderer>();
            if (text != null)
            {
                text.text = focus.DisplayName;
                text.enabled = true;
            }
            UpdatePose();
        }

        public void Hide()
        {
            _target = null;
            _targetRenderers = null;
            if (text != null)
                text.enabled = false;
        }

        private void LateUpdate()
        {
            if (_target != null)
                UpdatePose();
        }

        private void UpdatePose()
        {
            float topY = _target.position.y;
            if (_targetRenderers != null)
            {
                foreach (var r in _targetRenderers)
                {
                    if (r == null) continue;
                    topY = Mathf.Max(topY, r.bounds.max.y);
                }
            }

            transform.position = new Vector3(_target.position.x, topY + verticalMargin, _target.position.z);

            if (targetCamera == null)
                targetCamera = Camera.main;
            if (targetCamera != null)
            {
                Vector3 away = transform.position - targetCamera.transform.position;
                if (away.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(away);
            }
        }
    }
}
