using UnityEngine;
using CreatureExperiment.Interaction;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// A wall switch that turns one ceiling light on and off with the Interact key (routed by
    /// <c>PlayerInteractor</c> through <see cref="IUsable"/> - this component reads no input). It references
    /// its own <see cref="targetLight"/> directly; there is no shared power system. The fixture's lens
    /// renderer swaps between a bright and a dull material so the fixture itself reads on / off, and the
    /// small toggle part tilts to the matching side.
    /// Starts on unless <see cref="startOn"/> is cleared. <see cref="SetOn"/> lets other systems read or
    /// force the state; nothing calls it yet.
    /// </summary>
    public class LightSwitch : MonoBehaviour, IUsable, IFocusTarget
    {
        [Header("Target")]
        [SerializeField] private Light targetLight;
        [Tooltip("Fixture surface that shows the on / off material.")]
        [SerializeField] private Renderer fixtureSurface;
        [SerializeField] private Material onMaterial;
        [SerializeField] private Material offMaterial;

        [Header("Switch visual")]
        [Tooltip("Tilted about its local X axis to show the state.")]
        [SerializeField] private Transform toggle;
        [SerializeField] private float toggleTiltDegrees = 12f;

        [Header("Focus label")]
        [SerializeField] private string turnOnPrompt = "불 켜기";
        [SerializeField] private string turnOffPrompt = "불 끄기";

        [SerializeField] private bool startOn = true;

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
            if (targetLight != null)
                targetLight.enabled = on;
            if (fixtureSurface != null)
            {
                Material mat = on ? onMaterial : offMaterial;
                if (mat != null)
                    fixtureSurface.sharedMaterial = mat;
            }
            if (toggle != null)
                toggle.localRotation = Quaternion.Euler(on ? -toggleTiltDegrees : toggleTiltDegrees, 0f, 0f);
        }
    }
}
