using UnityEngine;
using TMPro;

namespace CreatureExperiment.Creature.Perception
{
    /// <summary>
    /// Debug-only readout of <see cref="CreatureHearing"/> (and, in a small second section, the
    /// <see cref="PlayerMovementSoundEmitter"/> it is listening to). Its own HUD class - sensor code
    /// never touches HUD code, same convention as the Brain sensors' HUDs. Drawn bottom-right, clear
    /// of <c>ReactionEpisodeHUD</c> (top-left) and the two Brain HUDs stacked top-right. Purely a
    /// readout - it does not touch either component or any Creature system.
    /// </summary>
    public class CreatureHearingHUD : MonoBehaviour
    {
        [Tooltip("The Creature hearing sensor to display. Auto-found on this GameObject or its parent, else anywhere in the scene, if left empty.")]
        [SerializeField] private CreatureHearing hearing;
        [Tooltip("Optional - shows a small PLAYER SOUND section too. Auto-found in the scene if left empty and one exists.")]
        [SerializeField] private PlayerMovementSoundEmitter emitter;
        [Tooltip("Optional. A TextMeshPro / TextMeshProUGUI text to mirror the readout into.")]
        [SerializeField] private TMP_Text text;
        [Tooltip("Show the HUD at all. Off = fully hidden (both the TMP text, if assigned, and OnGUI).")]
        [SerializeField] private bool showDebug = true;
        [Tooltip("Also draw the readout with OnGUI (bottom-right corner) so it works with no UI set up.")]
        [SerializeField] private bool alsoDrawOnGUI = true;
        [SerializeField] private int onGuiFontSize = 14;

        private GUIStyle _style;

        private void Awake()
        {
            if (hearing == null)
                hearing = GetComponentInParent<CreatureHearing>();
            if (hearing == null)
                hearing = FindFirstObjectByType<CreatureHearing>();
            if (emitter == null)
                emitter = FindFirstObjectByType<PlayerMovementSoundEmitter>();
        }

        private void Update()
        {
            if (!showDebug || hearing == null)
            {
                if (text != null && text.gameObject.activeSelf)
                    text.text = "";
                return;
            }

            if (text != null)
                text.text = Format(hearing, emitter);
        }

        private static string Format(CreatureHearing h, PlayerMovementSoundEmitter e)
        {
            float dist = h.TimeSinceLastHeard < float.PositiveInfinity
                ? Vector3.Distance(new Vector3(h.transform.position.x, 0f, h.transform.position.z),
                                    new Vector3(h.LastHeardPosition.x, 0f, h.LastHeardPosition.z))
                : -1f;
            string distStr = dist >= 0f ? $"{dist,6:F1}" : "  --  ";

            string s =
                "CREATURE HEARING\n\n" +
                $"Last Type {h.LastHeardType,10}\n" +
                $"Intensity {h.LastHeardIntensity,6:F2}\n" +
                $"Distance  {distStr} m\n" +
                $"Heard     {(h.HasRecentHeardStimulus ? "YES" : "NO"),6}\n" +
                $"Serial    {h.HeardStimulusSerial,6}\n\n" +
                $"Orient    {(h.IsOrienting ? "ACTIVE" : "-"),6}";

            if (e != null)
            {
                s += "\n\nPLAYER SOUND\n" +
                     $"Mode      {e.CurrentMode.ToString().ToUpperInvariant(),10}\n" +
                     $"Intensity {e.CurrentIntensity,6:F2}";
            }

            return s;
        }

        private void OnGUI()
        {
            if (!showDebug || !alsoDrawOnGUI || hearing == null)
                return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = onGuiFontSize,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                };
                _style.normal.textColor = Color.white;
            }

            string s = Format(hearing, emitter);
            const float w = 240f;
            const float h = 260f;
            Rect rect = new Rect(Screen.width - w - 12f, Screen.height - h - 12f, w, h);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), s, _style);
        }
    }
}
