using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// One street lamp head. <see cref="LightingEnvironment"/> switches every StreetLamp with the Day /
    /// Night state (off by day, on at night): the Light component plus the lens material (its own
    /// material while on, <see cref="offMaterial"/> while off). References are found on the children if
    /// left empty (the Light, and a child named "LampLens").
    /// </summary>
    public class StreetLamp : MonoBehaviour
    {
        [SerializeField] private Light lampLight;
        [SerializeField] private Renderer lens;
        [SerializeField] private Material offMaterial;

        private Material _onMaterial;

        public bool IsOn => lampLight != null && lampLight.enabled;

        private void Awake()
        {
            if (lampLight == null)
                lampLight = GetComponentInChildren<Light>(true);
            if (lens == null)
            {
                Transform t = transform.Find("LampLens");
                if (t != null)
                    lens = t.GetComponent<Renderer>();
            }
            if (lens != null)
                _onMaterial = lens.sharedMaterial;
        }

        public void SetOn(bool on)
        {
            if (lampLight != null)
                lampLight.enabled = on;
            if (lens != null && _onMaterial != null && offMaterial != null)
                lens.sharedMaterial = on ? _onMaterial : offMaterial;
        }
    }
}
