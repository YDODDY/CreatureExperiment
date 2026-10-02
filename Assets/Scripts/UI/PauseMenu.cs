using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CreatureExperiment.UI
{
    /// <summary>
    /// Tab pause menu: PAUSE main page (계속하기 / 조작 방법) and the one-screen controls reference.
    /// Tab toggles it from anywhere - gameplay, a conversation, the computer, the main page or the controls page.
    ///
    /// While open:
    /// - <see cref="Time.timeScale"/> is 0 and the audio listener is paused; both go back to what they were before.
    /// - Every action of <see cref="gameplayInput"/> (the shared asset all gameplay reads - movement, look, E, LMB,
    ///   F, RMB, slots, and the conversation owners that enable Interact / Navigate themselves) is switched off;
    ///   on resume only the actions this menu switched off come back on. Components are left enabled, so nothing's
    ///   own state (sitting, a conversation, the computer) is touched - it simply gets no input until resume.
    /// - The cursor is shown and unlocked; resume restores the exact lock / visibility from before the pause
    ///   (locked in plain gameplay, free at the computer).
    /// - OnGUI HUDs in <see cref="hideWhilePaused"/> are switched off (OnGUI always draws above a uGUI canvas).
    ///
    /// Tab is its own input action, not part of <see cref="gameplayInput"/>, so blocking gameplay never blocks it.
    /// The UI is a scene canvas (built by Tools ▸ CreatureExperiment ▸ Build Pause Menu); clicks go through the scene's
    /// EventSystem, which uses the Input System's default UI actions - not <see cref="gameplayInput"/>.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("The shared gameplay input asset. All of its actions are switched off while paused.")]
        [SerializeField] private InputActionAsset gameplayInput;
        [SerializeField] private string toggleBinding = "<Keyboard>/tab";

        [Header("UI")]
        [Tooltip("Dim overlay + pages. Off while not paused.")]
        [SerializeField] private GameObject overlay;
        [SerializeField] private GameObject mainPage;
        [SerializeField] private GameObject controlsPage;

        [Header("HUD")]
        [Tooltip("OnGUI HUDs to switch off while paused - only those that were on are switched back on.")]
        [SerializeField] private Behaviour[] hideWhilePaused;

        [Header("Font")]
        [Tooltip("Optional. Leave empty to build a dynamic font from an OS font with Korean glyphs (same as DialogueUI).")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private string[] osFontFamilies = { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR" };

        /// <summary>True while the pause menu is open.</summary>
        public static bool IsPaused { get; private set; }

        private InputAction _toggle;
        private bool _paused;
        private float _prevTimeScale = 1f;
        private bool _prevAudioPaused;
        private CursorLockMode _prevCursorLock;
        private bool _prevCursorVisible;
        private readonly List<InputAction> _blocked = new List<InputAction>();
        private readonly List<Behaviour> _hidden = new List<Behaviour>();

        private void Awake()
        {
            _toggle = new InputAction("PauseMenuToggle", InputActionType.Button, toggleBinding);

            if (font == null)
                font = CreateOsFont();
            if (font != null && overlay != null)
                foreach (var text in overlay.GetComponentsInChildren<TMP_Text>(includeInactive: true))
                    text.font = font;

            if (overlay != null)
                overlay.SetActive(false);
        }

        private void OnEnable() => _toggle?.Enable();

        private void OnDisable()
        {
            _toggle?.Disable();
            if (_paused)
                Resume(); // never leave the game frozen / input-less behind a disabled menu
        }

        private void OnDestroy() => _toggle?.Dispose();

        private void Update()
        {
            if (_toggle.WasPressedThisFrame())
            {
                if (_paused) Resume();
                else Pause();
                return;
            }

            // Anything that switches a gameplay action back on while paused (a component's OnEnable) is switched off
            // again and handed back on resume like the rest.
            if (_paused)
                BlockGameplayInput();
        }

        public void Pause()
        {
            if (_paused)
                return;
            _paused = true;
            IsPaused = true;

            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            _prevAudioPaused = AudioListener.pause;
            AudioListener.pause = true;

            _prevCursorLock = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            BlockGameplayInput();

            _hidden.Clear();
            if (hideWhilePaused != null)
                foreach (var b in hideWhilePaused)
                    if (b != null && b.enabled)
                    {
                        b.enabled = false;
                        _hidden.Add(b);
                    }

            if (overlay != null)
                overlay.SetActive(true);
            ShowMain();
        }

        /// <summary>계속하기 / Tab: close the menu and give the game back exactly as it was.</summary>
        public void Resume()
        {
            if (!_paused)
                return;
            _paused = false;
            IsPaused = false;

            if (overlay != null)
                overlay.SetActive(false);
            ClearSelection();

            foreach (var b in _hidden)
                if (b != null)
                    b.enabled = true;
            _hidden.Clear();

            foreach (var action in _blocked)
                action?.Enable();
            _blocked.Clear();

            Cursor.lockState = _prevCursorLock;
            Cursor.visible = _prevCursorVisible;

            AudioListener.pause = _prevAudioPaused;
            // Only undo our own freeze: if something else changed timeScale meanwhile, leave its value alone.
            if (Mathf.Approximately(Time.timeScale, 0f))
                Time.timeScale = _prevTimeScale;
        }

        /// <summary>The PAUSE page (also 뒤로 from the controls page).</summary>
        public void ShowMain()
        {
            if (mainPage != null) mainPage.SetActive(true);
            if (controlsPage != null) controlsPage.SetActive(false);
            ClearSelection();
        }

        /// <summary>조작 방법: the controls reference page.</summary>
        public void ShowControls()
        {
            if (mainPage != null) mainPage.SetActive(false);
            if (controlsPage != null) controlsPage.SetActive(true);
            ClearSelection();
        }

        private void BlockGameplayInput()
        {
            if (gameplayInput == null)
                return;
            foreach (var map in gameplayInput.actionMaps)
                foreach (var action in map.actions)
                    if (action.enabled)
                    {
                        action.Disable();
                        if (!_blocked.Contains(action))
                            _blocked.Add(action);
                    }
        }

        // A clicked button stays selected; don't let it carry over to the next page / the next pause.
        private static void ClearSelection()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
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
