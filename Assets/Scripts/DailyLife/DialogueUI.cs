using UnityEngine;
using TMPro;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The bottom-of-screen conversation overlay: a translucent black band with a speaker name and one
    /// line of text, shown all at once. <see cref="Show"/> / <see cref="Hide"/> only - no typing effect,
    /// choices, history or dialogue data. Whoever opens it decides when to close it.
    ///
    /// The panel and both texts are scene objects (tune size / alpha / fonts in the Inspector). The project
    /// has no Korean TMP font asset, so unless <see cref="font"/> is set, a dynamic font asset is made at
    /// runtime from the first installed OS font in <see cref="osFontFamilies"/> and put on both texts.
    /// </summary>
    public class DialogueUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text speakerText;
        [SerializeField] private TMP_Text bodyText;

        [Header("Font")]
        [Tooltip("Optional. Leave empty to build a dynamic font from an OS font with Korean glyphs.")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private string[] osFontFamilies = { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR" };

        public bool IsShowing => panel != null && panel.activeSelf;

        private void Awake()
        {
            if (font == null)
                font = CreateOsFont();
            if (font != null)
            {
                if (speakerText != null) speakerText.font = font;
                if (bodyText != null) bodyText.font = font;
            }
            Hide();
        }

        public void Show(string speakerName, string text)
        {
            if (speakerText != null) speakerText.text = speakerName ?? "";
            if (bodyText != null) bodyText.text = text ?? "";
            if (panel != null) panel.SetActive(true);
        }

        public void Hide()
        {
            if (panel != null) panel.SetActive(false);
        }

        private TMP_FontAsset CreateOsFont()
        {
            foreach (string family in osFontFamilies)
            {
                if (string.IsNullOrEmpty(family))
                    continue;
                TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(family, "Regular");
                if (asset != null)
                    return asset;
            }
            return null;
        }
    }
}
