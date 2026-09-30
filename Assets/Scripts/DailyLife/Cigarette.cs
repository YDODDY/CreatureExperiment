using System;
using UnityEngine;
using UnityEngine.Rendering;
using CreatureExperiment.Interaction;
using CreatureExperiment.Player;

namespace CreatureExperiment.DailyLife
{
    public enum CigaretteState
    {
        Unlit,
        Lit,
        Spent
    }

    /// <summary>
    /// One cigarette: Unlit → Lit → Spent. An ordinary Interactable otherwise (slot, E / Place, F / Right Click, trash).
    ///
    /// Left Click is a press / hold / release action (<see cref="IHeldPrimaryAction"/> with <see cref="PrimaryActive"/> -
    /// the same path the tape roll uses; <c>MealEater</c> feeds it): the first press of an unlit cigarette lights it
    /// (<see cref="Ignite"/> - no lighter yet; a lighter would call the same method), and every hold is a drag. Holding adds
    /// to <see cref="SmokedSeconds"/>; releasing breathes out a small puff in front of the camera. <see cref="secondsToFinish"/>
    /// of drags in total (any number of holds) finish it. Lit, it also burns down by itself: <see cref="maxBurnSeconds"/>
    /// after lighting it is spent however much was smoked. Spent = a butt: no ember, no smoke, no more drags.
    ///
    /// Look: the paper (<see cref="paperPivot"/>, scaled along its local Z from the filter end) gets shorter with
    /// max(smoked / secondsToFinish, burnt / maxBurnSeconds) down to <see cref="buttFraction"/>; the ember sits at the paper's
    /// end and glows a little bigger during a drag. While lit a thin incense-like thread of smoke rises from the ember:
    /// slow, nearly straight up, swaying a little (weak noise), widening and fading out as it climbs. Both particle
    /// systems are built at runtime from <see cref="smokeMaterial"/> (<see cref="ParticleFx.SoftMaterial"/>), so nothing
    /// needs authoring in the scene.
    ///
    /// A drag is also shown: <see cref="HeldItemUseMotion"/> raises the cigarette to the lips while Left Click is held
    /// and lowers it on release (the breath out comes from the mouth point); spent mid-drag, it goes back down.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public class Cigarette : MonoBehaviour, IHeldPrimaryAction
    {
        /// <summary>Raised when any cigarette is lit. Nothing listens yet.</summary>
        public static event Action<Cigarette> Lit;

        [Header("State")]
        [SerializeField] private CigaretteState state = CigaretteState.Unlit;

        [Header("Timing")]
        [Tooltip("Total drag time (Left Click held, summed over any number of drags) that finishes it.")]
        [SerializeField] private float secondsToFinish = 15f;
        [Tooltip("Once lit it is spent after this long, smoked or not.")]
        [SerializeField] private float maxBurnSeconds = 60f;

        [Header("Look")]
        [Tooltip("Pivot at the filter end of the paper; its local Z scale is the paper length.")]
        [SerializeField] private Transform paperPivot;
        [Tooltip("Paper length (local Z of the pivot's parent, m) at full size - where the ember sits unsmoked.")]
        [SerializeField] private float paperLength = 0.095f;
        [Tooltip("What is left of the paper on a butt (fraction of the full length).")]
        [SerializeField] private float buttFraction = 0.2f;
        [Tooltip("Glowing tip, shown only while lit. Moved to the end of the paper.")]
        [SerializeField] private Transform ember;
        [SerializeField] private float emberDragScale = 1.35f;

        [Header("Smoke")]
        [Tooltip("Base particle material (URP Particles/Unlit); made transparent with a soft sprite at runtime.")]
        [SerializeField] private Material smokeMaterial;
        [Tooltip("Idle thread of smoke: particles per second (many small, long-lived ones make a continuous thread).")]
        [SerializeField] private float streamRate = 22f;
        [SerializeField] private float streamLifetime = 3.5f;
        [Tooltip("Rise speed range (m/s, world up).")]
        [SerializeField] private Vector2 streamRise = new Vector2(0.08f, 0.11f);
        [Tooltip("Sideways sway (noise strength). Keep small - it should drift, not wriggle.")]
        [SerializeField] private float streamSway = 0.03f;
        [SerializeField] private float streamSwayFrequency = 0.4f;
        [Tooltip("Idle thread particle size range (m) at the ember; it widens as it rises.")]
        [SerializeField] private Vector2 streamSize = new Vector2(0.02f, 0.028f);
        [Tooltip("Idle thread colour - a slightly blue mid grey reads against both dark rooms and a bright sky.")]
        [SerializeField] private Color streamColor = new Color(0.72f, 0.75f, 0.8f, 0.9f);
        [Tooltip("Where the thread starts, in the Ember's local space (its outer burning face).")]
        [SerializeField] private Vector3 smokeOriginLocal = new Vector3(0f, 1f, 0f);
        [SerializeField] private int exhaleParticles = 14;

        [Header("Identity")]
        [SerializeField] private string spentName = "담배꽁초";
        [SerializeField] private string spentItemId = "CigaretteButt";

        private Interactable _item;
        private float _smoked;
        private float _burnt;
        private bool _dragging;
        private float _dragStartSmoked;
        private Vector3 _emberBaseScale = Vector3.one;
        private Vector3 _paperFullScale = Vector3.one;
        private ParticleSystem _stream;
        private Renderer _emberRenderer;
        private ParticleSystem _exhale;
        private HeldItemUseMotion _motion;

        public CigaretteState State => state;
        public float SmokedSeconds => _smoked;
        public float BurntSeconds => _burnt;
        public bool PrimaryActive => _dragging;

        private void Awake()
        {
            _item = GetComponent<Interactable>();
            if (ember != null)
            {
                _emberBaseScale = ember.localScale;
                _emberRenderer = ember.GetComponent<Renderer>();
                ember.gameObject.SetActive(true); // stays active: the smoke emitter lives under it; only its renderer shows lit / unlit
            }
            if (paperPivot != null) _paperFullScale = paperPivot.localScale;
            BuildSmoke();
            ApplyLook();
        }

        // --- Left Click: first press lights it, every hold is a drag
        public bool PrimaryPress(Ray aim)
        {
            if (state == CigaretteState.Spent)
                return false; // a butt: nothing to do
            if (state == CigaretteState.Unlit)
                Ignite();
            _dragging = true;
            _dragStartSmoked = _smoked;
            _motion = HeldItemUseMotion.For(_item);
            if (_motion != null)
                _motion.BeginHold(_item, HeldUseStyle.Smoke);
            ApplyLook();
            return true;
        }

        public void PrimaryHold(Ray aim)
        {
            if (!_dragging || state != CigaretteState.Lit)
                return;
            _smoked += Time.deltaTime;
            if (_smoked >= secondsToFinish)
                BecomeSpent();
        }

        public void PrimaryRelease(Ray aim) => EndDrag(exhale: true);

        public void PrimaryCancel() => EndDrag(exhale: false);

        /// <summary>Light it (Unlit only). The entry point for a future lighter / match.</summary>
        public void Ignite()
        {
            if (state != CigaretteState.Unlit)
                return;
            state = CigaretteState.Lit;
            _burnt = 0f;
            ApplyLook();
            Lit?.Invoke(this);
        }

        private void EndDrag(bool exhale)
        {
            bool dragged = _dragging && _smoked > _dragStartSmoked + 0.05f;
            _dragging = false;
            if (_motion != null)
                _motion.EndHold(_item);
            if (exhale && dragged)
                Exhale();
            ApplyLook();
        }

        private void Update()
        {
            if (state != CigaretteState.Lit)
            {
                if (_emberRenderer != null && _emberRenderer.enabled)
                    _emberRenderer.enabled = false; // a slot restore can switch it back on - a butt has no glow
                return;
            }
            _burnt += Time.deltaTime;
            if (_burnt >= maxBurnSeconds)
            {
                BecomeSpent();
                return;
            }
            UpdateLength();
        }

        private void BecomeSpent()
        {
            if (state == CigaretteState.Spent)
                return;
            bool wasDragging = _dragging;
            state = CigaretteState.Spent;
            _dragging = false;
            if (_motion != null)
                _motion.EndHold(_item);
            if (wasDragging)
                Exhale();
            _item.SetDisplayName(spentName);
            _item.SetItemId(spentItemId);
            ApplyLook();
        }

        private void ApplyLook()
        {
            bool lit = state == CigaretteState.Lit;
            if (ember != null)
            {
                if (_emberRenderer != null)
                    _emberRenderer.enabled = lit;
                ember.localScale = _emberBaseScale * (lit && _dragging ? emberDragScale : 1f);
            }
            if (_stream != null)
            {
                var emission = _stream.emission;
                emission.rateOverTime = lit ? streamRate : 0f;
                if (lit && !_stream.isPlaying) _stream.Play();
            }
            UpdateLength();
        }

        private void UpdateLength()
        {
            float used;
            if (state == CigaretteState.Spent)
                used = 1f;
            else if (state == CigaretteState.Unlit)
                used = 0f;
            else
                used = Mathf.Clamp01(Mathf.Max(_smoked / Mathf.Max(0.01f, secondsToFinish), _burnt / Mathf.Max(0.01f, maxBurnSeconds)));
            float k = Mathf.Lerp(1f, buttFraction, used);

            if (paperPivot != null)
            {
                Vector3 s = _paperFullScale;
                s.z *= k;
                paperPivot.localScale = s;
            }
            if (ember != null && paperPivot != null)
                ember.localPosition = paperPivot.localPosition + new Vector3(0f, 0f, paperLength * k);
        }

        // --- Smoke

        private void BuildSmoke()
        {
            Material mat = ParticleFx.SoftMaterial(smokeMaterial);
            if (mat == null)
                return;

            // Idle: incense-like thread - tiny, slow, long-lived, rising almost straight up, drifting a little, widening, fading.
            _stream = MakeSystem("SmokeStream", mat, lifetime: streamLifetime, maxParticles: 120);
            if (ember != null)
            {
                // The emitter rides the ember (its burning face), so it follows the shortening paper, the hand-to-mouth
                // motion and a cigarette lying in the world. Particles already out stay in world space.
                _stream.transform.SetParent(ember, false);
                _stream.transform.localPosition = smokeOriginLocal;
                _stream.transform.localRotation = Quaternion.identity;
            }
            var main = _stream.main;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(streamSize.x, streamSize.y);
            main.startColor = streamColor;
            main.scalingMode = ParticleSystemScalingMode.Local; // own scale only - the ember's squashed scale never touches the size
            var vel = _stream.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(streamRise.x, streamRise.y);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var noise = _stream.noise;
            noise.enabled = true;
            noise.separateAxes = false;
            noise.strength = streamSway;
            noise.frequency = streamSwayFrequency;
            noise.scrollSpeed = 0.25f;
            noise.damping = false;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            var size = _stream.sizeOverLifetime;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 3.5f));
            var color = _stream.colorOverLifetime;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.06f), new GradientAlphaKey(0.42f, 0.45f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var rotation = _stream.rotationOverLifetime;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var emission = _stream.emission;
            emission.rateOverTime = 0f;

            // Breath out: the same small cloud as before, now from the mouth point.
            _exhale = MakeSystem("SmokeExhale", mat, lifetime: 1.6f, maxParticles: 60);
            var exMain = _exhale.main;
            exMain.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            exMain.startColor = new Color(0.85f, 0.85f, 0.87f, 0.4f);
            var exEmission = _exhale.emission;
            exEmission.rateOverTime = 0f;
        }

        private ParticleSystem MakeSystem(string systemName, Material mat, float lifetime, int maxParticles)
        {
            var go = new GameObject(systemName);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = lifetime;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.gravityModifier = 0f;
            // Never culled: with Automatic culling a looping, non-procedural system (noise) is paused while Unity thinks
            // its renderer is off screen - and with no particles yet its bounds are empty / stale, so it could be judged
            // invisible, pause, never emit, and stay invisible for good (the idle thread "not showing at all").
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // A breath out: a little cloud pushed forward from just under the camera (the player's mouth).
        private void Exhale()
        {
            if (_exhale == null)
                return;
            Camera cam = Camera.main;
            Vector3 origin = _motion != null ? _motion.MouthPosition
                : cam != null ? cam.transform.position + cam.transform.forward * 0.22f - cam.transform.up * 0.07f : transform.position;
            Vector3 forward = cam != null ? cam.transform.forward : transform.forward;
            if (!_exhale.isPlaying) _exhale.Play();
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < exhaleParticles; i++)
            {
                p.position = origin + UnityEngine.Random.insideUnitSphere * 0.02f;
                p.velocity = forward * UnityEngine.Random.Range(0.35f, 0.7f) + UnityEngine.Random.insideUnitSphere * 0.08f + Vector3.up * 0.05f;
                _exhale.Emit(p, 1);
            }
        }
    }
}
