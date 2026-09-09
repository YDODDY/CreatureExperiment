using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    public enum DayPhase
    {
        CommuteToWork, // "출근하기"      - walk Home -> Workplace
        Working,       // "물건 정리하기 X/N" - clocked in at the terminal, sorting
        CommuteHome,   // "퇴근하기"      - clocked out, walk Workplace -> Home
        Sleep          // "잠자기"        - back inside the house, may use the Bed
    }

    /// <summary>
    /// Daily-Life 0.1 - the one and only orchestrator of the small day loop. It holds just the state
    /// the loop needs (day number, phase) and nothing else: no money, hunger, stamina, time-of-day,
    /// schedule, or generic task framework.
    ///
    /// Sorting progress is recomputed from the WORLD every frame - for each <see cref="SortableItem"/>
    /// that is not currently held, is its pivot inside the matching <see cref="SortingArea"/>? So the
    /// count rises and falls live as things move; it is never "permanently done". Reaching N/N does
    /// NOT auto-complete work: the player must, while it currently reads N/N, use the card terminal
    /// (<see cref="UseTerminal"/>) to clock out. Once that succeeds the day's work is latched and
    /// later item moves no longer un-complete it.
    ///
    /// Interactions call in: <see cref="UseTerminal"/> (CardTerminal), <see cref="OnEnteredHome"/>
    /// (HomeArrival trigger), <see cref="UseBed"/> (Bed).
    /// </summary>
    public class DailyLifeDirector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ObjectiveHUD objectiveHud;
        [SerializeField] private SortingArea cubeArea;
        [SerializeField] private SortingArea sphereArea;
        [Tooltip("Player root - moved back to homeSpawn on day rollover.")]
        [SerializeField] private Transform player;
        [Tooltip("Where the player is placed at the start of each day. Position/rotation only - not used for any phase test.")]
        [SerializeField] private Transform homeSpawn;

        [Header("Setup")]
        [Tooltip("Day number the first day starts on.")]
        [SerializeField] private int startDay = 1;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private int day = 1;
        [SerializeField] private DayPhase phase = DayPhase.CommuteToWork;
        [SerializeField] private int sortedCount;
        [SerializeField] private int totalCount;
        [SerializeField] private bool workDoneToday;

        private SortableItem[] _items;
        private string _lastObjective;

        public int Day => day;
        public DayPhase Phase => phase;
        public int SortedCount => sortedCount;
        public int TotalCount => totalCount;

        private void Awake()
        {
            _items = FindObjectsByType<SortableItem>(FindObjectsSortMode.None);
            totalCount = _items.Length;
            day = Mathf.Max(1, startDay);
        }

        private void Start()
        {
            phase = DayPhase.CommuteToWork;
            workDoneToday = false;
            RecomputeSorted();
            RefreshObjective(force: true);
        }

        private void Update()
        {
            RecomputeSorted();
            RefreshObjective(force: false);
        }

        // --- world query ---------------------------------------------------

        private void RecomputeSorted()
        {
            int n = 0;
            for (int i = 0; i < _items.Length; i++)
            {
                SortableItem it = _items[i];
                if (it == null || it.IsHeld)
                    continue;
                SortKind? here = AreaKindAt(it.transform.position);
                if (here.HasValue && here.Value == it.Kind)
                    n++;
            }
            sortedCount = n;
        }

        private SortKind? AreaKindAt(Vector3 worldPos)
        {
            if (cubeArea != null && cubeArea.Contains(worldPos)) return SortKind.Cube;
            if (sphereArea != null && sphereArea.Contains(worldPos)) return SortKind.Sphere;
            return null;
        }

        // --- interactions ------------------------------------------------

        /// <summary>CardTerminal: clock in from the commute, or - only while the count currently reads N/N - clock out.</summary>
        public void UseTerminal()
        {
            switch (phase)
            {
                case DayPhase.CommuteToWork:
                    SetPhase(DayPhase.Working);
                    Debug.Log($"[DailyLife] Day {day}: clocked IN.");
                    break;

                case DayPhase.Working:
                    RecomputeSorted();
                    if (totalCount > 0 && sortedCount >= totalCount)
                    {
                        workDoneToday = true;
                        SetPhase(DayPhase.CommuteHome);
                        Debug.Log($"[DailyLife] Day {day}: clocked OUT - work complete.");
                    }
                    else
                    {
                        Debug.Log($"[DailyLife] Day {day}: clock-out denied - sorting {sortedCount}/{totalCount}.");
                    }
                    break;

                // CommuteHome / Sleep: the terminal does nothing.
                default:
                    break;
            }
        }

        /// <summary>HomeArrival trigger: the player walked back into the house. Only means something after clocking out.</summary>
        public void OnEnteredHome()
        {
            if (phase == DayPhase.CommuteHome)
            {
                SetPhase(DayPhase.Sleep);
                Debug.Log($"[DailyLife] Day {day}: home. Use the bed to sleep.");
            }
        }

        /// <summary>Bed: end the day and roll to the next. Gated on being in the Sleep phase.</summary>
        public void UseBed()
        {
            if (phase != DayPhase.Sleep)
                return;

            for (int i = 0; i < _items.Length; i++)
                if (_items[i] != null)
                    _items[i].ResetToSpawn();

            if (player != null && homeSpawn != null)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false; // teleport past the CharacterController's own collision
                player.SetPositionAndRotation(homeSpawn.position, homeSpawn.rotation);
                if (cc != null) cc.enabled = true;
            }

            day++;
            workDoneToday = false;
            SetPhase(DayPhase.CommuteToWork);
            RecomputeSorted();
            RefreshObjective(force: true);
            Debug.Log($"[DailyLife] Slept. Day {day} begins.");
        }

        // --- objective text --------------------------------------------

        private void SetPhase(DayPhase p)
        {
            phase = p;
            RefreshObjective(force: true);
        }

        private void RefreshObjective(bool force)
        {
            string s;
            switch (phase)
            {
                case DayPhase.CommuteToWork: s = "출근하기"; break;
                case DayPhase.Working:       s = $"물건 정리하기 {sortedCount}/{totalCount}"; break;
                case DayPhase.CommuteHome:   s = "퇴근하기"; break;
                case DayPhase.Sleep:         s = "잠자기"; break;
                default:                     s = ""; break;
            }

            if (!force && s == _lastObjective)
                return;
            _lastObjective = s;
            if (objectiveHud != null)
                objectiveHud.SetText(s);
        }
    }
}
