using System;
using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// An instant item that needs hot water before it is food / a drink - a cup noodle, an instant coffee cup. Dry, it is an
    /// ingredient in its container (its consumer - <see cref="ReadyToEatFood"/> / <see cref="DrinkContainer"/> - refuses, and
    /// its contents can't spill). A <see cref="HotWaterStation"/> prepares it: the same object switches to its prepared
    /// identity (<see cref="preparedName"/>, <see cref="preparedTags"/>) and look (<see cref="dryVisual"/> off,
    /// <see cref="preparedVisual"/> on). One component for both items - what differs is data.
    ///
    /// Left Click while held and dry (<see cref="IHeldPrimaryAction"/>): aimed at a station that takes it, it is handed over
    /// (like an ingredient into a pan - <see cref="IOptionalHeldItemReceiver.ReceivesWithPrimary"/>); an unpaid store cup is
    /// refused (<see cref="StoreProduct.RefuseUnpaidUse"/>); aimed anywhere else, <see cref="dryNotice"/>. Prepared, the press
    /// is not used here - eating / drinking goes on as for any food / drink.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class HotWaterPreparable : MonoBehaviour, IHeldPrimaryAction
    {
        /// <summary>Raised when hot water has prepared it. Nothing listens yet.</summary>
        public static event Action<HotWaterPreparable> PreparedEvent;

        [SerializeField] private bool prepared;
        [Tooltip("Seconds under the hot water.")]
        [SerializeField] private float prepareSeconds = 2.8f;

        [Header("Look")]
        [Tooltip("Shown while dry (sealed lid, powder).")]
        [SerializeField] private GameObject dryVisual;
        [Tooltip("Shown once prepared (opened lid, hot contents).")]
        [SerializeField] private GameObject preparedVisual;

        [Header("Prepared identity")]
        [SerializeField] private string preparedName = "";
        [SerializeField] private ItemTag preparedTags = ItemTag.Food | ItemTag.Container;

        [Header("Left Click")]
        [SerializeField] private string dryNotice = "뜨거운 물이 필요해.";
        [SerializeField] private float reach = 1.6f;

        private Interactable _item;

        public bool IsPrepared => prepared;
        public float PrepareSeconds => prepareSeconds;
        private Interactable Item => _item != null ? _item : (_item = GetComponent<Interactable>());

        private void Awake() => ApplyVisuals();

        /// <summary>Hot water is in: prepared identity and look; the contents / drink become real.</summary>
        public void SetPrepared()
        {
            if (prepared)
                return;
            prepared = true;
            Item.SetTags(preparedTags);
            if (!string.IsNullOrEmpty(preparedName))
                Item.SetDisplayName(preparedName);
            ApplyVisuals();
            if (TryGetComponent(out DrinkContainer drink))
                drink.RefreshVisuals();
            if (TryGetComponent(out SpillableContents contents))
                contents.Refresh();
            PreparedEvent?.Invoke(this);
        }

        public bool PrimaryPress(Ray aim)
        {
            if (prepared)
                return false; // ready: eating / drinking is the consumer's
            IOptionalHeldItemReceiver station = null;
            if (Physics.Raycast(aim, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                var receiver = hit.collider.GetComponentInParent<IOptionalHeldItemReceiver>();
                if (receiver != null && receiver.ReceivesWithPrimary && receiver.AppliesTo(Item))
                    station = receiver;
            }
            if (station == null)
            {
                ObjectiveHUD.Notice(dryNotice);
                return true;
            }
            if (StoreProduct.RefuseUnpaidUse(this))
                return true; // a store cup not paid for yet: no hot water
            if (!station.CanReceive(Item))
            {
                ObjectiveHUD.Notice(station.GetRejectPrompt(Item));
                return true;
            }
            if (Item.Holder is PlayerInteractor player)
                player.TryHandOverHeld(station);
            return true;
        }

        private void ApplyVisuals()
        {
            if (dryVisual != null) dryVisual.SetActive(!prepared);
            if (preparedVisual != null) preparedVisual.SetActive(prepared);
        }
    }
}
