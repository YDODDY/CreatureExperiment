using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One of the two wall card readers at the WorkArea door: the Prep side clocks in, the Work Floor
    /// side clocks out. It only forwards to <see cref="WorkplaceAttendance"/>, which decides whether the
    /// press applies. Used through <c>PlayerInteractor</c>'s World Use like any other <see cref="IUsable"/>.
    /// Not a pickup - no <c>Interactable</c>.
    /// </summary>
    public class WorkplaceCardReader : MonoBehaviour, IUsable, IFocusTarget
    {
        public enum Mode
        {
            ClockIn,
            ClockOut
        }

        [Header("References")]
        [SerializeField] private WorkplaceAttendance attendance;
        [Tooltip("Renderer of the outline child - enabled only while focused.")]
        [SerializeField] private Renderer outlineRenderer;

        [Header("Setup")]
        [SerializeField] private Mode mode = Mode.ClockIn;

        public string FocusName
        {
            get
            {
                AttendanceState s = attendance != null ? attendance.State : AttendanceState.NotClockedIn;
                if (mode == Mode.ClockIn)
                    return s == AttendanceState.NotClockedIn ? "출근 카드 찍기" : "출근 처리됨";
                switch (s)
                {
                    case AttendanceState.ClockedIn: return "퇴근 카드 찍기";
                    case AttendanceState.ClockedOut: return "퇴근 처리됨";
                    default: return "출근 기록 없음";
                }
            }
        }

        public Transform FocusTransform => transform;

        private void Awake()
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = false;
        }

        public void SetFocused(bool focused)
        {
            if (outlineRenderer != null)
                outlineRenderer.enabled = focused;
        }

        public void Use()
        {
            if (attendance == null)
                return;
            if (mode == Mode.ClockIn)
                attendance.TryClockIn();
            else
                attendance.TryClockOut();
        }
    }
}
