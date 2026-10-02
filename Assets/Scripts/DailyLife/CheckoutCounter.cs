using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The store checkout: one Interact target for the counter and the cashier standing behind it. Both sit under
    /// this object, so a hit on either collider finds this <see cref="IUsable"/> (GetComponentInParent) - "E · 계산하기".
    ///
    /// Use: gameplay input is locked (<see cref="PlayerControlLock"/>), the view turns to <see cref="cashierLook"/>, and
    /// the player's whole inventory (every slot, not just the hand) is checked for this store's unpaid
    /// <see cref="StoreProduct"/>s:
    /// - none (own things / already paid only): "어서오세요." then the conversation closes by itself.
    /// - some: "모두 구매하시겠어요?" with 예 / 아니요 on <see cref="DialogueUI"/> (default 예). W / S move the choice
    ///   (the UI "Navigate" action - PlayerMovement is locked, so the player doesn't walk), E confirms it.
    ///   아니요 → "천천히 둘러보세요." · 예 → the inventory is checked again, the prices summed and, if the
    ///   <see cref="PlayerWallet"/> covers it, the total is taken once and every one of them is marked paid
    ///   ("결제 완료됐습니다. 감사합니다."); if not, nothing changes ("다 사시기엔 돈이 부족하신데요.").
    ///   Each closing line stays up for <see cref="autoCloseSeconds"/> and closes without input.
    /// E is the only conversation key (<see cref="DialogueInput"/>): confirm the choice, cut a closing line short.
    /// Space does nothing (PlayerMovement - and Jump - stay locked). Input is ignored for <see cref="minLineSeconds"/>
    /// after each line appears, so the E that started the talk can't also confirm. Control comes back a frame after
    /// closing and the checkout waits <see cref="reuseDelay"/>, so the closing E isn't also read as a new Use.
    ///
    /// <see cref="WarnUnpaidExit"/>: the cashier stops a player leaving with unpaid goods (asked by
    /// <see cref="StoreExitGate"/>) - same lock / look-turn, "손님, 아직 계산 안 하셨습니다.", closed with E; nothing
    /// about the goods or the wallet changes. <see cref="PlayerHasUnpaid"/> is the shared "anything unpaid?" test.
    ///
    /// Items put down on the counter top are not part of the payment (inventory only).
    ///
    /// Focus: aiming at the counter or the cashier lights both outline groups together; talking hides them.
    /// The counter's top slab is deliberately NOT under this object, so a held item can still be placed on it.
    /// </summary>
    public class CheckoutCounter : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("References")]
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private DialogueUI dialogue;
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private PlayerInteractor player;
        [Tooltip("Found on the player if empty.")]
        [SerializeField] private PlayerWallet wallet;
        [Tooltip("Point on the cashier the player's view turns to (face).")]
        [SerializeField] private Transform cashierLook;

        [Header("Store")]
        [Tooltip("Only StoreProducts with this store id are charged here.")]
        [SerializeField] private string storeId = "GroceryStore";

        [Header("Focus outline")]
        [Tooltip("Outline group of the counter - every renderer under it is shown while focused.")]
        [SerializeField] private Transform counterOutline;
        [Tooltip("Outline group of the cashier - shown together with the counter's.")]
        [SerializeField] private Transform cashierOutline;

        [Header("Conversation")]
        [SerializeField] private string speakerName = "점원";
        [SerializeField] private string greetingLine = "어서오세요.";
        [SerializeField] private string askLine = "모두 구매하시겠어요?";
        [SerializeField] private string yesOption = "예";
        [SerializeField] private string noOption = "아니요";
        [SerializeField] private string declinedLine = "천천히 둘러보세요.";
        [SerializeField] private string paidLine = "결제 완료됐습니다. 감사합니다.";
        [SerializeField] private string notEnoughLine = "다 사시기엔 돈이 부족하신데요.";
        [SerializeField] private string unpaidExitLine = "손님, 아직 계산 안 하셨습니다.";

        [Header("Feel")]
        [SerializeField] private float focusDuration = 0.4f;
        [Tooltip("Input is ignored for this long after a line / the choice appears (stops the opening press / a held key).")]
        [SerializeField] private float minLineSeconds = 0.3f;
        [Tooltip("How long a closing line stays up before the conversation ends by itself.")]
        [SerializeField] private float autoCloseSeconds = 1.5f;
        [Tooltip("After the conversation, the checkout can't be used again for this long.")]
        [SerializeField] private float reuseDelay = 0.3f;

        [Header("Focus label")]
        [SerializeField] private string usePrompt = "E · 계산하기";

        private bool _talking;
        private bool _leave;
        private PlayerControlLock _lock;
        private float _readyAt;
        private InputAction _interact, _navigate;
        private bool _navigateWasEnabled;
        private readonly List<Renderer> _outlines = new List<Renderer>();
        private readonly List<StoreProduct> _due = new List<StoreProduct>();

        public bool InConversation => _talking;

        /// <summary>Raised after a successful payment (wallet charged, every product marked paid): counter, the products paid.</summary>
        public static event System.Action<CheckoutCounter, IReadOnlyList<StoreProduct>> Paid;

        public bool CanUse => !_talking && Time.time >= _readyAt;

        public string StoreId => storeId;

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

            var movement = player != null ? player.GetComponent<PlayerMovement>() : null;
            InputActionAsset asset = movement != null ? movement.InputActions : null;
            var map = asset != null ? asset.FindActionMap("Player", throwIfNotFound: false) : null;
            _interact = map?.FindAction(DialogueInput.AdvanceAction, throwIfNotFound: false);
            // Choice up / down: the UI map's Navigate (W/S, arrows) - separate from the Player map's Move.
            _navigate = asset != null ? asset.FindActionMap("UI", throwIfNotFound: false)?.FindAction("Navigate", throwIfNotFound: false) : null;

            if (counterOutline != null) _outlines.AddRange(counterOutline.GetComponentsInChildren<Renderer>(true));
            if (cashierOutline != null) _outlines.AddRange(cashierOutline.GetComponentsInChildren<Renderer>(true));
            SetFocused(false);
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
            _leave = false;
            _lock = PlayerControlLock.Acquire(player.gameObject);
            SetFocused(false); // the lock already cleared the aim focus; make sure nothing stays lit

            yield return TurnToCashier();

            // The lock switched PlayerInteractor off, which also disabled the shared Interact (E) action.
            _interact?.Enable();

            if (CollectDue() == 0)
            {
                yield return ClosingLine(greetingLine);
            }
            else
            {
                int choice = 0;
                yield return AskYesNo(c => choice = c);
                if (!_leave)
                {
                    if (choice != 0)
                        yield return ClosingLine(declinedLine);
                    else
                        yield return ClosingLine(TryPayAll() ? paidLine : notEnoughLine);
                }
            }

            yield return Close();
        }

        /// <summary>
        /// The cashier stops the player at the door: "손님, 아직 계산 안 하셨습니다." until E. Changes nothing about the
        /// goods or the money. False if a conversation is already running (the caller tries again later).
        /// </summary>
        public bool WarnUnpaidExit()
        {
            if (_talking || player == null || dialogue == null)
                return false;
            StartCoroutine(RunUnpaidWarning());
            return true;
        }

        private IEnumerator RunUnpaidWarning()
        {
            _talking = true;
            _leave = false;
            _lock = PlayerControlLock.Acquire(player.gameObject);
            SetFocused(false);

            yield return TurnToCashier();
            _interact?.Enable();

            dialogue.Show(speakerName, unpaidExitLine);
            float shownAt = Time.time;
            yield return null;
            while (Time.time - shownAt < minLineSeconds || _interact == null || !_interact.WasPressedThisFrame())
            {
                if (_interact == null && Time.time - shownAt > 2f)
                    break; // no input asset wired - don't trap the player
                yield return null;
            }

            yield return Close();
        }

        /// <summary>Any inventory slot (not just the hand) holds this store's product not yet paid for.</summary>
        public bool PlayerHasUnpaid()
        {
            if (player == null)
                return false;
            for (int i = 0; i < player.SlotCount; i++)
            {
                Interactable item = player.GetSlotItem(i);
                if (item != null && item.TryGetComponent(out StoreProduct product) && product.IsUnpaidOf(storeId))
                    return true;
            }
            return false;
        }

        /// <summary>Fills <see cref="_due"/> with this store's unpaid products in any inventory slot; returns the count.</summary>
        private int CollectDue()
        {
            _due.Clear();
            for (int i = 0; i < player.SlotCount; i++)
            {
                Interactable item = player.GetSlotItem(i);
                if (item != null && item.TryGetComponent(out StoreProduct product) && product.IsUnpaidOf(storeId))
                    _due.Add(product);
            }
            return _due.Count;
        }

        /// <summary>
        /// Pay for everything due, all or nothing: the inventory is checked again, the total taken from the wallet once,
        /// then every product is marked paid - all in this one call (no frame in between), so it can't run twice.
        /// </summary>
        private bool TryPayAll()
        {
            if (CollectDue() == 0 || wallet == null)
                return false;
            int total = 0;
            foreach (var p in _due)
                total += p.Price;
            if (!wallet.TrySpend(total))
                return false;
            foreach (var p in _due)
                p.MarkPaid();
            var paid = new List<StoreProduct>(_due);
            _due.Clear();
            Paid?.Invoke(this, paid);
            return true;
        }

        // Question + 예 / 아니요. W / S move, E confirms. Default 예 (0).
        private IEnumerator AskYesNo(System.Action<int> result)
        {
            string[] options = { yesOption, noOption };
            int selected = 0;
            dialogue.ShowChoice(speakerName, askLine, options, selected);

            _navigateWasEnabled = _navigate != null && _navigate.enabled;
            _navigate?.Enable();
            float shownAt = Time.time;
            float lastY = 0f;
            yield return null;

            while (true)
            {
                float y = _navigate != null ? _navigate.ReadValue<Vector2>().y : 0f;
                bool ready = Time.time - shownAt >= minLineSeconds;
                if (ready)
                {
                    if (_interact != null && _interact.WasPressedThisFrame())
                        break;
                    // Edge of the stick / key, so holding W doesn't keep stepping.
                    int step = y > 0.5f && lastY <= 0.5f ? -1 : y < -0.5f && lastY >= -0.5f ? 1 : 0;
                    if (step != 0)
                    {
                        selected = Mathf.Clamp(selected + step, 0, options.Length - 1);
                        dialogue.ShowChoice(speakerName, askLine, options, selected);
                    }
                }
                if (_interact == null && Time.time - shownAt > 2f)
                    break; // no input asset wired - don't trap the player (answers the default)
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
                    _leave = true;
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

        // Turn the first-person view to the cashier (PlayerLook is off, so only this moves it).
        private IEnumerator TurnToCashier()
        {
            var look = player.GetComponent<PlayerLook>();
            Camera cam = Camera.main;
            if (look == null || cam == null || cashierLook == null)
                yield break;

            Vector3 dir = cashierLook.position - cam.transform.position;
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
