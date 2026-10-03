using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A hot water dispenser (the convenience store's): one cup at a time under its nozzle. Same grammar as the toaster:
    /// - a dry instant cup (<see cref="HotWaterPreparable"/>) in the hand + Left Click aimed here: it is set on the tray
    ///   (a Left Click receiver - <see cref="IOptionalHeldItemReceiver.ReceivesWithPrimary"/>) and fills for
    ///   <see cref="HotWaterPreparable.PrepareSeconds"/>: a stream from the nozzle, a pouring sound, then a click and a puff of
    ///   steam - it is prepared (<see cref="HotWaterPreparable.SetPrepared"/>).
    /// - meanwhile the cup is seat-locked (<see cref="Interactable.SetPickupLock"/>): no taking it, eating, drinking or swapping.
    /// - done: Left Click aimed at it takes it into the hand (<see cref="IAimedPrimaryAction"/> - "LMB 꺼내기"), ahead of any
    ///   eating, so a click never eats it off the tray. In the hand, Left Click eats / drinks as usual.
    /// The station is part of the store (not a pickup), and the cup on it stays locked until it is taken: E never picks the cup.
    /// </summary>
    public class HotWaterStation : MonoBehaviour, IOptionalHeldItemReceiver, IAimedPrimaryAction
    {
        [Tooltip("Where the cup's pivot (its bottom) sits, under the nozzle.")]
        [SerializeField] private Transform seatPoint;
        [Tooltip("Shown only while water is running (a thin stream from the nozzle).")]
        [SerializeField] private GameObject waterStream;
        [SerializeField] private Renderer outlineRenderer;

        [Header("Labels / notices")]
        [SerializeField] private string stationName = "온수기";
        [SerializeField] private string receivePrompt = "뜨거운 물 받기";
        [SerializeField] private string busyPrompt = "뜨거운 물 받는 중...";
        [Tooltip("'{0}' = the cup's name.")]
        [SerializeField] private string takeOutPrompt = "LMB {0} 꺼내기";
        [SerializeField] private string occupiedPrompt = "이미 올려져 있어.";
        [SerializeField] private string notPreparablePrompt = "뜨거운 물이 필요한 것만 올릴 수 있어.";
        [SerializeField] private string alreadyPreparedPrompt = "이미 물을 부었어.";
        [SerializeField] private string handsFullNotice = "더 이상 들 수 없어.";

        private Interactable _item;
        private HotWaterPreparable _prep;
        private Collider[] _itemColliders;
        private float _time;
        private bool _done;

        public bool IsBusy => _item != null && !_done;

        // --- IFocusTarget: the label says what is going on here
        public string FocusName
        {
            get
            {
                if (_item == null) return stationName;
                return _done ? string.Format(takeOutPrompt, _item.DisplayName) : busyPrompt;
            }
        }
        public Transform FocusTransform => transform;
        public void SetFocused(bool focused)
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = focused;
        }

        // --- IOptionalHeldItemReceiver (Left Click)
        public float MaxReach => 0f;
        public bool IsDedicated => true;
        public bool ReceivesWithPrimary => true;
        public string PrimaryReceivePrompt => receivePrompt;

        public bool AppliesTo(Interactable item) => item != null && item.GetComponent<HotWaterPreparable>() != null;

        public bool CanReceive(Interactable item)
        {
            var prep = item != null ? item.GetComponent<HotWaterPreparable>() : null;
            return prep != null && !prep.IsPrepared && _item == null;
        }

        public string GetRejectPrompt(Interactable item)
        {
            var prep = item != null ? item.GetComponent<HotWaterPreparable>() : null;
            if (prep == null) return notPreparablePrompt;
            if (prep.IsPrepared) return alreadyPreparedPrompt;
            return _item != null ? occupiedPrompt : null;
        }

        public void Receive(Interactable item)
        {
            var prep = item != null ? item.GetComponent<HotWaterPreparable>() : null;
            if (prep == null)
                return;
            _itemColliders = FoodMount.Attach(item, seatPoint != null ? seatPoint : transform);
            FoodMount.IgnoreHost(this, _itemColliders, true);
            item.SetPickupLock(this);
            _item = item;
            _prep = prep;
            _time = 0f;
            _done = false;
            SetStream(true);
            ProceduralSfx.Play(SfxKind.Pour, item.transform.position, 0.8f);
        }

        // --- IAimedPrimaryAction: the prepared cup comes out with Left Click
        public bool PrimaryEnabled => _item != null && _done;
        public string PrimaryHint => _item != null ? string.Format(takeOutPrompt, _item.DisplayName) : "";

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
            Interactable cup = _item;
            Release();
            player.TryHoldNew(cup);
            return true;
        }

        private void Awake()
        {
            SetStream(false);
            if (outlineRenderer != null)
                outlineRenderer.enabled = false;
        }

        private void Update()
        {
            if (_item is null)
                return;
            Transform point = seatPoint != null ? seatPoint : transform;
            if (_item == null || _item.IsHeld || _item.transform.parent != point)
            {
                Release(); // gone, or somehow taken
                return;
            }
            FoodMount.IgnoreHost(this, _itemColliders, true);
            if (_done)
                return;
            _time += Time.deltaTime;
            if (_time < _prep.PrepareSeconds)
                return;
            _done = true;
            SetStream(false);
            _prep.SetPrepared();
            ProceduralSfx.Play(SfxKind.Pop, _item.transform.position, 0.6f);
            ParticleFx.Burst(_item.transform.position + Vector3.up * 0.15f, Vector3.up, new Color(0.92f, 0.94f, 0.96f, 0.8f), 8, 0.05f); // a puff of steam
        }

        private void Release()
        {
            if (_item != null)
            {
                FoodMount.IgnoreHost(this, _itemColliders, false);
                _item.ClearPickupLock(this);
            }
            _item = null;
            _prep = null;
            _itemColliders = null;
            _done = false;
            SetStream(false);
        }

        private void SetStream(bool on)
        {
            if (waterStream != null && waterStream.activeSelf != on)
                waterStream.SetActive(on);
        }
    }
}
