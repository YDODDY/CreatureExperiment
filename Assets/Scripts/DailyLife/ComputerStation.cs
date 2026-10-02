using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// "Using the computer" - the one state for both ways in: Interact on the computer, or on its chair (whose
    /// <see cref="SittableChair"/> hands its Use / prompt to this). Idle → Entering → Active → Exiting → Idle.
    ///
    /// Enter: the chair's own sitting runs (<see cref="SittableChair.SitForOwner"/> - no sit, no use), gameplay
    /// input is locked (<see cref="PlayerControlLock"/>), the held item is kept but hidden, the computer is powered
    /// on if it was off, and the Main Camera itself (not FOV) glides from the seated eye to <see cref="viewPoint"/>.
    /// Active: cursor free; Left Click = screen click; E (<see cref="DialogueInput"/>) = on the chat page, the next line
    /// (after the last line, back to the desktop - <see cref="ComputerScreen.AdvanceChat"/>); on any other page, leave.
    /// Leaving is therefore always one E away from the desktop / internet page, and two from a finished chat. Space
    /// is not read (Jump stays locked). The lock switched those shared actions off, so this enables and reads them
    /// itself - PlayerInteractor / MealEater / PlayerLook stay off, so nothing else sees the press. Exit: cursor back, camera glides back to its seated pose, the lock
    /// is released and the chair stands the player up. Power stays as it is.
    ///
    /// Focus: aiming at the computer or at its chair lights both outlines together - one shared state here (the chair
    /// passes <see cref="SetFocused"/> on). Using the computer switches them off (the lock clears the aim focus).
    ///
    /// The world keeps running (no timeScale change).
    ///
    /// While a <see cref="DialogueUI"/> line is on screen (a story narration over the computer) the computer reads no
    /// input at all - the line's owner advances it. F (the "Throw" action, switched off for the world by the lock) is read
    /// here only while the screen's game window runs, and ends the game (<see cref="ComputerScreen.EndGame"/>).
    /// <see cref="Entered"/> is raised once the computer is ready to use.
    /// </summary>
    [RequireComponent(typeof(ComputerScreen))]
    public class ComputerStation : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("References")]
        [SerializeField] private SittableChair chair;
        [Tooltip("Camera pose while using the computer (looks at the monitor).")]
        [SerializeField] private Transform viewPoint;
        [SerializeField] private ComputerScreen screen;
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private PlayerInteractor player;
        [Tooltip("Found in the scene if empty. While it shows a line, the computer takes no input.")]
        [SerializeField] private DialogueUI dialogue;

        [Header("Focus outline")]
        [Tooltip("Outline group of the computer - every renderer under it is shown while focused.")]
        [SerializeField] private Transform computerOutline;
        [Tooltip("Outline group of the chair - shown together with the computer's.")]
        [SerializeField] private Transform chairOutline;

        [Header("Feel")]
        [SerializeField] private float transitionTime = 0.5f;
        [Tooltip("After leaving, the computer can't be used again for this long.")]
        [SerializeField] private float reuseDelay = 0.3f;

        [Header("Focus label")]
        [SerializeField] private string usePrompt = "E · 컴퓨터 사용";

        private enum Mode { Idle, Entering, Active, Exiting }

        private Mode _mode = Mode.Idle;
        private PlayerControlLock _lock;
        private Transform _cam;
        private Camera _camera;
        private Vector3 _camLocalPos;
        private Quaternion _camLocalRot;
        private CursorLockMode _prevCursorLock;
        private bool _prevCursorVisible;
        private InputAction _interact, _click, _throw;
        private readonly List<Renderer> _hiddenHeld = new List<Renderer>();
        private float _readyAt;
        private readonly List<Renderer> _outlines = new List<Renderer>();

        public bool InUse => _mode != Mode.Idle;
        /// <summary>Seated, camera at the screen, cursor free - the computer takes input.</summary>
        public bool IsActive => _mode == Mode.Active;

        /// <summary>Raised when someone has sat down and the computer is ready to use (camera arrived, cursor free).</summary>
        public static event System.Action<ComputerStation> Entered;

        public bool CanUse => _mode == Mode.Idle && SittableChair.Occupied == null && Time.time >= _readyAt;

        public string FocusName => CanUse ? usePrompt : "";
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused)
        {
            foreach (var r in _outlines)
                if (r != null)
                    r.enabled = focused;
        }

        private void Awake()
        {
            if (screen == null)
                screen = GetComponent<ComputerScreen>();
            if (player == null)
                player = FindFirstObjectByType<PlayerInteractor>();
            _camera = Camera.main;
            _cam = _camera != null ? _camera.transform : null;

            var movement = player != null ? player.GetComponent<PlayerMovement>() : null;
            var map = movement != null && movement.InputActions != null ? movement.InputActions.FindActionMap("Player", throwIfNotFound: false) : null;
            _interact = map?.FindAction(DialogueInput.AdvanceAction, throwIfNotFound: false);
            _click = map?.FindAction("Attack", throwIfNotFound: false);
            _throw = map?.FindAction("Throw", throwIfNotFound: false);
            if (dialogue == null)
                dialogue = FindFirstObjectByType<DialogueUI>();

            if (computerOutline != null) _outlines.AddRange(computerOutline.GetComponentsInChildren<Renderer>(true));
            if (chairOutline != null) _outlines.AddRange(chairOutline.GetComponentsInChildren<Renderer>(true));
            SetFocused(false);
        }

        public void Use()
        {
            if (!CanUse || chair == null || viewPoint == null || _cam == null || player == null)
                return;
            if (!chair.SitForOwner())
                return;

            _lock = PlayerControlLock.Acquire(player.gameObject);
            SetFocused(false); // the lock already cleared the aim focus; make sure nothing stays lit
            HideHeld();
            if (!screen.IsOn)
                screen.SetPower(true);
            screen.ShowDesktop();
            StartCoroutine(Enter());
        }

        private IEnumerator Enter()
        {
            _mode = Mode.Entering;
            _camLocalPos = _cam.localPosition;
            _camLocalRot = _cam.localRotation;

            Vector3 fromPos = _cam.position;
            Quaternion fromRot = _cam.rotation;
            for (float t = 0f; t < transitionTime; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / transitionTime);
                _cam.SetPositionAndRotation(Vector3.Lerp(fromPos, viewPoint.position, k), Quaternion.Slerp(fromRot, viewPoint.rotation, k));
                yield return null;
            }
            _cam.SetPositionAndRotation(viewPoint.position, viewPoint.rotation);

            _prevCursorLock = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Mouse.current?.WarpCursorPosition(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

            _interact?.Enable();
            _click?.Enable();
            _throw?.Enable();
            screen.SetInteractive(true);
            _mode = Mode.Active;
            Entered?.Invoke(this);
        }

        private void Update()
        {
            if (_mode != Mode.Active)
                return;

            if (dialogue != null && dialogue.IsShowing)
                return; // a story line over the computer: its owner reads the keys

            Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            screen.Hover(_camera, pointer);

            if (screen.IsGameRunning && _throw != null && _throw.WasPressedThisFrame())
            {
                screen.EndGame();
                return;
            }

            if (_interact != null && _interact.WasPressedThisFrame())
            {
                // The chat takes E while it is open; anywhere else E leaves the computer.
                if (screen.AdvanceChat())
                    return;
                StartCoroutine(Exit());
                return;
            }
            if (_click != null && _click.WasPressedThisFrame())
                screen.Click(_camera, pointer);
        }

        private IEnumerator Exit()
        {
            _mode = Mode.Exiting;
            screen.SetInteractive(false);
            RestoreCursor();

            Vector3 fromPos = _cam.position;
            Quaternion fromRot = _cam.rotation;
            Transform parent = _cam.parent;
            for (float t = 0f; t < transitionTime; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / transitionTime);
                Vector3 toPos = parent != null ? parent.TransformPoint(_camLocalPos) : _camLocalPos;
                Quaternion toRot = parent != null ? parent.rotation * _camLocalRot : _camLocalRot;
                _cam.SetPositionAndRotation(Vector3.Lerp(fromPos, toPos, k), Quaternion.Slerp(fromRot, toRot, k));
                yield return null;
            }
            FinishExit();
        }

        private void FinishExit()
        {
            _cam.localPosition = _camLocalPos;
            _cam.localRotation = _camLocalRot;
            _lock?.Release();
            _lock = null;
            chair.StandForOwner();
            ShowHeld();
            _readyAt = Time.time + reuseDelay;
            _mode = Mode.Idle;
        }

        private void OnDisable()
        {
            if (_mode == Mode.Idle)
                return;
            // Leave at once - never strand the player seated with the controls locked.
            StopAllCoroutines();
            screen.SetInteractive(false);
            if (_mode == Mode.Active || _mode == Mode.Exiting)
                RestoreCursor();
            FinishExit();
        }

        private void RestoreCursor()
        {
            Cursor.lockState = _prevCursorLock;
            Cursor.visible = _prevCursorVisible;
        }

        // The held item stays in the hand, but the hand's anchor is left at the seated eye while the camera moves in,
        // so it would hang in front of the monitor - hide its renderers meanwhile.
        private void HideHeld()
        {
            _hiddenHeld.Clear();
            Interactable held = player.HeldItem;
            if (held == null)
                return;
            foreach (var r in held.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled)
                    continue;
                r.enabled = false;
                _hiddenHeld.Add(r);
            }
        }

        private void ShowHeld()
        {
            foreach (var r in _hiddenHeld)
                if (r != null)
                    r.enabled = true;
            _hiddenHeld.Clear();
        }
    }
}
