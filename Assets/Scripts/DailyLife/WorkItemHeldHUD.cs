using UnityEngine;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One short line under the crosshair naming what work thing is in hand ("들고 있는 물건: 포장 완료"),
    /// so the player can still read it while aiming at a box / receiver. Covers work items, packing
    /// boxes, tape and stickers; shows their Interactable display name. Reads PlayerInteractor only.
    /// </summary>
    public class WorkItemHeldHUD : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private int fontSize = 16;

        private GUIStyle _style;

        private void OnGUI()
        {
            if (interactor == null)
                return;
            var held = interactor.HeldItem;
            if (held == null)
                return;

            var work = held.GetComponent<WorkItem>();
            bool isWorkThing = work != null
                || held.GetComponent<PackingBox>() != null
                || held.GetComponent<TapeRoll>() != null
                || held.GetComponent<FragileStickerItem>() != null;
            if (!isWorkThing)
                return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                };
            }

            string text = $"들고 있는 물건: {held.DisplayName}";
            const float w = 400f;
            Rect r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f + 56f, w, 24f);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _style);
            GUI.color = work != null && work.IsBroken ? new Color(1f, 0.55f, 0.5f) : Color.white;
            GUI.Label(r, text, _style);
            GUI.color = prev;
        }
    }
}
