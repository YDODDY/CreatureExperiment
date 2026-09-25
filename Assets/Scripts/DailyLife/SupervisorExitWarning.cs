using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Trigger on the Workplace exit route. If the player walks in with the day's work done but not
    /// clocked out, and hasn't been warned yet today, the Supervisor stops them once: player control is
    /// locked (<see cref="PlayerControlLock"/>), the view turns to <see cref="lookTarget"/> over
    /// <see cref="focusDuration"/>, <see cref="DialogueUI"/> shows the line, and Space (the "Jump" action -
    /// dialogue advance / confirm) closes it and gives control back. E does not advance dialogue. It only warns - no clock-out, no blocking, no teleport; the player may
    /// still walk out. A new day (<see cref="DailyLifeDirector.DayStarted"/>) allows one warning again.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SupervisorExitWarning : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DailyLifeDirector director;
        [SerializeField] private WorkplaceAttendance attendance;
        [SerializeField] private WorkShiftController workShift;
        [SerializeField] private DialogueUI dialogue;
        [Tooltip("Point on the Supervisor the player's view turns to (face / upper chest).")]
        [SerializeField] private Transform lookTarget;
        [SerializeField] private InputActionAsset inputActions;
        [Tooltip("Action that closes the line (Space).")]
        [SerializeField] private string advanceActionName = "Jump";
        [SerializeField] private string playerTag = "Player";

        [Header("Line")]
        [SerializeField] private string speakerName = "상사";
        [SerializeField] private string line = "퇴근 처리하고 가세요.";

        [Header("Focus")]
        [SerializeField] private float focusDuration = 0.4f;
        [Tooltip("The line can't be closed before it has been up this long (stops the entering press / a held key closing it at once).")]
        [SerializeField] private float minShowSeconds = 0.3f;

        [Header("State (read-only, for debugging)")]
        [SerializeField] private bool warningShownToday;

        private InputAction _advance;
        private bool _running;

        public bool WarningShownToday => warningShownToday;

        /// <summary>Work for today is done (quota resolved), the player is still clocked in, and no warning yet today.</summary>
        public bool ShouldWarn =>
            !warningShownToday && !_running
            && attendance != null && attendance.State == AttendanceState.ClockedIn
            && workShift != null && workShift.CanAcceptWork
            && workShift.Current.resolvedCount >= workShift.DailyQuota;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void Awake()
        {
            if (inputActions != null)
                _advance = inputActions.FindActionMap("Player", throwIfNotFound: false)?.FindAction(advanceActionName, throwIfNotFound: false);
        }

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

        private void OnDayStarted(int day) => warningShownToday = false;

        private void OnTriggerEnter(Collider other)
        {
            if (!string.IsNullOrEmpty(playerTag) && !other.CompareTag(playerTag))
                return;
            if (!ShouldWarn)
                return;
            StartCoroutine(RunWarning(other.gameObject));
        }

        private IEnumerator RunWarning(GameObject player)
        {
            _running = true;
            warningShownToday = true;

            PlayerControlLock controlLock = PlayerControlLock.Acquire(player);
            var look = player.GetComponent<PlayerLook>();
            Camera cam = Camera.main;

            // Turn the existing first-person view to the Supervisor (PlayerLook is off, so only this moves it).
            if (look != null && cam != null && lookTarget != null)
            {
                Vector3 dir = lookTarget.position - cam.transform.position;
                float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float targetPitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
                float startYaw = player.transform.eulerAngles.y;
                float startPitch = -Mathf.Asin(Mathf.Clamp(cam.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;

                for (float t = 0f; t < focusDuration; t += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / focusDuration);
                    look.SetLookAngles(Mathf.LerpAngle(startYaw, targetYaw, k), Mathf.Lerp(startPitch, targetPitch, k));
                    yield return null;
                }
                look.SetLookAngles(targetYaw, targetPitch);
            }

            if (dialogue != null)
            {
                dialogue.Show(speakerName, line);
                // The lock switched PlayerMovement off, which also disabled the shared Jump (Space) action.
                _advance?.Enable();

                float shownAt = Time.time;
                yield return null;
                while (_advance == null || Time.time - shownAt < minShowSeconds || !_advance.WasPressedThisFrame())
                {
                    if (_advance == null && Time.time - shownAt > 2f)
                        break; // no input asset wired - don't trap the player
                    yield return null;
                }
                dialogue.Hide();
            }

            // Give control back a frame later so the closing Space press isn't also read as a Jump.
            yield return null;
            controlLock.Release();
            _running = false;
        }
    }
}
