using UnityEngine;
using UnityEngine.InputSystem;
using CreatureExperiment.Creature.Brain;

namespace CreatureExperiment.Player
{
    /// <summary>
    /// TEMPORARY diagnostic ONLY - camera jitter regression investigation. Reads transforms and one
    /// PlayerLook/PlayerObservation debug seam; writes nothing to any gameplay transform or state.
    /// Delete this file + the DebugRawLookInput/DebugYaw/DebugPitch getters on PlayerLook once the
    /// jitter root cause is found and fixed.
    ///
    /// Purpose: find out WHICH transform changes, WHEN in the frame, and by HOW MUCH, without assuming
    /// the cause. Takes two snapshots per frame:
    ///   - "UpdateEnd"     : in this component's own Update() - see execution order note below.
    ///   - "LateUpdateEnd" : in this component's own LateUpdate().
    /// Comparing UpdateEnd(this frame) against LateUpdateEnd(PREVIOUS frame) isolates what changed
    /// DURING this frame's Update phase; comparing LateUpdateEnd(this frame) against UpdateEnd(this
    /// frame) isolates what changed DURING this frame's LateUpdate phase - directly answering "did it
    /// already move in Update, or did something re-write it afterwards in LateUpdate".
    ///
    /// Execution order: [DefaultExecutionOrder(32000)] puts BOTH this component's Update() and
    /// LateUpdate() after every default-order (0) script's same-phase method - so UpdateEnd is taken
    /// after PlayerMovement/PlayerLook/PlayerInteractor's Update(), and LateUpdateEnd is taken after
    /// PlayerObservation's LateUpdate() (also default order), letting the PlayerObservation cross-check
    /// below compare like-for-like, same-frame values instead of racing it.
    ///
    /// Logs only frames where something actually moved (past a small epsilon) or raw look input was
    /// non-zero - never a fixed-interval spam. The OnGUI HUD instead updates every frame unconditionally
    /// so the numbers can be watched live while standing still.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class PlayerCameraJitterDebug : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform cameraHolder;
        [SerializeField] private Transform mainCamera;
        [SerializeField] private PlayerLook playerLook;
        [SerializeField] private PlayerObservation playerObservation;

        [Header("Log thresholds (a frame logs to Console if ANY is exceeded)")]
        [Tooltip("Raw Look action magnitude above which the frame is considered 'mouse moved'.")]
        [SerializeField] private float rawLookEpsilon = 0.0001f;
        [Tooltip("Degrees of rotation change (full-frame, any tracked transform) above which the frame logs.")]
        [SerializeField] private float rotationEpsilonDeg = 0.001f;
        [Tooltip("Metres of position change (full-frame, any tracked transform) above which the frame logs.")]
        [SerializeField] private float positionEpsilon = 0.0002f;

        [Tooltip("Also print a Console line on every logged frame. The OnGUI HUD always shows the latest numbers regardless.")]
        [SerializeField] private bool logToConsole = true;
        [SerializeField] private Vector2 hudOrigin = new Vector2(12f, 12f);

        private struct Snapshot
        {
            public Vector3 playerPos;
            public Quaternion playerRot;
            public Vector3 holderLocalPos;
            public Quaternion holderLocalRot;
            public Vector3 camLocalPos;
            public Quaternion camLocalRot;
            public Vector3 camWorldPos;
            public Quaternion camWorldRot;
        }

        private Snapshot _prevFrameEnd;  // LateUpdateEnd of the PREVIOUS frame
        private Snapshot _thisUpdateEnd; // UpdateEnd of THIS frame
        private bool _initialized;

        private string _hud = "";

        private void Awake()
        {
            if (playerRoot == null)
                playerRoot = transform;
            if (playerLook == null)
                playerLook = GetComponent<PlayerLook>();
            if (playerObservation == null)
                playerObservation = GetComponent<PlayerObservation>();
            if (playerObservation == null)
                playerObservation = FindFirstObjectByType<PlayerObservation>();

            if (cameraHolder == null)
            {
                var found = transform.Find("CameraHolder");
                cameraHolder = found != null ? found : GetComponentInChildren<Camera>()?.transform.parent;
            }
            if (mainCamera == null)
            {
                var cam = cameraHolder != null ? cameraHolder.GetComponentInChildren<Camera>() : GetComponentInChildren<Camera>();
                mainCamera = cam != null ? cam.transform : null;
            }
        }

        private Snapshot Take()
        {
            return new Snapshot
            {
                playerPos = playerRoot.position,
                playerRot = playerRoot.rotation,
                holderLocalPos = cameraHolder != null ? cameraHolder.localPosition : Vector3.zero,
                holderLocalRot = cameraHolder != null ? cameraHolder.localRotation : Quaternion.identity,
                camLocalPos = mainCamera != null ? mainCamera.localPosition : Vector3.zero,
                camLocalRot = mainCamera != null ? mainCamera.localRotation : Quaternion.identity,
                camWorldPos = mainCamera != null ? mainCamera.position : Vector3.zero,
                camWorldRot = mainCamera != null ? mainCamera.rotation : Quaternion.identity,
            };
        }

        private void Update()
        {
            if (playerRoot == null)
                return;

            if (!_initialized)
            {
                _prevFrameEnd = Take();
                _thisUpdateEnd = _prevFrameEnd;
                _initialized = true;
                return;
            }

            _thisUpdateEnd = Take(); // runs after PlayerMovement/PlayerLook/PlayerInteractor's Update() - see DefaultExecutionOrder note
        }

        private void LateUpdate()
        {
            if (!_initialized || playerRoot == null)
                return;

            Snapshot lateEnd = Take(); // runs after PlayerObservation's LateUpdate() - see DefaultExecutionOrder note
            float dt = Time.deltaTime;

            Vector2 rawLook = playerLook != null ? playerLook.DebugRawLookInput : Vector2.zero;
            string activeControl = playerLook != null ? playerLook.DebugActiveControlPath : "-";

            // PlayerLook's own inline chain snapshots (captured INSIDE PlayerLook.Update() at the exact
            // instant each value exists - see PlayerLook.cs). No execution-order inference needed here.
            float yawBefore = playerLook != null ? playerLook.DebugYawBefore : 0f;
            float yawAfter = playerLook != null ? playerLook.DebugYawAfter : 0f;
            float yawDelta = playerLook != null ? playerLook.DebugYawDelta : 0f;
            float pitchBefore = playerLook != null ? playerLook.DebugPitchBefore : 0f;
            float pitchAfter = playerLook != null ? playerLook.DebugPitchAfter : 0f;
            float pitchDelta = playerLook != null ? playerLook.DebugPitchDelta : 0f;

            Quaternion beforeWriteRot = playerLook != null ? playerLook.DebugLocalRotationBeforeWrite : Quaternion.identity;
            Quaternion targetRot = playerLook != null ? playerLook.DebugTargetRotation : Quaternion.identity;
            Quaternion afterWriteRot = playerLook != null ? playerLook.DebugLocalRotationAfterWrite : Quaternion.identity;
            float angleBeforeToTarget = Quaternion.Angle(beforeWriteRot, targetRot);
            float angleTargetToAfter = Quaternion.Angle(targetRot, afterWriteRot);

            // Section 5 (still kept): value the INSTANT PlayerLook wrote it this frame, vs what we read
            // moments later in the same Update phase (our Update() runs after PlayerLook's, same frame -
            // see DefaultExecutionOrder note). If these differ, something rewrote Player's rotation
            // between PlayerLook's own write and the end of this frame's Update phase.
            float postLookToOurUpdateEnd = playerLook != null
                ? Quaternion.Angle(afterWriteRot, _thisUpdateEnd.playerRot)
                : -1f;

            // Section 6/7: is a Gamepad/Joystick connected right now and reporting non-zero stick data?
            // The "Look" action (see InputSystem_Actions.inputactions) binds <Pointer>/delta AND
            // <Gamepad>/rightStick AND <Joystick>/{Hatswitch} - if ANY such device is present with
            // resting analog noise, PlayerLook.DebugRawLookInput can read non-zero even though the
            // mouse itself never moved. Read-only device queries, no gameplay effect.
            bool gamepadPresent = Gamepad.current != null;
            Vector2 gamepadRightStick = gamepadPresent ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;
            bool joystickPresent = Joystick.current != null;

            // --- full-frame deltas (prev frame's LateUpdateEnd -> this frame's LateUpdateEnd) --------
            // This is the EXACT SAME span PlayerObservation.CurrentViewAngularSpeed measures (prevViewRot
            // was captured in PlayerObservation's own LateUpdate last frame; ours runs right after it).
            float playerRotFull = Quaternion.Angle(_prevFrameEnd.playerRot, lateEnd.playerRot);
            float holderRotFull = Quaternion.Angle(_prevFrameEnd.holderLocalRot, lateEnd.holderLocalRot);
            float camLocalRotFull = Quaternion.Angle(_prevFrameEnd.camLocalRot, lateEnd.camLocalRot);
            float camWorldRotFull = Quaternion.Angle(_prevFrameEnd.camWorldRot, lateEnd.camWorldRot);

            Vector3 playerPosFull = lateEnd.playerPos - _prevFrameEnd.playerPos;
            Vector3 holderPosFull = lateEnd.holderLocalPos - _prevFrameEnd.holderLocalPos;
            Vector3 camLocalPosFull = lateEnd.camLocalPos - _prevFrameEnd.camLocalPos;
            Vector3 camWorldPosFull = lateEnd.camWorldPos - _prevFrameEnd.camWorldPos;

            // --- phase split: Update phase (prevEnd -> thisUpdateEnd) vs LateUpdate phase (thisUpdateEnd -> lateEnd) ---
            float playerRotUpdatePhase = Quaternion.Angle(_prevFrameEnd.playerRot, _thisUpdateEnd.playerRot);
            float playerRotLatePhase = Quaternion.Angle(_thisUpdateEnd.playerRot, lateEnd.playerRot);
            float holderRotUpdatePhase = Quaternion.Angle(_prevFrameEnd.holderLocalRot, _thisUpdateEnd.holderLocalRot);
            float holderRotLatePhase = Quaternion.Angle(_thisUpdateEnd.holderLocalRot, lateEnd.holderLocalRot);
            float camWorldRotUpdatePhase = Quaternion.Angle(_prevFrameEnd.camWorldRot, _thisUpdateEnd.camWorldRot);
            float camWorldRotLatePhase = Quaternion.Angle(_thisUpdateEnd.camWorldRot, lateEnd.camWorldRot);

            Vector3 playerPosUpdatePhase = _thisUpdateEnd.playerPos - _prevFrameEnd.playerPos;
            Vector3 playerPosLatePhase = lateEnd.playerPos - _thisUpdateEnd.playerPos;
            Vector3 camWorldPosUpdatePhase = _thisUpdateEnd.camWorldPos - _prevFrameEnd.camWorldPos;
            Vector3 camWorldPosLatePhase = lateEnd.camWorldPos - _thisUpdateEnd.camWorldPos;

            // --- PlayerObservation cross-check (section 9) ------------------------------------------
            float obsCurrent = playerObservation != null ? playerObservation.CurrentViewAngularSpeed : -1f;
            float debuggerComputedSpeed = dt > 0f ? camWorldRotFull / dt : 0f;
            float obsDiff = obsCurrent >= 0f ? Mathf.Abs(obsCurrent - debuggerComputedSpeed) : -1f;

            bool notable = rawLook.sqrMagnitude > rawLookEpsilon * rawLookEpsilon
                           || Mathf.Abs(yawDelta) > rotationEpsilonDeg
                           || Mathf.Abs(pitchDelta) > rotationEpsilonDeg
                           || angleBeforeToTarget > rotationEpsilonDeg
                           || angleTargetToAfter > rotationEpsilonDeg
                           || playerRotFull > rotationEpsilonDeg
                           || holderRotFull > rotationEpsilonDeg
                           || camLocalRotFull > rotationEpsilonDeg
                           || camWorldRotFull > rotationEpsilonDeg
                           || playerPosFull.magnitude > positionEpsilon
                           || holderPosFull.magnitude > positionEpsilon
                           || camLocalPosFull.magnitude > positionEpsilon
                           || camWorldPosFull.magnitude > positionEpsilon;

            _hud =
                $"FRAME {Time.frameCount}  dt={dt * 1000f:F2}ms\n" +
                $"=== RAW LOOK ===\n" +
                $"x={rawLook.x:F8}  y={rawLook.y:F8}\n" +
                $"activeControl: {activeControl}\n" +
                $"Gamepad: {(gamepadPresent ? $"PRESENT rightStick=({gamepadRightStick.x:F4},{gamepadRightStick.y:F4})" : "none")}   Joystick: {(joystickPresent ? "PRESENT" : "none")}\n" +
                $"=== PLAYERLOOK INTERNAL ===\n" +
                $"Yaw   Before={yawBefore:F8}  After={yawAfter:F8}  Delta={yawDelta:F8}\n" +
                $"Pitch Before={pitchBefore:F8}  After={pitchAfter:F8}  Delta={pitchDelta:F8}\n" +
                $"=== ROTATION WRITE (Player root local) ===\n" +
                $"BeforeWrite EulerY={beforeWriteRot.eulerAngles.y:F6}  ({beforeWriteRot.x:F6},{beforeWriteRot.y:F6},{beforeWriteRot.z:F6},{beforeWriteRot.w:F6})\n" +
                $"Target      EulerY={targetRot.eulerAngles.y:F6}  ({targetRot.x:F6},{targetRot.y:F6},{targetRot.z:F6},{targetRot.w:F6})\n" +
                $"AfterWrite  EulerY={afterWriteRot.eulerAngles.y:F6}  ({afterWriteRot.x:F6},{afterWriteRot.y:F6},{afterWriteRot.z:F6},{afterWriteRot.w:F6})\n" +
                $"Angle(Before,Target)={angleBeforeToTarget:F6}deg   Angle(Target,After)={angleTargetToAfter:F6}deg\n" +
                $"=== frame-end state (matches PlayerObservation's own window) ===\n" +
                $"Player     PosΔ={playerPosFull.magnitude:F6}m  RotΔ={playerRotFull:F6}deg\n" +
                $"Holder(loc)PosΔ={holderPosFull.magnitude:F6}m  RotΔ={holderRotFull:F6}deg\n" +
                $"Cam(local) PosΔ={camLocalPosFull.magnitude:F6}m  RotΔ={camLocalRotFull:F6}deg\n" +
                $"Cam(world) PosΔ={camWorldPosFull.magnitude:F6}m  RotΔ={camWorldRotFull:F6}deg\n" +
                $"--- phase split (Update-phase vs LateUpdate-phase, this frame) ---\n" +
                $"Player     RotΔ  Update={playerRotUpdatePhase:F6}  LateUpdate={playerRotLatePhase:F6}\n" +
                $"Holder(loc)RotΔ  Update={holderRotUpdatePhase:F6}  LateUpdate={holderRotLatePhase:F6}\n" +
                $"Cam(world) RotΔ  Update={camWorldRotUpdatePhase:F6}  LateUpdate={camWorldRotLatePhase:F6}\n" +
                $"Player     PosΔ  Update={playerPosUpdatePhase.magnitude:F6}  LateUpdate={playerPosLatePhase.magnitude:F6}\n" +
                $"Cam(world) PosΔ  Update={camWorldPosUpdatePhase.magnitude:F6}  LateUpdate={camWorldPosLatePhase.magnitude:F6}\n" +
                $"AfterWrite vs our Update-end read: RotΔ={postLookToOurUpdateEnd:F6}deg  (>0 => something rewrote it between PlayerLook's write and end of Update phase)\n" +
                $"--- PlayerObservation cross-check ---\n" +
                $"Obs.CurrentViewAngularSpeed={obsCurrent:F3} deg/s   DebuggerComputed={debuggerComputedSpeed:F3} deg/s   diff={obsDiff:F3}";

            if (notable && logToConsole)
            {
                Debug.Log(
                    $"[JitterDebug] FRAME {Time.frameCount} dt={dt:F4} " +
                    $"RawLook=({rawLook.x:F8},{rawLook.y:F8}) activeControl={activeControl} " +
                    $"Gamepad={(gamepadPresent ? gamepadRightStick.ToString("F4") : "none")} Joystick={(joystickPresent ? "present" : "none")} | " +
                    $"Yaw before={yawBefore:F8} after={yawAfter:F8} Δ={yawDelta:F8} | " +
                    $"Pitch before={pitchBefore:F8} after={pitchAfter:F8} Δ={pitchDelta:F8} | " +
                    $"Angle(Before,Target)={angleBeforeToTarget:F6} Angle(Target,After)={angleTargetToAfter:F6} | " +
                    $"Player PosΔ={playerPosFull.magnitude:F6} RotΔ={playerRotFull:F6} " +
                    $"(upd={playerRotUpdatePhase:F6} late={playerRotLatePhase:F6}) postLookΔ={postLookToOurUpdateEnd:F6} | " +
                    $"Holder RotΔ={holderRotFull:F6} (upd={holderRotUpdatePhase:F6} late={holderRotLatePhase:F6}) | " +
                    $"CamLocal PosΔ={camLocalPosFull.magnitude:F6} RotΔ={camLocalRotFull:F6} | " +
                    $"CamWorld PosΔ={camWorldPosFull.magnitude:F6} RotΔ={camWorldRotFull:F6} " +
                    $"(upd={camWorldRotUpdatePhase:F6} late={camWorldRotLatePhase:F6}) | " +
                    $"Obs={obsCurrent:F3} Computed={debuggerComputedSpeed:F3} diff={obsDiff:F3}");
            }

            _prevFrameEnd = lateEnd;
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                richText = false,
            };
            style.normal.textColor = Color.white;

            var rect = new Rect(hudOrigin.x, hudOrigin.y, 720f, 480f);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f), _hud, style);
        }
    }
}
