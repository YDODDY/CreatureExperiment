using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Running water turned on and off with the Interact key (routed by <c>PlayerInteractor</c> through
    /// <see cref="IUsable"/> - this component reads no input). The "water" is just a visual object that is
    /// shown while on - no fluid, no collider. Used by the bathroom sink and the shower.
    /// Starts off unless <see cref="startOn"/>. <see cref="SetOn"/> lets other systems read or force it.
    /// </summary>
    public class WaterToggle : MonoBehaviour, IUsable, IFocusTarget
    {
        [Tooltip("Shown while the water is on.")]
        [SerializeField] private GameObject waterVisual;

        [Header("Focus label")]
        [SerializeField] private string turnOnPrompt = "물 틀기";
        [SerializeField] private string turnOffPrompt = "물 끄기";

        [SerializeField] private bool startOn;

        private bool _isOn;

        public bool IsOn => _isOn;

        public string FocusName => _isOn ? turnOffPrompt : turnOnPrompt;
        public Transform FocusTransform => transform;

        public void SetFocused(bool focused) { }

        private void Awake() => SetOn(startOn);

        public void Use() => SetOn(!_isOn);

        public void SetOn(bool on)
        {
            _isOn = on;
            if (waterVisual != null)
                waterVisual.SetActive(on);
        }
    }
}
