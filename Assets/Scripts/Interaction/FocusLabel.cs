using UnityEngine;
using TMPro;

namespace CreatureExperiment.Interaction
{
    /// <summary>
    /// A single reusable name tag for whatever the player is focusing (an <see cref="Interactable"/>
    /// or a non-pickup <see cref="IFocusTarget"/> prop). It knows nothing about how focus is decided;
    /// <c>PlayerInteractor</c> owns that and calls <see cref="Show"/> / <see cref="Hide"/>.
    ///
    /// If a <see cref="TMP_Text"/> is assigned it hovers that above the target facing the camera
    /// (the original behaviour). If none is assigned it falls back to a small screen label under the
    /// crosshair, so the name still shows with zero UI set up.
    /// </summary>
    public class FocusLabel : MonoBehaviour
    {
        [Tooltip("Text element to write the name into. Found in children if left empty. If null, an OnGUI screen label is used instead.")]
        [SerializeField] private TMP_Text text;
        [Tooltip("Camera the label turns to face. Defaults to Camera.main.")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("Extra height above the target's top, in world units.")]
        [SerializeField] private float verticalMargin = 0.25f;
        [SerializeField] private int onGuiFontSize = 18;

        private Transform _target;
        private Renderer[] _targetRenderers;
        private string _name;
        private GUIStyle _style;

        private void Awake()
        {
            if (text == null)
                text = GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (targetCamera == null)
                targetCamera = Camera.main;
            Hide();
        }

        public void Show(IFocusTarget focus)
        {
            _target = focus.FocusTransform;
            _targetRenderers = _target != null ? _target.GetComponentsInChildren<Renderer>() : null;
            _name = focus.FocusName;

            if (text != null)
            {
                text.text = _name;
                text.enabled = true;
            }
            UpdatePose();
        }

        public void Hide()
        {
            _target = null;
            _targetRenderers = null;
            _name = null;
            if (text != null)
                text.enabled = false;
        }

        private void LateUpdate()
        {
            if (_target != null && text != null)
                UpdatePose();
        }

        private void UpdatePose()
        {
            if (text == null || _target == null)
                return;

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

        // Screen-label fallback - only used when no TMP text is assigned (so TestRoom, which has one,
        // is unaffected). Draws the focused object's name just under the screen centre.
        private void OnGUI()
        {
            if (text != null || _target == null || string.IsNullOrEmpty(_name))
                return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = onGuiFontSize,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                };
            }

            const float w = 400f;
            Rect r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f + 24f, w, 28f);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), _name, _style);
            GUI.color = Color.white;
            GUI.Label(r, _name, _style);
            GUI.color = prev;
        }
    }
}
