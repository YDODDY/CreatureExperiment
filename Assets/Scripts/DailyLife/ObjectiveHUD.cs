using UnityEngine;
using TMPro;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The single objective line. <see cref="DailyLifeDirector"/> owns what it says.
    ///
    /// If a <see cref="TMP_Text"/> is assigned it is written to (use this with a Korean-glyph TMP font
    /// asset for proper text). Independently, an <see cref="alsoDrawOnGUI"/> fallback draws the same
    /// string centred at the top of the screen with the built-in font, so the loop is testable with
    /// zero UI setup - the counts ("4/6") always read correctly there even if the Hangul shows as
    /// boxes until a font is wired.
    /// </summary>
    public class ObjectiveHUD : MonoBehaviour
    {
        [Tooltip("Optional. A TextMeshPro / TextMeshProUGUI text to mirror the objective into. Needs a font asset with Korean glyphs.")]
        [SerializeField] private TMP_Text text;
        [Tooltip("Also draw the objective with OnGUI at the top of the screen (works with no UI set up).")]
        [SerializeField] private bool alsoDrawOnGUI = true;
        [SerializeField] private int onGuiFontSize = 24;

        private string _current = "";
        private GUIStyle _style;

        private void Awake()
        {
            if (text == null)
                text = GetComponentInChildren<TMP_Text>(includeInactive: true);
        }

        public void SetText(string value)
        {
            _current = value ?? "";
            if (text != null)
                text.text = _current;
        }

        private void OnGUI()
        {
            if (!alsoDrawOnGUI || string.IsNullOrEmpty(_current))
                return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = onGuiFontSize,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter,
                    wordWrap = false,
                };
            }

            const float w = 700f;
            Rect r = new Rect((Screen.width - w) * 0.5f, 22f, w, 48f);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), _current, _style);
            GUI.color = Color.white;
            GUI.Label(r, _current, _style);
            GUI.color = prev;
        }
    }
}
