using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The café's order counter: one Interact target for the counter and the barista behind it (both under this object,
    /// so a hit on either collider finds this <see cref="IUsable"/>) - "E · 주문하기". Unlike the stores there is nothing
    /// to take off a shelf: the player orders from the barista, pays at once and picks the order up from
    /// <see cref="pickupPoint"/>.
    ///
    /// Use: same conversation rules as <see cref="CheckoutCounter"/> - <see cref="PlayerControlLock"/>, the view turns to
    /// <see cref="baristaLook"/>, <see cref="DialogueUI"/> shows the text, W / S (UI Navigate) move a choice, E confirms
    /// (<see cref="DialogueInput"/>), closing lines close by themselves after <see cref="autoCloseSeconds"/>.
    /// 1. An order still being made, or anything loose left in <see cref="pickupArea"/> (the last order not taken yet):
    ///    one line, no menu - nothing new is ordered.
    /// 2. Menu: every <see cref="CafeMenuItem"/> under <see cref="menuRoot"/> ("Americano · $3.50") + "그만두기".
    /// 3. Confirm: "Americano, $3.50. 주문하시겠어요?" 예 / 아니요 - 아니요 goes back to the menu.
    /// 4. 예: the pickup spot is checked again, then the <see cref="PlayerWallet"/> is charged (not enough: nothing is
    ///    taken, "돈이 부족합니다."), then the order is placed - "잠시만 기다려주세요.". Nothing is charged for an order
    ///    that can't be served.
    /// <see cref="prepareSeconds"/> after payment a copy of the item's template appears at <see cref="pickupPoint"/> -
    /// an ordinary world item (E pickup, inventory, Left Click drink / eat), already paid (no <see cref="StoreProduct"/>)
    /// - and the HUD says "주문하신 상품 나왔습니다.". The timer runs in Update, independent of the conversation.
    /// One order at a time; no queue or ticket.
    /// </summary>
    public class BaristaCounter : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("References")]
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private DialogueUI dialogue;
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private PlayerInteractor player;
        [Tooltip("Found on the player if empty.")]
        [SerializeField] private PlayerWallet wallet;
        [Tooltip("Point on the barista the player's view turns to (face).")]
        [SerializeField] private Transform baristaLook;

        [Header("Menu / pickup")]
        [Tooltip("Parent of the inactive item templates; each one with a CafeMenuItem is a menu line, in hierarchy order.")]
        [SerializeField] private Transform menuRoot;
        [Tooltip("Where a finished order's pivot appears (the copy keeps the template's pivot).")]
        [SerializeField] private Transform pickupPoint;
        [Tooltip("Box (position / rotation / lossyScale) over the pickup spot: a loose item (an Interactable nobody holds) in it blocks new orders.")]
        [SerializeField] private Transform pickupArea;
        [Tooltip("Optional board text rewritten from the menu at start (title + one line per item).")]
        [SerializeField] private TextMesh menuBoard;
        [SerializeField] private string menuBoardTitle = "CAFÉ MENU";
        [Tooltip("Seconds from payment until the order is on the pickup counter.")]
        [SerializeField] private float prepareSeconds = 1.5f;

        [Header("Focus outline")]
        [SerializeField] private Transform counterOutline;
        [SerializeField] private Transform baristaOutline;

        [Header("Conversation")]
        [SerializeField] private string speakerName = "바리스타";
        [SerializeField] private string greetingLine = "어서오세요. 주문하시겠어요?";
        [SerializeField] private string cancelOption = "그만두기";
        [SerializeField] private string confirmFormat = "{0}, {1}. 주문하시겠어요?";
        [SerializeField] private string yesOption = "예";
        [SerializeField] private string noOption = "아니요";
        [SerializeField] private string cancelledLine = "다음에 또 오세요.";
        [SerializeField] private string orderedLine = "잠시만 기다려주세요.";
        [SerializeField] private string notEnoughLine = "돈이 부족합니다.";
        [SerializeField] private string pickupWaitingLine = "먼저 주문하신 상품을 가져가 주세요.";
        [SerializeField] private string preparingLine = "주문하신 상품 준비 중입니다. 잠시만 기다려주세요.";
        [SerializeField] private string readyNotice = "주문하신 상품 나왔습니다.";

        [Header("Feel")]
        [SerializeField] private float focusDuration = 0.4f;
        [Tooltip("Input is ignored for this long after a line / a choice appears (stops the opening press / a held key).")]
        [SerializeField] private float minLineSeconds = 0.3f;
        [SerializeField] private float autoCloseSeconds = 1.5f;
        [SerializeField] private float reuseDelay = 0.3f;

        [Header("Focus label")]
        [SerializeField] private string usePrompt = "E · 주문하기";

        private bool _talking;
        private PlayerControlLock _lock;
        private float _readyAt;
        private InputAction _interact, _navigate;
        private bool _navigateWasEnabled;
        private ObjectiveHUD _hud;
        private readonly List<Renderer> _outlines = new List<Renderer>();
        private readonly List<CafeMenuItem> _menu = new List<CafeMenuItem>();

        private CafeMenuItem _preparing; // paid, not on the counter yet
        private float _serveAt;

        public bool InConversation => _talking;
        public bool CanUse => !_talking && Time.time >= _readyAt;

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
            if (dialogue == null)
                dialogue = FindFirstObjectByType<DialogueUI>();
            if (player == null)
                player = FindFirstObjectByType<PlayerInteractor>();
            if (wallet == null && player != null)
                wallet = player.GetComponent<PlayerWallet>();
            _hud = FindFirstObjectByType<ObjectiveHUD>();

            var movement = player != null ? player.GetComponent<PlayerMovement>() : null;
            InputActionAsset asset = movement != null ? movement.InputActions : null;
            var map = asset != null ? asset.FindActionMap("Player", throwIfNotFound: false) : null;
            _interact = map?.FindAction(DialogueInput.AdvanceAction, throwIfNotFound: false);
            _navigate = asset != null ? asset.FindActionMap("UI", throwIfNotFound: false)?.FindAction("Navigate", throwIfNotFound: false) : null;

            if (menuRoot != null)
                foreach (Transform child in menuRoot)
                    if (child.TryGetComponent(out CafeMenuItem entry))
                    {
                        child.gameObject.SetActive(false);
                        _menu.Add(entry);
                    }
            WriteMenuBoard();

            if (counterOutline != null) _outlines.AddRange(counterOutline.GetComponentsInChildren<Renderer>(true));
            if (baristaOutline != null) _outlines.AddRange(baristaOutline.GetComponentsInChildren<Renderer>(true));
            SetFocused(false);
        }

        private void Update()
        {
            if (_preparing != null && Time.time >= _serveAt)
                Serve();
        }

        public void Use()
        {
            if (!CanUse || player == null || dialogue == null)
                return;
            StartCoroutine(RunConversation());
        }

        private IEnumerator RunConversation()
        {
            _talking = true;
            _lock = PlayerControlLock.Acquire(player.gameObject);
            SetFocused(false);

            yield return TurnToBarista();
            // The lock switched PlayerInteractor off, which also disabled the shared Interact (E) action.
            _interact?.Enable();

            string busy = BusyLine();
            if (busy != null || _menu.Count == 0)
            {
                yield return ClosingLine(busy ?? cancelledLine);
                yield return Close();
                yield break;
            }

            int pick = 0;
            while (true)
            {
                yield return Choose(greetingLine, MenuOptions(), pick, c => pick = c);
                if (pick >= _menu.Count)
                {
                    yield return ClosingLine(cancelledLine);
                    break;
                }

                CafeMenuItem item = _menu[pick];
                int answer = 0;
                yield return Choose(string.Format(confirmFormat, item.MenuName, PlayerWallet.FormatUsd(item.Price)),
                    new[] { yesOption, noOption }, 0, c => answer = c);
                if (answer != 0)
                    continue; // 아니요: back to the menu, same line still selected

                yield return ClosingLine(TryOrder(item));
                break;
            }

            yield return Close();
        }

        /// <summary>
        /// Can it be served (nothing in the making, pickup spot clear) → pay → order, all in this one call, so nothing is
        /// charged for an order that can't be made. Returns the barista's answer.
        /// </summary>
        private string TryOrder(CafeMenuItem item)
        {
            string busy = BusyLine();
            if (busy != null)
                return busy;
            if (wallet == null || !wallet.TrySpend(item.Price))
                return notEnoughLine;
            _preparing = item;
            _serveAt = Time.time + prepareSeconds;
            return orderedLine;
        }

        // Why no new order can be taken now, or null.
        private string BusyLine()
        {
            if (_preparing != null)
                return preparingLine;
            if (IsPickupOccupied())
                return pickupWaitingLine;
            return null;
        }

        // Only a loose item lying on the pickup spot counts - the counter itself never does.
        private bool IsPickupOccupied()
        {
            if (pickupArea == null)
                return false;
            Collider[] hits = Physics.OverlapBox(pickupArea.position, pickupArea.lossyScale * 0.5f,
                pickupArea.rotation, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider hit in hits)
            {
                var loose = hit.GetComponentInParent<Interactable>();
                if (loose != null && !loose.IsHeld)
                    return true;
            }
            return false;
        }

        private void Serve()
        {
            CafeMenuItem item = _preparing;
            _preparing = null;
            Transform at = pickupPoint != null ? pickupPoint : transform;
            GameObject made = Instantiate(item.gameObject, at.position, at.rotation);
            made.name = item.gameObject.name.Replace("_Template", "");
            made.SetActive(true);
            RestOn(made, at.position.y);
            if (_hud != null)
                _hud.ShowNotice(readyNotice);
        }

        // Templates have different pivots (a cup's is its middle, a pastry's its bottom): lift the copy so the bottom
        // of its solid colliders sits just on the pickup surface.
        private static void RestOn(GameObject made, float surfaceY)
        {
            Physics.SyncTransforms();
            bool any = false;
            float bottom = 0f;
            foreach (Collider c in made.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger)
                    continue;
                bottom = any ? Mathf.Min(bottom, c.bounds.min.y) : c.bounds.min.y;
                any = true;
            }
            if (any)
                made.transform.position += Vector3.up * (surfaceY - bottom + 0.002f);
        }

        private string[] MenuOptions()
        {
            var options = new string[_menu.Count + 1];
            for (int i = 0; i < _menu.Count; i++)
                options[i] = $"{_menu[i].MenuName}  ·  {PlayerWallet.FormatUsd(_menu[i].Price)}";
            options[_menu.Count] = cancelOption;
            return options;
        }

        private void WriteMenuBoard()
        {
            if (menuBoard == null)
                return;
            var text = new System.Text.StringBuilder(menuBoardTitle);
            text.Append('\n');
            foreach (var entry in _menu)
                text.Append('\n').Append(entry.MenuName).Append("   ").Append(PlayerWallet.FormatUsd(entry.Price));
            menuBoard.text = text.ToString();
        }

        // A question with options: W / S move, E confirms. Same input handling as the checkout's 예 / 아니요.
        private IEnumerator Choose(string question, string[] options, int start, System.Action<int> result)
        {
            int selected = Mathf.Clamp(start, 0, options.Length - 1);
            dialogue.ShowChoice(speakerName, question, options, selected);

            _navigateWasEnabled = _navigate != null && _navigate.enabled;
            _navigate?.Enable();
            float shownAt = Time.time;
            float lastY = 0f;
            yield return null;

            while (true)
            {
                float y = _navigate != null ? _navigate.ReadValue<Vector2>().y : 0f;
                if (Time.time - shownAt >= minLineSeconds)
                {
                    if (_interact != null && _interact.WasPressedThisFrame())
                        break;
                    // Edge of the key, so holding W doesn't keep stepping.
                    int step = y > 0.5f && lastY <= 0.5f ? -1 : y < -0.5f && lastY >= -0.5f ? 1 : 0;
                    if (step != 0)
                    {
                        selected = Mathf.Clamp(selected + step, 0, options.Length - 1);
                        dialogue.ShowChoice(speakerName, question, options, selected);
                    }
                }
                if (_interact == null && Time.time - shownAt > 2f)
                {
                    selected = options.Length - 1; // no input asset wired - don't trap the player (cancel / 아니요)
                    break;
                }
                lastY = y;
                yield return null;
            }
            RestoreNavigate();
            result(selected);
        }

        // One line, shown for autoCloseSeconds; E cuts it short.
        private IEnumerator ClosingLine(string line)
        {
            dialogue.Show(speakerName, line);
            float shownAt = Time.time;
            yield return null;
            while (Time.time - shownAt < autoCloseSeconds)
            {
                if (Time.time - shownAt >= minLineSeconds && _interact != null && _interact.WasPressedThisFrame())
                {
                    yield break;
                }
                yield return null;
            }
        }

        private IEnumerator Close()
        {
            dialogue.Hide();
            // Give control back a frame later so the closing E isn't also read as a new Interact.
            yield return null;
            _lock.Release();
            _lock = null;
            _readyAt = Time.time + reuseDelay;
            _talking = false;
        }

        private void RestoreNavigate()
        {
            if (_navigate != null && !_navigateWasEnabled)
                _navigate.Disable();
        }

        // Turn the first-person view to the barista (PlayerLook is off, so only this moves it).
        private IEnumerator TurnToBarista()
        {
            var look = player.GetComponent<PlayerLook>();
            Camera cam = Camera.main;
            if (look == null || cam == null || baristaLook == null)
                yield break;

            Vector3 dir = baristaLook.position - cam.transform.position;
            float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float targetPitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
            float startYaw = player.transform.eulerAngles.y;
            float startPitch = -Mathf.Asin(Mathf.Clamp(cam.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;

            for (float t = 0f; t < focusDuration; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / focusDuration);
                look.SetLookAngles(Mathf.LerpAngle(startYaw, targetYaw, k), Mathf.Lerp(startPitch, targetPitch, k));
                yield return null;
            }
            look.SetLookAngles(targetYaw, targetPitch);
        }

        private void OnDisable()
        {
            if (!_talking)
                return;
            // Never strand the player locked in a conversation.
            StopAllCoroutines();
            RestoreNavigate();
            if (dialogue != null)
                dialogue.Hide();
            _lock?.Release();
            _lock = null;
            _talking = false;
        }
    }
}
