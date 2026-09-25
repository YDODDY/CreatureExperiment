using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Two fixed global-lighting presets, Day and Night. The daily routine picks one through
    /// <see cref="SetState"/> (<see cref="DailyLifeDirector"/>: day start = Day, clock-out = Night); the
    /// <see cref="state"/> field can also be flipped in the Inspector while playing for a quick comparison
    /// (applied on the next frame). It sets the sun's intensity / color, the flat ambient color, the skybox
    /// material and the environment reflection intensity, and switches every <see cref="StreetLamp"/>
    /// (off by day, on at night). No time of day, no sun movement; room and sensor lights are never touched.
    /// Runtime only: edit mode keeps whatever the scene has saved (the Day values).
    /// </summary>
    public class LightingEnvironment : MonoBehaviour
    {
        public enum LightingState { Day, Night }

        [Serializable]
        public class Preset
        {
            public float sunIntensity = 1f;
            public Color sunColor = Color.white;
            [Tooltip("Flat ambient color (RenderSettings.ambientLight).")]
            public Color ambientColor = new Color(0.3f, 0.3f, 0.33f);
            [Tooltip("Empty = keep the scene's own skybox.")]
            public Material skybox;
            public float reflectionIntensity = 1f;
        }

        [SerializeField] private LightingState state = LightingState.Day;

        [Tooltip("The scene's Directional Light.")]
        [SerializeField] private Light sun;

        [SerializeField] private Preset day = new Preset();
        [SerializeField] private Preset night = new Preset
        {
            sunIntensity = 0.1f,
            sunColor = new Color(0.6f, 0.68f, 0.85f),
            ambientColor = new Color(0.04f, 0.045f, 0.06f),
            reflectionIntensity = 0.15f,
        };

        private Material _sceneSkybox;
        private StreetLamp[] _streetLamps;
        private LightingState _applied;
        private bool _hasApplied;

        public LightingState State => state;

        private void Awake()
        {
            _sceneSkybox = RenderSettings.skybox;
            if (sun == null)
                sun = RenderSettings.sun;
        }

        private void Start() => Apply();

        private void Update()
        {
            if (!_hasApplied || state != _applied)
                Apply();
        }

        public void SetState(LightingState next)
        {
            state = next;
            Apply();
        }

        private void Apply()
        {
            Preset p = state == LightingState.Night ? night : day;

            if (sun != null)
            {
                sun.intensity = p.sunIntensity;
                sun.color = p.sunColor;
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = p.ambientColor;
            RenderSettings.skybox = p.skybox != null ? p.skybox : _sceneSkybox;
            RenderSettings.reflectionIntensity = p.reflectionIntensity;
            DynamicGI.UpdateEnvironment();

            if (_streetLamps == null)
                _streetLamps = FindObjectsByType<StreetLamp>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool lampsOn = state == LightingState.Night;
            foreach (StreetLamp lamp in _streetLamps)
                if (lamp != null)
                    lamp.SetOn(lampsOn);

            _applied = state;
            _hasApplied = true;
        }
    }
}
