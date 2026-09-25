using System;
using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One thrown-away item, kept as plain data so it outlives the destroyed object. Enough to find
    /// out later what went in (and, for a future "뒤지기", to spawn an item of the same
    /// <see cref="itemId"/> back out).
    /// </summary>
    [Serializable]
    public struct DiscardedItemRecord
    {
        [Tooltip("Interactable.ItemId of the discarded item - what kind of item it was.")]
        public string itemId;
        [Tooltip("Interactable.DisplayName at the moment it was discarded.")]
        public string displayName;
        [Tooltip("GameObject name of the discarded instance (debug).")]
        public string objectName;
        [Tooltip("Time.time when it was discarded.")]
        public float discardedAt;
    }

    /// <summary>
    /// The body of a garbage dump. While its <see cref="cover"/> is fully open, a player holding a
    /// discardable <see cref="Interactable"/> can aim at the body and press Interact ("버리기"):
    /// <c>PlayerInteractor</c> releases its hold and hands the item here, and this dump adds a
    /// <see cref="DiscardedItemRecord"/> to its own list and destroys the object. Each dump keeps its own
    /// list for the session (runtime only). Taking items back out is not built yet. With no cover (the
    /// Home kitchen TrashBin) it is always open. What may go in is the item's own
    /// <see cref="Interactable.IsDiscardable"/> - the policy hook for items that must never be thrown away.
    /// </summary>
    public class GarbageDump : MonoBehaviour, IHeldItemReceiver
    {
        [Header("References")]
        [Tooltip("This dump's lid. The body only accepts items while it is fully open. None = no lid, always open.")]
        [SerializeField] private GarbageDumpCover cover;
        [Tooltip("Renderer of the body's outline child - enabled only while focused.")]
        [SerializeField] private Renderer outlineRenderer;

        [Header("Focus label")]
        [SerializeField] private string discardPrompt = "버리기";
        [Tooltip("Shown while the lid is open and the held item may not be thrown away.")]
        [SerializeField] private string rejectPrompt = "버릴 수 없습니다";

        [Header("Contents (runtime)")]
        [Tooltip("Everything thrown into this dump so far, oldest first.")]
        [SerializeField] private List<DiscardedItemRecord> discarded = new List<DiscardedItemRecord>();

        /// <summary>Everything thrown into this dump so far, oldest first.</summary>
        public IReadOnlyList<DiscardedItemRecord> Discarded => discarded;
        public int DiscardedCount => discarded.Count;
        public bool IsOpen => cover == null || cover.IsOpen; // no lid (a kitchen bin): always open

        // --- IFocusTarget
        public string FocusName => discardPrompt;
        public Transform FocusTransform => transform;

        public void SetFocused(bool focused)
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = focused;
        }

        private void Awake()
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = false;
        }

        // --- IHeldItemReceiver
        public bool CanReceive(Interactable item)
        {
            return IsOpen && item != null && item.IsDiscardable;
        }

        // Closed lid: no label - the body is just a closed box. Open lid + non-discardable: say why.
        public string GetRejectPrompt(Interactable item)
        {
            return IsOpen && item != null && !item.IsDiscardable ? rejectPrompt : null;
        }

        public void Receive(Interactable item)
        {
            if (item == null)
                return;

            var record = new DiscardedItemRecord
            {
                itemId = item.ItemId,
                displayName = item.DisplayName,
                objectName = item.gameObject.name,
                discardedAt = Time.time,
            };
            discarded.Add(record);
            Debug.Log($"[GarbageDump] {name}: discarded '{record.displayName}' ({record.itemId}) - {discarded.Count} item(s) inside.", this);

            Destroy(item.gameObject);
        }
    }
}
