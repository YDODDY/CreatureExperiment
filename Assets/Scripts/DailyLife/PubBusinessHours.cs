using System.Collections.Generic;
using UnityEngine;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// The pub is a night-only business. It follows the project's own Day / Night state
    /// (<see cref="LightingEnvironment.State"/>, set by the daily routine) - no clock of its own.
    ///
    /// OPEN (Night): every light under <see cref="lightRoot"/> on (glow meshes lit), the bartender
    /// (<see cref="staff"/> - with his idle and his <see cref="DisturbanceReaction"/>) active, the
    /// <see cref="counter"/> taking orders, the sign reads OPEN, the doors are unlocked (closed by default,
    /// opened with E like any door).
    /// CLOSED (Day): lights and glows off, bartender gone, no orders, the sign reads CLOSED, and the front /
    /// rear doors are locked from the outside.
    ///
    /// Never traps the player: the doors are only locked while the player is NOT inside
    /// <see cref="interiorArea"/> (hall + restroom + backroom). Someone still inside when the pub closes can walk
    /// out through either door; once they are outside the doors lock, and a door left open snaps shut as soon as
    /// the player is <see cref="snapCloseDistance"/> away from it. No teleport, no push.
    /// A conversation at the bar finishes before the counter is switched off.
    /// </summary>
    public class PubBusinessHours : MonoBehaviour
    {
        [Tooltip("Found in the scene if empty.")]
        [SerializeField] private LightingEnvironment lighting;
        [SerializeField] private SwingDoor frontDoor;
        [SerializeField] private SwingDoor rearDoor;
        [Tooltip("Box (position / rotation / lossyScale) over everything indoors: hall, restroom, backroom.")]
        [SerializeField] private Transform interiorArea;

        [Header("Open only")]
        [Tooltip("The bartender (SetActive): his idle loop and breakage reaction go with him.")]
        [SerializeField] private GameObject staff;
        [SerializeField] private BaristaCounter counter;

        [Header("Sign")]
        [SerializeField] private TextMesh openSign;
        [SerializeField] private string openText = "OPEN";
        [SerializeField] private string closedText = "CLOSED";
        [SerializeField] private Color openColor = new Color(0.45f, 1f, 0.5f);
        [SerializeField] private Color closedColor = new Color(1f, 0.38f, 0.32f);

        [Header("Lights")]
        [Tooltip("Every Light under it is switched with the business hours.")]
        [SerializeField] private Transform lightRoot;
        [Tooltip("Emissive 'bulb' materials under the light root: swapped for the off material while closed.")]
        [SerializeField] private Material[] glowMaterials;
        [SerializeField] private Material glowOffMaterial;
        [Tooltip("Lit signage text dimmed while closed.")]
        [SerializeField] private TextMesh[] litTexts;
        [SerializeField] private float closedTextDim = 0.4f;

        [Header("Doors")]
        [SerializeField] private float snapCloseDistance = 3f;

        private Transform _player;
        private bool _hasApplied;
        private bool _open;
        private readonly List<Light> _lights = new List<Light>();
        private readonly List<Renderer> _glows = new List<Renderer>();
        private readonly List<Material> _glowOn = new List<Material>();
        private Color[] _textColors;

        public bool IsOpen => _open;
        public bool PlayerInside => _player != null && Inside(_player.position);

        private void Awake()
        {
            if (lighting == null)
                lighting = FindFirstObjectByType<LightingEnvironment>();
            var interactor = FindFirstObjectByType<PlayerInteractor>();
            _player = interactor != null ? interactor.transform : null;

            if (lightRoot != null)
            {
                _lights.AddRange(lightRoot.GetComponentsInChildren<Light>(true));
                var glowSet = new HashSet<Material>(glowMaterials ?? new Material[0]);
                foreach (var r in lightRoot.GetComponentsInChildren<Renderer>(true))
                    if (r.sharedMaterial != null && glowSet.Contains(r.sharedMaterial))
                    {
                        _glows.Add(r);
                        _glowOn.Add(r.sharedMaterial);
                    }
            }
            if (litTexts != null)
            {
                _textColors = new Color[litTexts.Length];
                for (int i = 0; i < litTexts.Length; i++)
                    if (litTexts[i] != null)
                        _textColors[i] = litTexts[i].color;
            }
        }

        private void Update()
        {
            bool open = lighting == null || lighting.State == LightingEnvironment.LightingState.Night;
            if (!_hasApplied || open != _open)
            {
                // Let a running order conversation finish before the counter goes.
                if (!(open == false && counter != null && counter.InConversation))
                    Apply(open);
            }

            bool lockNow = !open && !PlayerInside;
            Guard(frontDoor, lockNow);
            Guard(rearDoor, lockNow);
        }

        private void Apply(bool open)
        {
            _open = open;
            _hasApplied = true;

            foreach (var l in _lights)
                if (l != null)
                    l.enabled = open;
            for (int i = 0; i < _glows.Count; i++)
                if (_glows[i] != null)
                    _glows[i].sharedMaterial = open || glowOffMaterial == null ? _glowOn[i] : glowOffMaterial;
            if (litTexts != null)
                for (int i = 0; i < litTexts.Length; i++)
                    if (litTexts[i] != null)
                        litTexts[i].color = open ? _textColors[i] : _textColors[i] * new Color(closedTextDim, closedTextDim, closedTextDim, 1f);

            if (staff != null)
                staff.SetActive(open);
            if (counter != null)
                counter.enabled = open;
            if (openSign != null)
            {
                openSign.text = open ? openText : closedText;
                openSign.color = open ? openColor : closedColor;
            }
        }

        // Locked = only from the outside: while the player is indoors it stays unlocked (see the class summary).
        private void Guard(SwingDoor door, bool lockNow)
        {
            if (door == null)
                return;
            if (lockNow && door.IsOpen && !door.IsMoving && _player != null
                && (door.transform.position - _player.position).sqrMagnitude > snapCloseDistance * snapCloseDistance)
                door.SnapClosed();
            if (door.IsLocked != lockNow)
                door.SetLocked(lockNow);
        }

        private bool Inside(Vector3 point)
        {
            if (interiorArea == null)
                return false;
            Vector3 local = Quaternion.Inverse(interiorArea.rotation) * (point - interiorArea.position);
            Vector3 half = interiorArea.lossyScale * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }
    }
}
