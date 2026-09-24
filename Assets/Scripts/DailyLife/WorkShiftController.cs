using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>What one shift came to - kept after clock-out (and for the day that just ended).</summary>
    [Serializable]
    public struct ShiftResult
    {
        public bool ended;
        public int dailyQuota;
        public int correctCompletedCount;
        public int correctDiscardedCount;
        public int wrongSubmissionCount;
        public int brokenCount;
        public int resolvedCount;
        public int unresolvedCount;
        /// <summary>wrong + broken + unresolved. Only a number for a later pay / evaluation system.</summary>
        public int penaltyCount;
    }

    /// <summary>
    /// The new Workplace's labour for one day (Packing Workflow 0.3).
    ///
    /// <see cref="WorkplaceAttendance"/> starts the shift (clock-in), ends it (clock-out, any time -
    /// <see cref="EndShift"/>) and resets it on a new day. The <see cref="IncomingWorkBox"/> lays out the
    /// day's quota; the player packs items with boxes / tape / stickers from the <see cref="WorkSupply"/>s
    /// and hands them to the two <see cref="WorkReceiver"/>s, which call <see cref="SubmitCompleted"/> /
    /// <see cref="SubmitDiscard"/>. Every item resolves once, right or wrong.
    ///
    /// Judging a submitted box: a sealed box with an unbroken, not-for-discard item, with a fragile
    /// sticker exactly when the item is Fragile, is correct; anything else is wrong. A broken item counts
    /// only as broken (its penalty was booked when it broke) whichever way it leaves - never broken + wrong.
    /// Discarding: a red item is correct, a good item is wrong, a broken one is just resolved.
    /// </summary>
    public class WorkShiftController : MonoBehaviour
    {
        [Header("Batch")]
        [Tooltip("Inactive Cube work item (Normal shape) the batch is cloned from.")]
        [SerializeField] private WorkItem cubeTemplate;
        [Tooltip("Inactive Sphere work item (Fragile shape) the batch is cloned from.")]
        [SerializeField] private WorkItem sphereTemplate;
        [Tooltip("Parent whose children are the spots the batch is laid out on. Reused (stacked up) if the quota is larger.")]
        [SerializeField] private Transform spawnRoot;
        [SerializeField] private IncomingWorkBox incomingBox;
        [Tooltip("Items laid out per day.")]
        [SerializeField] private int dailyQuota = 8;
        [Tooltip("How many of the quota are red (discard), any shape.")]
        [SerializeField] private int discardCount = 2;
        [Tooltip("How many of the quota are Fragile spheres (not red). The rest are Normal cubes.")]
        [SerializeField] private int fragileCount = 3;

        [Header("Runtime clean-up")]
        [Tooltip("Stickers stuck on walls / floors - cleared on a new day.")]
        [SerializeField] private RuntimeStickerRoot runtimeStickers;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private bool shiftActive;
        [SerializeField] private bool shiftEnded;
        [SerializeField] private bool boxOpened;
        [SerializeField] private ShiftResult current;
        [Tooltip("The last finished shift (clock-out, or the day ending without one).")]
        [SerializeField] private ShiftResult lastShiftResult;

        private readonly List<WorkItem> _spawned = new List<WorkItem>();
        private readonly List<GameObject> _runtime = new List<GameObject>();

        public bool ShiftActive => shiftActive;
        public bool ShiftEnded => shiftEnded;
        public bool BoxOpened => boxOpened;
        /// <summary>Submissions / discards count only between clock-in and clock-out.</summary>
        public bool CanAcceptWork => shiftActive && !shiftEnded;
        public int DailyQuota => dailyQuota;
        public ShiftResult Current => current;
        public ShiftResult LastShiftResult => lastShiftResult;

        /// <summary>Objective line while clocked in (the director shows it during the Working phase).</summary>
        public string ObjectiveText
        {
            get
            {
                if (!boxOpened) return "입고 상자 열기";
                string s = $"오늘 할당량 {current.resolvedCount}/{current.dailyQuota}";
                int issues = current.wrongSubmissionCount + current.brokenCount;
                return issues > 0 ? $"{s} (오류 {current.wrongSubmissionCount}, 파손 {current.brokenCount})" : s;
            }
        }

        private void Awake()
        {
            if (cubeTemplate != null) cubeTemplate.gameObject.SetActive(false);
            if (sphereTemplate != null) sphereTemplate.gameObject.SetActive(false);
        }

        /// <summary>Attendance: clock-in succeeded. The work starts once the player opens the box.</summary>
        public void OnClockedIn()
        {
            shiftActive = true;
            shiftEnded = false;
            current = new ShiftResult { dailyQuota = dailyQuota };
        }

        /// <summary>IncomingWorkBox: open once per day, only during the shift.</summary>
        public bool TryOpenIncomingBox()
        {
            if (!CanAcceptWork || boxOpened || cubeTemplate == null || sphereTemplate == null)
                return false;

            boxOpened = true;
            SpawnBatch();
            if (incomingBox != null)
                incomingBox.SetOpenVisual(true);
            Debug.Log($"[WorkShift] Incoming box opened - quota {dailyQuota}.");
            return true;
        }

        private void SpawnBatch()
        {
            int discard = Mathf.Clamp(discardCount, 0, dailyQuota);
            int fragile = Mathf.Clamp(fragileCount, 0, dailyQuota - discard);

            // (category, shouldDiscard) per item; red items get a random shape.
            var plan = new List<(WorkItemCategory, bool)>();
            for (int i = 0; i < discard; i++)
                plan.Add((UnityEngine.Random.value < 0.5f ? WorkItemCategory.Normal : WorkItemCategory.Fragile, true));
            for (int i = 0; i < fragile; i++)
                plan.Add((WorkItemCategory.Fragile, false));
            while (plan.Count < dailyQuota)
                plan.Add((WorkItemCategory.Normal, false));

            for (int i = plan.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (plan[i], plan[j]) = (plan[j], plan[i]);
            }

            int points = spawnRoot != null ? spawnRoot.childCount : 0;
            for (int i = 0; i < plan.Count; i++)
            {
                Vector3 pos = transform.position;
                Quaternion rot = Quaternion.identity;
                if (points > 0)
                {
                    Transform p = spawnRoot.GetChild(i % points);
                    pos = p.position + Vector3.up * (0.4f * (i / points));
                    rot = p.rotation;
                }

                (WorkItemCategory category, bool discardIt) = plan[i];
                WorkItem template = category == WorkItemCategory.Fragile ? sphereTemplate : cubeTemplate;
                WorkItem item = Instantiate(template, pos, rot, transform);
                item.name = $"WorkItem_{i + 1:00}";
                item.Init(this, category, discardIt);
                item.gameObject.SetActive(true);
                _spawned.Add(item);
            }
        }

        /// <summary>WorkSupply: something it made, to be removed on a new day.</summary>
        public void RegisterRuntimeObject(GameObject obj)
        {
            if (obj != null)
                _runtime.Add(obj);
        }

        /// <summary>Completed receiver: judge a handed-in box.</summary>
        public void SubmitCompleted(PackingBox box)
        {
            if (box == null || box.IsSubmitted)
                return;
            box.MarkSubmitted();

            WorkItem item = box.ContainedItem;
            if (item == null)
            {
                current.wrongSubmissionCount++;
                Debug.Log("[WorkShift] Submitted an empty box - wrong.");
                return;
            }
            if (item.IsResolved)
                return;

            item.MarkResolved();
            current.resolvedCount++;

            string verdict;
            if (item.IsBroken)
                verdict = "broken (already penalised)";
            else if (item.ShouldDiscard)
                { current.wrongSubmissionCount++; verdict = "wrong - discard item was packed"; }
            else if (!box.IsSealed)
                { current.wrongSubmissionCount++; verdict = "wrong - not taped"; }
            else if (item.Category == WorkItemCategory.Fragile && !box.HasFragileSticker)
                { current.wrongSubmissionCount++; verdict = "wrong - fragile without sticker"; }
            else if (item.Category == WorkItemCategory.Normal && box.HasFragileSticker)
                { current.wrongSubmissionCount++; verdict = "wrong - unnecessary fragile sticker"; }
            else
                { current.correctCompletedCount++; verdict = "correct"; }

            Debug.Log($"[WorkShift] Completed: {verdict}. {current.resolvedCount}/{current.dailyQuota}");
        }

        /// <summary>Discard receiver: judge a raw work item thrown away.</summary>
        public void SubmitDiscard(WorkItem item)
        {
            if (item == null || item.IsResolved)
                return;

            item.MarkResolved();
            current.resolvedCount++;

            string verdict;
            if (item.IsBroken)
                verdict = "broken (already penalised)";
            else if (item.ShouldDiscard)
                { current.correctDiscardedCount++; verdict = "correct"; }
            else
                { current.wrongSubmissionCount++; verdict = "wrong - good item discarded"; }

            Debug.Log($"[WorkShift] Discard: {verdict}. {current.resolvedCount}/{current.dailyQuota}");
        }

        /// <summary>WorkItem: a Fragile item broke (reported once per item).</summary>
        public void ReportBroken(WorkItem item)
        {
            current.brokenCount++;
            Debug.Log($"[WorkShift] Fragile broken - {current.brokenCount} this shift.");
        }

        /// <summary>
        /// Attendance clock-out (allowed any time while clocked in): fix the counts. Whatever was not
        /// resolved is missed. Also used when a day ends without a clock-out.
        /// </summary>
        public ShiftResult EndShift()
        {
            if (!shiftActive || shiftEnded)
                return lastShiftResult;

            shiftEnded = true;
            current.ended = true;
            current.unresolvedCount = Mathf.Max(0, current.dailyQuota - current.resolvedCount);
            current.penaltyCount = current.wrongSubmissionCount + current.brokenCount + current.unresolvedCount;
            lastShiftResult = current;

            Debug.Log($"[WorkShift] Shift ended - quota {current.dailyQuota}, correct packed {current.correctCompletedCount}, " +
                      $"correct discarded {current.correctDiscardedCount}, wrong {current.wrongSubmissionCount}, " +
                      $"broken {current.brokenCount}, unresolved {current.unresolvedCount}, penalty {current.penaltyCount}.");
            return lastShiftResult;
        }

        /// <summary>Attendance, at scene start and on every new day: box closed, everything made today gone, counts zeroed.</summary>
        public void ResetDay()
        {
            if (shiftActive && !shiftEnded)
                EndShift(); // the day ended without a clock-out - keep its result

            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null)
                    Destroy(_spawned[i].gameObject);
            _spawned.Clear();

            for (int i = 0; i < _runtime.Count; i++)
                if (_runtime[i] != null)
                    Destroy(_runtime[i]);
            _runtime.Clear();

            if (runtimeStickers != null)
                runtimeStickers.Clear();

            shiftActive = false;
            shiftEnded = false;
            boxOpened = false;
            current = new ShiftResult { dailyQuota = dailyQuota };

            if (incomingBox != null)
                incomingBox.SetOpenVisual(false);
        }
    }
}
