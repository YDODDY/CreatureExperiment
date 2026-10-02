using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using CreatureExperiment.DailyLife;
using CreatureExperiment.Player;
using CreatureExperiment.Story;
using CreatureExperiment.UI;

namespace CreatureExperiment.DistrictEditor
{
    /// <summary>
    /// Editor-only Play Mode test helpers (called from MCP while the editor plays): real input events, teleports, a status
    /// line. Not part of the game.
    /// </summary>
    public static class PlayTestDriver
    {
        public static string Setup()
        {
            Application.runInBackground = true;
            var s = InputSystem.settings;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            return "setup";
        }

        public static string Restore()
        {
            var s = InputSystem.settings;
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.PointersAndKeyboardsRespectGameViewFocus;
            s.backgroundBehavior = InputSettings.BackgroundBehavior.ResetAndDisableNonBackgroundDevices;
            Application.runInBackground = false;
            Time.timeScale = 1f;
            return "restored";
        }

        public static string Tap(string key)
        {
            var k = (Key)System.Enum.Parse(typeof(Key), key);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(k));
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            return "tap " + key;
        }

        public static string Hold(string key)
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState((Key)System.Enum.Parse(typeof(Key), key)));
            return "hold " + key;
        }

        public static string ReleaseKeys()
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            return "released";
        }

        public static string Click()
        {
            InputSystem.QueueStateEvent(Mouse.current, new MouseState().WithButton(MouseButton.Left, true));
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            return "click";
        }

        public static string ClickAt(Vector2 screen)
        {
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = screen });
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = screen }.WithButton(MouseButton.Left, true));
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = screen });
            return "click at " + screen;
        }

        /// <summary>Space if a dialogue line is showing; returns the status.</summary>
        public static string Advance()
        {
            var d = Object.FindFirstObjectByType<DialogueUI>();
            string before = Status();
            if (d != null && d.IsShowing) Tap("Space");
            return before;
        }

        public static string TP(float x, float y, float z, float yaw, float pitch = 8f)
        {
            var p = Player();
            var cc = p.GetComponent<CharacterController>();
            cc.enabled = false;
            p.transform.position = new Vector3(x, y, z);
            cc.enabled = true;
            p.GetComponent<PlayerLook>().SetLookAngles(yaw, pitch);
            return "tp " + p.transform.position;
        }

        public static string AimAt(Vector3 target)
        {
            var cam = Camera.main.transform;
            Vector3 d = target - cam.position;
            Player().GetComponent<PlayerLook>().SetLookAngles(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg,
                -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
            return "aim";
        }

        public static string AimHit()
        {
            var cam = Camera.main.transform;
            return Physics.Raycast(cam.position, cam.forward, out RaycastHit h, 5f, ~0, QueryTriggerInteraction.Ignore)
                ? h.collider.name + " d=" + h.distance.ToString("F2") : "none";
        }

        public static string FaceMike()
        {
            var mike = Object.FindFirstObjectByType<StoryNpc>();
            if (mike == null) return "no mike";
            var p = Player();
            Vector3 front = mike.transform.position + mike.transform.forward * 1.4f;
            TP(front.x, front.y + 0.05f, front.z, 0f);
            AimAt(mike.transform.position + Vector3.up * 1.1f);
            return "facing mike, aim=" + AimHit();
        }

        public static string Status()
        {
            var sb = new StringBuilder();
            var c = Object.FindFirstObjectByType<Day1StoryController>();
            var d = Object.FindFirstObjectByType<DialogueUI>();
            var mike = Object.FindFirstObjectByType<StoryNpc>();
            var p = Player();
            sb.Append("t=").Append(Time.time.ToString("F0"));
            if (c != null) sb.Append(" beat=").Append(c.Beat).Append(" conv=").Append(c.InConversation);
            sb.Append(" task='").Append(MainTaskHUD.CurrentTask).Append("'");
            if (d != null && d.IsShowing) sb.Append(" LINE[").Append(DialogueText(d)).Append("]");
            if (mike != null) sb.Append(" mike=").Append(mike.transform.position.ToString("F1")).Append(mike.IsWalking ? " walk" : "").Append(mike.HasArrived ? " arrived" : "")
                .Append(mike.IsWaitingForPlayer ? " WAIT" : "").Append(mike.IsTalkable ? " TALK" : "").Append(" v=").Append(mike.CurrentSpeed.ToString("F1"));
            if (p != null) sb.Append(" player=").Append(p.transform.position.ToString("F1"));
            return sb.ToString();
        }

        private static string DialogueText(DialogueUI d)
        {
            var texts = d.GetComponentsInChildren<TMPro.TMP_Text>(true);
            var sb = new StringBuilder();
            foreach (var t in texts) if (t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text)) sb.Append(t.text.Replace("\n", " / ")).Append(" | ");
            return sb.ToString();
        }

        public static string Notice()
        {
            var o = Object.FindFirstObjectByType<ObjectiveHUD>();
            var f = typeof(ObjectiveHUD).GetField("_notice", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var u = typeof(ObjectiveHUD).GetField("_noticeUntil", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var cur = typeof(ObjectiveHUD).GetField("_current", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return $"objective='{cur.GetValue(o)}' notice='{f.GetValue(o)}' active={(float)u.GetValue(o) > Time.time}";
        }

        private static GameObject Player() => GameObject.FindWithTag("Player");
    }
}
