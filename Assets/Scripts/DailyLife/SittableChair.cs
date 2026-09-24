using UnityEngine;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A chair the player sits on and gets up from with the Interact key (routed by
    /// <c>PlayerInteractor</c> through <see cref="IUsable"/> - this component reads no input).
    ///
    /// Sitting turns off PlayerMovement and the CharacterController, moves the player so the camera sits
    /// at <see cref="sitPoint"/>, and turns the view to sitPoint's forward with <see cref="sitPitch"/> down.
    /// Mouse look stays live. While seated the chair registers itself as the interactor's fallback use,
    /// so Interact with nothing else aimed stands up - the player doesn't have to look back at the chair.
    /// Standing puts the player's feet at <see cref="standPoint"/> and turns movement back on.
    ///
    /// Only one chair can be occupied; while seated, using any chair stands up from the current one.
    /// A held item stays in hand (it rides the camera's hold anchor).
    /// </summary>
    public class SittableChair : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("Poses")]
        [Tooltip("Where the camera (eye) goes while seated; its forward is the seated view direction.")]
        [SerializeField] private Transform sitPoint;
        [Tooltip("Where the player's feet go on standing up - clear of the chair and the table.")]
        [SerializeField] private Transform standPoint;
        [Tooltip("Initial downward look while seated, degrees.")]
        [SerializeField] private float sitPitch = 25f;
        [Tooltip("Label anchor while seated (the chair itself is usually out of view). Falls back to this transform.")]
        [SerializeField] private Transform seatedLabelAnchor;

        [Header("Focus label")]
        [SerializeField] private string sitPrompt = "앉기";
        [SerializeField] private string standPrompt = "일어서기";

        [Header("Player (found in the scene if empty)")]
        [SerializeField] private PlayerInteractor player;

        private static SittableChair s_occupied;

        private CharacterController _controller;
        private PlayerMovement _movement;
        private PlayerLook _look;

        /// <summary>The chair the player is sitting on, or null.</summary>
        public static SittableChair Occupied => s_occupied;
        public bool IsSeated => s_occupied == this;

        public string FocusName => s_occupied != null ? standPrompt : sitPrompt;
        public Transform FocusTransform => IsSeated && seatedLabelAnchor != null ? seatedLabelAnchor : transform;

        public void SetFocused(bool focused) { }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_occupied = null;

        private void Awake()
        {
            if (player == null)
                player = FindFirstObjectByType<PlayerInteractor>();
            if (player != null)
            {
                _controller = player.GetComponent<CharacterController>();
                _movement = player.GetComponent<PlayerMovement>();
                _look = player.GetComponent<PlayerLook>();
            }
        }

        private void OnDisable()
        {
            if (IsSeated)
                StandUp();
        }

        public void Use()
        {
            if (s_occupied != null)
                s_occupied.StandUp();
            else
                SitDown();
        }

        private void SitDown()
        {
            if (player == null || sitPoint == null)
                return;

            // Camera height above the player's feet right now (standing or crouched).
            Camera cam = Camera.main;
            float eyeOffset = cam != null ? cam.transform.position.y - player.transform.position.y : 1.6f;

            if (_movement != null) _movement.enabled = false;
            if (_controller != null) _controller.enabled = false;

            Vector3 eye = sitPoint.position;
            player.transform.position = new Vector3(eye.x, eye.y - eyeOffset, eye.z);
            if (_look != null)
                _look.SetLookAngles(sitPoint.eulerAngles.y, sitPitch);

            // The chair's box reaches the backrest top, just under the seated eye - it would catch the
            // aim meant for the table. The player's controller is off while seated, so it isn't needed.
            SetChairCollider(false);

            s_occupied = this;
            player.SetFallbackUse(this);
        }

        private void StandUp()
        {
            if (player != null)
            {
                if (standPoint != null)
                    player.transform.position = standPoint.position;
                if (_controller != null) _controller.enabled = true;
                if (_movement != null) _movement.enabled = true;
                if (_look != null)
                    _look.SetLookAngles(player.transform.eulerAngles.y, 0f);
                player.SetFallbackUse(null);
            }
            SetChairCollider(true);
            if (s_occupied == this)
                s_occupied = null;
        }

        private void SetChairCollider(bool on)
        {
            foreach (var col in GetComponents<Collider>())
                col.enabled = on;
        }
    }
}
