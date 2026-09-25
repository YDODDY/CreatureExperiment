using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    public enum DayPhase
    {
        CommuteToWork, // "출근하기"   - walk Home -> Workplace
        Working,       // clocked in at the Workplace; the objective line is the shift's own
        CommuteHome,   // "퇴근하기"   - clocked out, walk Workplace -> Home
        Sleep          // "잠자기"     - back inside the house, may use the Bed
    }

    /// <summary>
    /// The orchestrator of the small day loop. It holds just the state the loop needs (day number,
    /// phase) and nothing else: no money, hunger, stamina, time-of-day or generic task framework.
    ///
    /// The Workplace owns the work itself: <see cref="WorkplaceAttendance"/> reports clock-in / clock-out
    /// (<see cref="OnWorkClockedIn"/> / <see cref="OnWorkClockedOut"/>), and while Working the objective
    /// line shows <see cref="WorkShiftController.ObjectiveText"/>. Clock-out is accepted any time; the
    /// shift scores whatever was left undone.
    ///
    /// Interactions call in: WorkplaceAttendance (clock in/out), <see cref="OnEnteredHome"/> (HomeArrival
    /// trigger), <see cref="UseBed"/> (Bed). A day rollover raises <see cref="DayStarted"/>.
    ///
    /// The routine, not a clock, sets the lighting: a day starting sets <see cref="LightingEnvironment"/> to
    /// Day, a clock-out sets it to Night (optional reference - the loop works without it). Sleeping is a
    /// separate permission, <see cref="CanSleep"/>: false until the day's clock-out, false again once a new
    /// day starts. The bed checks that, never the lighting state.
    /// </summary>
    public class DailyLifeDirector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ObjectiveHUD objectiveHud;
        [Tooltip("The day's work - only read for the objective line while Working.")]
        [SerializeField] private WorkShiftController workShift;
        [Tooltip("Player root - moved back to homeSpawn on day rollover.")]
        [SerializeField] private Transform player;
        [Tooltip("Where the player is placed at the start of each day. Position/rotation only - not used for any phase test.")]
        [SerializeField] private Transform homeSpawn;
        [Tooltip("Optional. Set to Day when a day starts and to Night on clock-out.")]
        [SerializeField] private LightingEnvironment lighting;

        [Header("Messages")]
        [SerializeField] private string notTimeToSleepMessage = "아직 잘 시간이 아니다.";

        [Header("Setup")]
        [Tooltip("Day number the first day starts on.")]
        [SerializeField] private int startDay = 1;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private int day = 1;
        [SerializeField] private DayPhase phase = DayPhase.CommuteToWork;
        [SerializeField] private bool canSleep;

        private string _lastObjective;

        public int Day => day;
        public DayPhase Phase => phase;
        /// <summary>Whether the bed may end the day: true only after today's clock-out. Independent of the lighting state.</summary>
        public bool CanSleep => canSleep;

        /// <summary>Raised after the bed rolls the day over, with the new day number. Listeners reset their own per-day state.</summary>
        public event System.Action<int> DayStarted;

        private void Awake()
        {
            day = Mathf.Max(1, startDay);
        }

        private void Start()
        {
            phase = DayPhase.CommuteToWork;
            canSleep = false;
            SetLighting(LightingEnvironment.LightingState.Day);
            RefreshObjective(force: true);
        }

        private void Update()
        {
            RefreshObjective(force: false);
        }

        // --- interactions ------------------------------------------------

        /// <summary>WorkplaceAttendance: clock-in succeeded.</summary>
        public void OnWorkClockedIn()
        {
            if (phase != DayPhase.CommuteToWork)
                return;
            SetPhase(DayPhase.Working);
            Debug.Log($"[DailyLife] Day {day}: clocked IN.");
        }

        /// <summary>WorkplaceAttendance: clock-out succeeded.</summary>
        public void OnWorkClockedOut()
        {
            if (phase != DayPhase.Working)
                return;
            SetPhase(DayPhase.CommuteHome);
            canSleep = true;
            SetLighting(LightingEnvironment.LightingState.Night);
            Debug.Log($"[DailyLife] Day {day}: clocked OUT.");
        }

        /// <summary>
        /// Legacy TestWorld_Old CardTerminal entry point. The Workplace readers replaced it; kept only so
        /// the old component still compiles. Does nothing.
        /// </summary>
        public void UseTerminal()
        {
            Debug.Log("[DailyLife] Legacy CardTerminal ignored - use the Workplace card readers.");
        }

        /// <summary>
        /// HomeArrival trigger: the player walked back into the house. After a clock-out this is the normal
        /// way home (CommuteHome -> Sleep). Coming home without clocking out is not blocked, but the phase
        /// stays Working and <see cref="CanSleep"/> stays false - the player has to go back and clock out.
        /// </summary>
        public void OnEnteredHome()
        {
            if (phase == DayPhase.Working)
            {
                Debug.Log($"[DailyLife] Day {day}: home without clocking out - can't sleep until clock-out.");
                return;
            }
            if (phase == DayPhase.CommuteHome)
            {
                SetPhase(DayPhase.Sleep);
                Debug.Log($"[DailyLife] Day {day}: home. Use the bed to sleep.");
            }
        }

        /// <summary>Bed: end the day and roll to the next. Gated on <see cref="CanSleep"/> (short notice if not) and the Sleep phase.</summary>
        public void UseBed()
        {
            if (!canSleep)
            {
                if (objectiveHud != null)
                    objectiveHud.ShowNotice(notTimeToSleepMessage);
                return;
            }
            if (phase != DayPhase.Sleep)
                return;

            if (player != null && homeSpawn != null)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false; // teleport past the CharacterController's own collision
                player.SetPositionAndRotation(homeSpawn.position, homeSpawn.rotation);
                if (cc != null) cc.enabled = true;
            }

            day++;
            canSleep = false;
            SetPhase(DayPhase.CommuteToWork);
            SetLighting(LightingEnvironment.LightingState.Day);
            Debug.Log($"[DailyLife] Slept. Day {day} begins.");
            DayStarted?.Invoke(day);
        }

        private void SetLighting(LightingEnvironment.LightingState state)
        {
            if (lighting != null)
                lighting.SetState(state);
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
                case DayPhase.Working:       s = workShift != null ? workShift.ObjectiveText : "작업하기"; break;
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
