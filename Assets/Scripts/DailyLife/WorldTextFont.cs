using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Gives a legacy <see cref="TextMesh"/> sign (work guideline, area labels) Unity's built-in dynamic
    /// font, so Korean text renders through the OS font fallback without a Korean TMP font asset.
    /// Runs in edit mode too, so the sign is readable in the Scene view.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TextMesh))]
    public class WorldTextFont : MonoBehaviour
    {
        private void OnEnable()
        {
            var text = GetComponent<TextMesh>();
            if (text.font == null)
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var r = GetComponent<MeshRenderer>();
            if (r != null && text.font != null && r.sharedMaterial != text.font.material)
                r.sharedMaterial = text.font.material;
        }
    }
}
