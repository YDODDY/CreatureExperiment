using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// "Take one out": Interact on this collider takes one unit from <see cref="stock"/> and puts a fresh copy
    /// of <see cref="template"/> (a loose Egg / Bacon / Bread) straight into the player's hand (a free inventory slot).
    ///
    /// On a movable food container (egg carton, bacon pack, bread bag) this sits on the ContentZone child - the
    /// collider over the eggs / strips / slices - while the rest of the container (tray, tab, bag end) is the
    /// plain <see cref="Interactable"/> pickup. Touch the contents = one of the contents; grab the container =
    /// the whole container. Purely spatial: no press-length timing. A World Use (IUsable), so it beats the
    /// container's pickup only where the ray actually hits the content zone. A container in the hand has all
    /// its colliders off, so nothing can be taken out of it until it is put down.
    ///
    /// On the fixed fridge egg holder it sits on the holder itself (no Interactable - it can't be picked up). In Left Click
    /// mode the holder works like any container: Left Click aimed at it takes one egg into a free slot (<c>MealEater</c> finds
    /// it directly, as it is not a pickup); a held carton / egg aimed at it is put in first (<see cref="StockRefiller"/>).
    ///
    /// Hands full: nothing is taken and the label says so. Stock empty: nothing happens; with
    /// <see cref="hideWhenEmpty"/> (containers) this zone switches itself off, so the whole empty package is
    /// just the pickup again.
    ///
    /// <see cref="takeWithPrimary"/> (movable containers - cartons, packs, the bread bag, the cigarette pack): the zone is
    /// no longer an E target of its own (<see cref="IUsable.CanUse"/> false, so its collider is simply part of the
    /// container's body and E picks the whole container up). Taking one out is Left Click on the container
    /// (<see cref="IAimedPrimaryAction"/>, routed by <c>MealEater</c>), and the container's focus label shows both keys
    /// with <see cref="PrimaryHint"/>. Same take rules: unpaid refuses, empty does nothing, hands full says so.
    ///
    /// The same Left Click also works on the container while it is in the player's hand (<c>MealEater</c> asks the held
    /// item's <see cref="IAimedPrimaryAction"/> when nothing else used the press): the content goes into a free slot
    /// (<see cref="PlayerInteractor.TryStashNew"/>) and the pack stays in the hand, so clicking again takes the next one.
    /// One path for every pack (cigarettes, eggs, bacon, bread) - no per-pack code.
    /// </summary>
    public class PortionDispenser : MonoBehaviour, IUsable, IFocusTarget, IAimedPrimaryAction
    {
        [Tooltip("The count this takes from. Found in parents if empty.")]
        [SerializeField] private ConsumableStock stock;
        [Tooltip("Inactive loose item cloned per take. The copy's world scale = the template's localScale.")]
        [SerializeField] private GameObject template;
        [Tooltip("Where the copy appears if there is no player hand to put it in (debug / no interactor).")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Outline shown while aimed at (optional; e.g. the container's outline).")]
        [SerializeField] private Renderer outlineRenderer;
        [Tooltip("Switch this zone's GameObject off once the stock is empty (movable containers).")]
        [SerializeField] private bool hideWhenEmpty = true;

        [Tooltip("Left Click on the whole container takes one (E then only picks the container up). Off = the old E on this zone.")]
        [SerializeField] private bool takeWithPrimary;

        [Header("Label")]
        [SerializeField] private string prompt = "하나 꺼내기";
        [SerializeField] private string emptyPrompt = "비어 있습니다";
        [SerializeField] private string handsFullPrompt = "손을 비우세요";
        [Tooltip("Shown when the held item can be put back in here with Left Click (refillable stock only).")]
        [SerializeField] private string refillPrompt = "LMB 넣기";
        [Tooltip("Left Click on an empty container ('비어 있어.'; the egg holder / carton: '계란이 없어.').")]
        [SerializeField] private string emptyNotice = "비어 있어.";
        [SerializeField] private string fullPrompt = "가득 찼습니다";
        [Tooltip("Shown on an unpaid store package's contents (StoreProduct.paid == false).")]
        [SerializeField] private string unpaidPrompt = "구매 후 사용할 수 있습니다";

        private const string InventoryFullNotice = "더 이상 들 수 없어.";

        private static PlayerInteractor s_player;
        private static ObjectiveHUD s_hud;

        /// <summary>The loose item one take makes (read-only - e.g. to tell what a package holds).</summary>
        public GameObject Template => template;

        private ConsumableStock Stock => stock != null ? stock : (stock = GetComponentInParent<ConsumableStock>());

        private static PlayerInteractor Player
        {
            get
            {
                if (s_player == null)
                    s_player = FindFirstObjectByType<PlayerInteractor>();
                return s_player;
            }
        }

        private SwingDoor _door;
        private bool _doorLooked;

        // Mounted inside a door (fridge door egg holder) that is shut or swinging: this is really the door.
        private SwingDoor ClosedDoor
        {
            get
            {
                if (!_doorLooked) { _door = GetComponentInParent<SwingDoor>(); _doorLooked = true; }
                return _door != null && (!_door.IsOpen || _door.IsMoving) ? _door : null;
            }
        }

        public string FocusName
        {
            get
            {
                if (ClosedDoor != null) return ClosedDoor.FocusName;
                if (StoreProduct.IsUnpaidPackage(this)) return unpaidPrompt; // a store package not paid for yet
                // Holding something that can go back in here (egg / egg carton at the fridge egg holder): say so.
                var held = Player != null ? Player.HeldItem : null;
                if (Stock != null && held != null && held.TryGetComponent(out StockRefiller refiller) && refiller.AppliesTo(Stock))
                {
                    if (Stock.IsFull) return fullPrompt;
                    return refiller.Available > 0 ? $"{refillPrompt} ({Stock.Current}/{Stock.Max})" : handsFullPrompt;
                }
                string key = takeWithPrimary ? "LMB " : "";
                if (Stock == null || Stock.IsEmpty) return key + emptyPrompt;
                if (Player != null && !Player.HasFreeSlot) return key + InventoryFullNotice;
                return $"{key}{prompt} ({Stock.Current}/{Stock.Max})";
            }
        }

        public Transform FocusTransform => transform;

        /// <summary>Not an E target of its own in Left Click mode - E goes to the container's pickup.</summary>
        public bool CanUse => !takeWithPrimary;

        // --- IAimedPrimaryAction (Left Click on the container)
        public bool PrimaryEnabled => takeWithPrimary;

        public string PrimaryHint
        {
            get
            {
                if (StoreProduct.IsUnpaidPackage(this)) return $"LMB {unpaidPrompt}";
                if (Stock == null || Stock.IsEmpty) return "LMB 비어 있어";
                if (Player != null && !Player.HasFreeSlot) return $"LMB {InventoryFullNotice}";
                return $"LMB {prompt} ({Stock.Current}/{Stock.Max})";
            }
        }

        public bool TryAimedPrimary()
        {
            if (!takeWithPrimary || ClosedDoor != null)
                return false; // a shut door in front of it (fridge holder): the press is not for this
            if (Stock == null || Stock.IsEmpty)
            {
                Notice(emptyNotice); // nothing inside: the press is spent on the container, nothing happens
                return true;
            }
            if (StoreProduct.RefuseUnpaidUse(this))
                return true;
            if (Player != null && !Player.HasFreeSlot)
            {
                Notice(InventoryFullNotice);
                return true;
            }
            TakeOne();
            return true;
        }

        private static void Notice(string text)
        {
            if (s_hud == null)
                s_hud = FindFirstObjectByType<ObjectiveHUD>();
            if (s_hud != null)
                s_hud.ShowNotice(text);
        }

        public void SetFocused(bool focused)
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = focused;
        }

        private void OnEnable()
        {
            if (Stock != null)
                Stock.Changed += OnStockChanged;
        }

        // Not in OnEnable: switching a GameObject off while it is being switched on is refused.
        private void Start() => OnStockChanged(Stock);

        private void OnDisable()
        {
            if (stock != null)
                stock.Changed -= OnStockChanged;
            SetFocused(false);
        }

        private void OnStockChanged(ConsumableStock s)
        {
            if (hideWhenEmpty && s != null && s.IsEmpty && gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        public void Use()
        {
            if (ClosedDoor != null)
            {
                ClosedDoor.Use(); // aimed "through" a shut door whose leaf collider is off: E means the door
                return;
            }
            if (template == null || Stock == null || Stock.IsEmpty)
                return;
            if (StoreProduct.RefuseUnpaidUse(this))
                return; // unpaid store package: nothing is made, the count stays
            if (Player != null && !Player.HasFreeSlot)
                return;
            TakeOne();
        }

        // One unit out of the stock and into the player's hand (checks already done by the caller).
        private void TakeOne()
        {
            if (template == null || Stock == null || Stock.IsEmpty)
                return;
            PlayerInteractor player = Player;
            Transform at = spawnPoint != null ? spawnPoint : transform;
            GameObject made = Instantiate(template, at.position + Vector3.up * 0.05f, Quaternion.Euler(0f, at.eulerAngles.y, 0f));
            made.name = template.name.Replace("_Template", "");
            made.transform.localScale = template.transform.localScale;
            made.SetActive(true);

            Stock.TryTake();

            var item = made.GetComponent<Interactable>();
            if (player == null || item == null)
                return;
            // Out of the pack in the hand: the content goes into another slot and the pack stays in the hand,
            // so Left Click can keep taking. Out of a pack lying in the world: the content comes into the hand.
            if (IsInPlayerHand(player))
                player.TryStashNew(item);
            else
                player.TryHoldNew(item);
        }

        private bool IsInPlayerHand(PlayerInteractor player)
        {
            Interactable held = player.HeldItem;
            return held != null && transform.IsChildOf(held.transform);
        }
    }
}
