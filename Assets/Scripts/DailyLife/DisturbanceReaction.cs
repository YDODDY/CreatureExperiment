using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A shop owner's one-line reaction to something smashing in the shop (the pub's bartender): a glass shattering
    /// (<see cref="DrinkContainer.Shattered"/>) or a plate / bowl breaking (<see cref="Breakable.Broke"/>) inside
    /// <see cref="area"/> - "헤이, 뭐하는 거야!". The line shows over the owner's head (<see cref="bubble"/>) and as a HUD
    /// notice, and the owner turns toward where it happened for a moment, then back.
    ///
    /// One accident = one line: after a reaction everything is ignored for <see cref="cooldown"/> seconds (a full glass
    /// that shatters and spills, or several plates in a row, still get only the one line). No other consequence - no
    /// fine, no ban, no chase: pure atmosphere for now.
    /// </summary>
    public class DisturbanceReaction : MonoBehaviour
    {
        [Tooltip("Box (position / rotation / lossyScale) the owner cares about - the shop interior.")]
        [SerializeField] private Transform area;
        [SerializeField] private string speakerName = "주인장";
        [Tooltip("Used in turn, one per reaction.")]
        [SerializeField] private string[] lines = { "헤이, 뭐하는 거야!" };
        [Tooltip("Seconds after a reaction during which further breakages are ignored.")]
        [SerializeField] private float cooldown = 5f;

        [Header("Presentation")]
        [Tooltip("Optional world text over the owner's head, shown for bubbleSeconds and turned to the camera.")]
        [SerializeField] private TextMesh bubble;
        [SerializeField] private float bubbleSeconds = 2.5f;
        [Tooltip("Turned (yaw only) toward the incident for lookSeconds, then back to its resting rotation. Empty = this transform.")]
        [SerializeField] private Transform body;
        [SerializeField] private float lookSeconds = 2.5f;
        [SerializeField] private float turnSpeed = 300f;

        private float _readyAt, _bubbleUntil, _lookUntil;
        private int _next;
        private Vector3 _lookAt;
        private Quaternion _rest;
        private Camera _camera;

        public float LastReactionTime { get; private set; } = -1f;
        public int ReactionCount { get; private set; }

        private Transform Body => body != null ? body : transform;

        private void Awake()
        {
            _rest = Body.localRotation;
            if (bubble != null)
                bubble.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            DrinkContainer.Shattered += OnShattered;
            Breakable.Broke += OnBroke;
        }

        private void OnDisable()
        {
            DrinkContainer.Shattered -= OnShattered;
            Breakable.Broke -= OnBroke;
        }

        private void OnShattered(DrinkContainer glass, Vector3 point) => React(point);
        private void OnBroke(Breakable item, Vector3 point, float noise) => React(point);

        private void React(Vector3 point)
        {
            if (Time.time < _readyAt || lines == null || lines.Length == 0 || !Inside(point))
                return;
            _readyAt = Time.time + cooldown;
            string line = lines[_next++ % lines.Length];
            LastReactionTime = Time.time;
            ReactionCount++;

            ObjectiveHUD.Notice($"{speakerName}: {line}");
            if (bubble != null)
            {
                bubble.text = line;
                bubble.gameObject.SetActive(true);
                _bubbleUntil = Time.time + bubbleSeconds;
            }
            _lookAt = point;
            _lookUntil = Time.time + lookSeconds;
        }

        private bool Inside(Vector3 point)
        {
            if (area == null)
                return true;
            Vector3 local = Quaternion.Inverse(area.rotation) * (point - area.position);
            Vector3 half = area.lossyScale * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private void LateUpdate()
        {
            Transform t = Body;
            Quaternion target = _rest;
            if (Time.time < _lookUntil)
            {
                Vector3 to = _lookAt - t.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                {
                    Quaternion world = Quaternion.LookRotation(to, Vector3.up);
                    target = t.parent != null ? Quaternion.Inverse(t.parent.rotation) * world : world;
                }
            }
            t.localRotation = Quaternion.RotateTowards(t.localRotation, target, turnSpeed * Time.deltaTime);

            if (bubble == null || !bubble.gameObject.activeSelf)
                return;
            if (Time.time >= _bubbleUntil)
            {
                bubble.gameObject.SetActive(false);
                return;
            }
            if (_camera == null)
                _camera = Camera.main;
            if (_camera != null) // TextMesh reads along its -Z: face away from the camera
                bubble.transform.rotation = Quaternion.LookRotation(bubble.transform.position - _camera.transform.position, Vector3.up);
        }
    }
}
