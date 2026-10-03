using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A one-food cooking spot on a pick-up-able object - the frying pan (egg, bacon), the pot (ramen, soup kit), the
    /// toaster (bread). Which foods it takes is <see cref="acceptedKinds"/> (empty = any food).
    ///
    /// Food goes in with the held food's Left Click (<see cref="IOptionalHeldItemReceiver.ReceivesWithPrimary"/>, see
    /// <see cref="FoodItem.PrimaryPress"/>); for Interact this is just the pan / pot / toaster to pick up. Food thrown onto
    /// it (F / Right Click) is still caught (<see cref="IThrownItemReceiver"/>) - physics play keeps working.
    ///
    /// Received food is parented to <see cref="foodPoint"/> (so it travels with the host), held still and kept from
    /// colliding with it (<see cref="FoodMount"/>). With <see cref="lockSeatedFood"/> it is seat-locked
    /// (<see cref="Interactable.SetPickupLock"/>): no grabbing or eating it out of the pan - an aim at it is an aim at the
    /// host. It leaves by being served (<see cref="TakeFood"/>, <see cref="CookwareServe"/>) - or, with
    /// <see cref="popWhenCooked"/> (the toaster), it is lifted by <see cref="popHeight"/> once cooked and taken out with
    /// Left Click (<see cref="IAimedPrimaryAction"/>: "LMB 토스트 꺼내기" - before any eating, so a click never eats it off
    /// the toaster). It stays seat-locked while it sits there, so E always means the toaster itself, never the toast.
    /// The slot frees itself (and unlocks) as soon as the food leaves the point or is gone.
    ///
    /// Heat: on a stove burner (<see cref="StoveBurner"/> - a pan / pot on a burner is "on"), or by itself
    /// (<see cref="selfHeated"/> - the toaster), never while the host is carried.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class FoodSlot : MonoBehaviour, IOptionalHeldItemReceiver, IThrownItemReceiver, IAimedPrimaryAction
    {
        [Tooltip("Where the food's pivot (its bottom) sits.")]
        [SerializeField] private Transform foodPoint;
        [Tooltip("Food here cooks while it is heated.")]
        [SerializeField] private bool cooks;
        [Tooltip("Heats by itself (toaster). Off: only while the host sits on a stove burner (pan, pot).")]
        [SerializeField] private bool selfHeated;
        [Tooltip("Foods this takes. Empty = any food.")]
        [SerializeField] private List<FoodKind> acceptedKinds = new List<FoodKind>();
        [Tooltip("Food seated here can't be picked up or eaten directly - only served out.")]
        [SerializeField] private bool lockSeatedFood = true;
        [Tooltip("Once cooked, the food is unlocked and lifted (a toaster's pop) - picked up with E.")]
        [SerializeField] private bool popWhenCooked;
        [SerializeField] private float popHeight = 0.08f;
        [Tooltip("Catch assist for thrown food: how far past the collider edge (m) a falling throw still lands in it.")]
        [SerializeField] private float catchMargin = 0.06f;

        [Header("Labels / notices")]
        [Tooltip("Left Click hint while holding food it takes (\"LMB 올려놓기\").")]
        [SerializeField] private string placePrompt = "올려놓기";
        [SerializeField] private string occupiedPrompt = "이미 음식이 있어.";
        [Tooltip("Food it does not take (bread at the pan).")]
        [SerializeField] private string wrongFoodPrompt = "여기에는 넣을 수 없어.";
        [Tooltip("Shown after putting food in while it is not heated (not on a burner).")]
        [SerializeField] private string notHeatingNotice = "가스레인지에 올려야 해.";
        [Tooltip("Left Click on a popped food: '{0}' = its name (\"LMB 토스트 꺼내기\").")]
        [SerializeField] private string takeOutPrompt = "LMB {0} 꺼내기";
        [SerializeField] private string handsFullNotice = "더 이상 들 수 없어.";

        private Interactable _host;
        private FoodItem _food;
        private Collider[] _foodColliders;
        private bool _popped;

        public bool Cooks => cooks;
        public FoodItem Food => _food;
        public bool IsOccupied => _food != null;
        public string NotHeatingNotice => notHeatingNotice;

        /// <summary>True while this slot cooks and is heated (on a burner, or self-heated), and its host is not carried.</summary>
        public bool IsHeating => cooks && !Host.IsHeld && (selfHeated || StoveBurner.IsOnBurner(Host));

        private Interactable Host => _host != null ? _host : (_host = GetComponent<Interactable>());

        // --- IFocusTarget
        public string FocusName => placePrompt;
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused) => Host.SetFocused(focused);

        // --- IOptionalHeldItemReceiver
        public float MaxReach => 0f;
        public bool IsDedicated => true;
        public bool ReceivesWithPrimary => true;
        public float CatchMargin => catchMargin;

        public bool AppliesTo(Interactable item) => item != null && item.GetComponent<FoodItem>() != null;

        public bool Accepts(FoodKind kind) => acceptedKinds == null || acceptedKinds.Count == 0 || acceptedKinds.Contains(kind);

        public bool CanReceive(Interactable item)
        {
            var food = item != null ? item.GetComponent<FoodItem>() : null;
            return food != null && Accepts(food.Kind) && !IsOccupied && !Host.IsHeld;
        }

        public string GetRejectPrompt(Interactable item)
        {
            var food = item != null ? item.GetComponent<FoodItem>() : null;
            if (food == null || !Accepts(food.Kind))
                return wrongFoodPrompt;
            return IsOccupied ? occupiedPrompt : null;
        }

        public void Receive(Interactable item)
        {
            var food = item != null ? item.GetComponent<FoodItem>() : null;
            if (food == null)
                return;

            _foodColliders = FoodMount.Attach(item, foodPoint != null ? foodPoint : transform);
            FoodMount.IgnoreHost(this, _foodColliders, true);
            _food = food;
            _popped = false;
            food.SetSlot(this);
            if (lockSeatedFood)
                item.SetPickupLock(this);
        }

        /// <summary>Let the seated food go (to be handed to a plate / bowl right away): unlocked, slot empty. Null if empty.</summary>
        public FoodItem TakeFood()
        {
            FoodItem food = _food;
            if (food == null)
                return null;
            Release();
            return food;
        }

        private void Release()
        {
            FoodMount.IgnoreHost(this, _foodColliders, false);
            _food.Interactable.ClearPickupLock(this);
            _food.SetSlot(null);
            _food = null;
            _foodColliders = null;
        }

        // --- IAimedPrimaryAction: a popped food (toast) is taken out with Left Click, straight into the hand.
        public bool PrimaryEnabled => popWhenCooked && _popped && _food != null && !Host.IsHeld;

        public string PrimaryHint => _food != null ? string.Format(takeOutPrompt, _food.Interactable.DisplayName) : "";

        public bool TryAimedPrimary()
        {
            if (!PrimaryEnabled)
                return false;
            var player = FindFirstObjectByType<PlayerInteractor>();
            if (player == null)
                return false;
            if (!player.HasFreeSlot)
            {
                ObjectiveHUD.Notice(handsFullNotice);
                return true;
            }
            FoodItem food = TakeFood(); // unlocked, slot free; still on the point until the hand takes it
            player.TryHoldNew(food.Interactable);
            return true;
        }

        private void Update()
        {
            if (_food == null)
            {
                _foodColliders = null; // eaten / destroyed
                return;
            }

            Transform point = foodPoint != null ? foodPoint : transform;
            if (_food.Interactable.IsHeld || _food.transform.parent != point)
            {
                Release();
                return;
            }
            FoodMount.IgnoreHost(this, _foodColliders, true);

            if (popWhenCooked && !_popped && _food.IsCooked)
                Pop();
        }

        // Done (toaster): up it comes - still seat-locked (E = the toaster), taken out with Left Click.
        private void Pop()
        {
            _popped = true;
            _food.transform.position += Vector3.up * popHeight; // world up, whatever way the slot point is turned
            ProceduralSfx.Play(SfxKind.Pop, _food.transform.position, 0.7f);
        }
    }
}
