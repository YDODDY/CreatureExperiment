using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Gives a legacy <see cref="TextMesh"/> sign (work guideline, area labels, the grocery store sign) Unity's
    /// built-in dynamic font, so Korean text renders through the OS font fallback without a Korean TMP font asset.
    /// Runs in edit mode too, so the sign is readable in the Scene view.
    ///
    /// The font's own material is a GUI material that draws with ZTest Always - world text would show through
    /// walls and through the back of its own board. Signs therefore share one material on a depth-tested,
    /// one-sided text shader (never saved - rebuilt on enable).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TextMesh))]
    public class WorldTextFont : MonoBehaviour
    {
        private static Material s_depthTested;
        private static Font s_font;

        private void OnEnable()
        {
            var text = GetComponent<TextMesh>();
            if (text.font == null)
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var r = GetComponent<MeshRenderer>();
            Material mat = DepthTestedMaterial(text.font);
            if (r != null && mat != null && r.sharedMaterial != mat)
                r.sharedMaterial = mat;
        }

        private static Material DepthTestedMaterial(Font font)
        {
            if (font == null || font.material == null)
                return null;
            if (s_depthTested != null && s_font == font)
                return s_depthTested;

            // One-sided, depth-tested unlit text shader (Assets/Shaders/WorldText.shader); the font's own GUI
            // shader ignores a per-material ZTest. Missing shader: keep the font material as before.
            Shader shader = Shader.Find("CreatureExperiment/WorldText");
            if (shader == null)
                return font.material;

            s_font = font;
            s_depthTested = new Material(shader) { name = font.name + " (World, depth tested)", hideFlags = HideFlags.DontSave };
            s_depthTested.mainTexture = font.material.mainTexture;
            Font.textureRebuilt -= OnFontTextureRebuilt;
            Font.textureRebuilt += OnFontTextureRebuilt;
            return s_depthTested;
        }

        // The dynamic font atlas can be recreated when new glyphs are added: keep the copy pointing at it.
        private static void OnFontTextureRebuilt(Font font)
        {
            if (font == s_font && s_depthTested != null && font.material != null)
                s_depthTested.mainTexture = font.material.mainTexture;
        }
    }
}
