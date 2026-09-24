using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    public enum AttendanceState
    {
        NotClockedIn,
        ClockedIn,
        ClockedOut
    }

    /// <summary>
    /// Attendance for the Workplace (2F Prep / Work Floor). The two wall readers call
    /// <see cref="TryClockIn"/> / <see cref="TryClockOut"/>; this decides whether it applies, owns the
    /// WorkArea door's lock, and passes clock-in / clock-out on to the <see cref="WorkShiftController"/>
    /// and the <see cref="DailyLifeDirector"/>'s day phase.
    ///
    /// The door lock is per DAY, not per attendance: the first successful clock-in unlocks it and it
    /// stays unlocked through clock-out until the next day starts. Clocking out only changes
    /// <see cref="State"/> and fixes the shift's result (<see cref="WorkShiftController.EndShift"/>) -
    /// it is accepted any time while clocked in; unfinished work is scored, not blocked.
    ///
    /// Missing a clock-out is only recorded, never punished or blocked: <see cref="IsMissingClockOut"/>
    /// is true while clocked in but not out, and <see cref="PreviousDayMissedClockOut"/> keeps that
    /// answer for the day that just ended.
    ///
    /// This is the only listener of <see cref="DailyLifeDirector.DayStarted"/> on the Workplace side;
    /// it resets the shift too, so the day reset has a single entry point.
    /// </summary>
    public class WorkplaceAttendance : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Day rollover source + day phase. Optional - without it the state only resets on scene start.")]
        [SerializeField] private DailyLifeDirector director;
        [Tooltip("The Prep <-> Work Floor door this attendance locks and unlocks.")]
        [SerializeField] private SwingDoor workAreaDoor;
        [Tooltip("The day's work - started on clock-in, ended on clock-out, reset on a new day. Optional.")]
        [SerializeField] private WorkShiftController workShift;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private AttendanceState state = AttendanceState.NotClockedIn;
        [SerializeField] private bool doorUnlockedForDay;
        [SerializeField] private bool previousDayMissedClockOut;

        public AttendanceState State => state;
        public bool DoorUnlockedForDay => doorUnlockedForDay;
        /// <summary>Clocked in today and not clocked out (yet). Read at day end, this is "left without clocking out".</summary>
        public bool IsMissingClockOut => state == AttendanceState.ClockedIn;
        /// <summary>Whether the day that last ended had a clock-in with no clock-out.</summary>
        public bool PreviousDayMissedClockOut => previousDayMissedClockOut;
        /// <summary>Clock-out is accepted any time while clocked in; unfinished work is scored, not blocked.</summary>
        public bool CanClockOut => state == AttendanceState.ClockedIn;

        private void OnEnable()
        {
            if (director != null)
                director.DayStarted += OnDayStarted;
        }

        private void OnDisable()
        {
            if (director != null)
                director.DayStarted -= OnDayStarted;
        }

        private void Start()
        {
            ResetDay();
        }

        /// <summary>ClockIn reader. Only from NotClockedIn; unlocks the WorkArea door for the rest of the day and starts the shift.</summary>
        public bool TryClockIn()
        {
            if (state != AttendanceState.NotClockedIn)
                return false;

            state = AttendanceState.ClockedIn;
            doorUnlockedForDay = true;
            ApplyDoorLock();
            if (workShift != null)
                workShift.OnClockedIn();
            if (director != null)
                director.OnWorkClockedIn();
            Debug.Log("[Workplace] Clocked IN - WorkArea door unlocked for today.");
            return true;
        }

        /// <summary>ClockOut reader. Any time from ClockedIn - the shift's result is fixed; the door stays unlocked.</summary>
        public bool TryClockOut()
        {
            if (!CanClockOut)
                return false;

            if (workShift != null)
                workShift.EndShift();
            state = AttendanceState.ClockedOut;
            if (director != null)
                director.OnWorkClockedOut();
            Debug.Log("[Workplace] Clocked OUT.");
            return true;
        }

        private void OnDayStarted(int day)
        {
            previousDayMissedClockOut = IsMissingClockOut;
            if (previousDayMissedClockOut)
                Debug.Log($"[Workplace] Day {day - 1} ended without a clock-out.");
            ResetDay();
        }

        private void ResetDay()
        {
            state = AttendanceState.NotClockedIn;
            doorUnlockedForDay = false;
            // Closing the door is DoorDayResetManager's job; this only owns the lock.
            ApplyDoorLock();
            if (workShift != null)
                workShift.ResetDay();
        }

        private void ApplyDoorLock()
        {
            if (workAreaDoor != null)
                workAreaDoor.SetLocked(!doorUnlockedForDay);
        }
    }
}
