using System.Collections.Generic;
using UnityEngine;

namespace CreatureExperiment.DailyLife
{
    public enum SfxKind
    {
        /// <summary>A plate / bowl / cup shattering: a hard crack, then a scatter of bright clinks.</summary>
        CeramicBreak,
        /// <summary>A glass bottle shattering: brighter, longer tinkle.</summary>
        GlassBreak,
        /// <summary>A drink bursting / splashing out.</summary>
        Splash,
        /// <summary>A can punctured: a pop and a hiss.</summary>
        Puncture,
        /// <summary>A toaster popping up: a springy click and a little thunk.</summary>
        Pop,
        /// <summary>Hot water running into a cup for a few seconds.</summary>
        Pour,
        /// <summary>A toilet flush: a rushing swirl that drains away (~1.6 s).</summary>
        Flush,
        /// <summary>A thin metal door (locker): latch click + short metallic ring.</summary>
        MetalDoor
    }

    /// <summary>
    /// Placeholder one-shot sounds built from code (the project has no audio assets yet): each kind is synthesised once
    /// (noise bursts + decaying sine "clinks", three variations) into an <see cref="AudioClip"/> and played from a
    /// throwaway 3D <see cref="AudioSource"/> at the spot. Swap for real clips later without touching the callers.
    /// Loudness is deliberate: a break should be heard across a room (a future creature lure, see <c>Breakable.noise</c>).
    /// </summary>
    public static class ProceduralSfx
    {
        private const int Rate = 44100;
        /// <summary>Master scale on every procedural sound (all callers' volumes are multiplied by it) - one knob for the lot.</summary>
        public const float VolumeScale = 0.5f;
        private const int Variations = 3;
        private static readonly Dictionary<SfxKind, AudioClip[]> s_clips = new Dictionary<SfxKind, AudioClip[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_clips.Clear();

        public static void Play(SfxKind kind, Vector3 position, float volume = 1f)
        {
            AudioClip clip = Get(kind, Random.Range(0, Variations));
            if (clip == null)
                return;
            var go = new GameObject("Sfx_" + kind);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * VolumeScale);
            src.pitch = Random.Range(0.93f, 1.07f);
            src.spatialBlend = 0.8f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 4f;
            src.maxDistance = 45f;
            src.dopplerLevel = 0f;
            src.playOnAwake = false;
            src.Play();
            Object.Destroy(go, clip.length / src.pitch + 0.1f);
        }

        private static AudioClip Get(SfxKind kind, int variation)
        {
            if (!s_clips.TryGetValue(kind, out AudioClip[] clips))
            {
                clips = new AudioClip[Variations];
                for (int i = 0; i < Variations; i++)
                    clips[i] = Build(kind, i);
                s_clips[kind] = clips;
            }
            return clips[variation];
        }

        private static AudioClip Build(SfxKind kind, int variation)
        {
            var rng = new System.Random(1000 * (int)kind + variation + 17);
            float[] d;
            switch (kind)
            {
                case SfxKind.CeramicBreak: d = Shatter(rng, 0.6f, 1600f, 5200f, 16, 0.32f, 0.35f, 0.012f, 170f); break;
                case SfxKind.GlassBreak: d = Shatter(rng, 0.85f, 2800f, 8800f, 26, 0.55f, 0.8f, 0.008f, 0f); break;
                case SfxKind.Splash: d = Splash(rng); break;
                case SfxKind.Pop: d = Pop(rng); break;
                case SfxKind.Pour: d = Pour(rng); break;
                case SfxKind.Flush: d = Flush(rng); break;
                case SfxKind.MetalDoor: d = MetalDoor(rng); break;
                default: d = Puncture(rng); break;
            }
            Normalize(d, kind == SfxKind.Splash ? 0.8f : 0.95f);
            var clip = AudioClip.Create($"Sfx_{kind}_{variation}", d.Length, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        // Impact crack (lowpassed noise) + optional low thud + many short sine clinks scattered in time (the pieces).
        private static float[] Shatter(System.Random rng, float seconds, float fMin, float fMax, int clinks, float scatter,
            float crackLowpass, float crackDecay, float thudHz)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * crackLowpass;
                float s = lp * 1.1f * Mathf.Exp(-t / crackDecay) + noise * 0.25f * Mathf.Exp(-t / (crackDecay * 3f));
                if (thudHz > 0f)
                    s += 0.55f * Mathf.Sin(2f * Mathf.PI * thudHz * t) * Mathf.Exp(-t / 0.035f);
                d[i] = s;
            }
            for (int c = 0; c < clinks; c++)
            {
                float start = c < 4 ? (float)rng.NextDouble() * 0.01f : (float)(rng.NextDouble() * rng.NextDouble()) * scatter;
                float f = Mathf.Lerp(fMin, fMax, (float)rng.NextDouble());
                float amp = Mathf.Lerp(0.6f, 0.15f, start / Mathf.Max(scatter, 0.01f)) * (0.6f + 0.4f * (float)rng.NextDouble());
                float decay = Mathf.Lerp(0.018f, 0.07f, (float)rng.NextDouble());
                int i0 = (int)(start * Rate);
                int len = Mathf.Min(n - i0, (int)(decay * 6f * Rate));
                for (int k = 0; k < len; k++)
                {
                    float t = k / (float)Rate;
                    float env = Mathf.Exp(-t / decay);
                    d[i0 + k] += amp * env * (Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 2.76f * t));
                }
            }
            return d;
        }

        // Dull noise wash with a fast attack, plus a little "glug" modulation.
        private static float[] Splash(System.Random rng)
        {
            int n = (int)(0.5f * Rate);
            var d = new float[n];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.18f;
                lp2 += (lp - lp2) * 0.3f;
                float env = Mathf.Min(1f, t / 0.006f) * Mathf.Exp(-t / 0.13f);
                float glug = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * 23f * t);
                d[i] = lp2 * env * glug + 0.3f * Mathf.Sin(2f * Mathf.PI * 140f * t) * Mathf.Exp(-t / 0.03f);
            }
            return d;
        }

        // A steady soft stream that slowly rises in pitch as the cup fills, fading at the end (~2.8 s).
        private static float[] Pour(System.Random rng)
        {
            int n = (int)(2.8f * Rate);
            var d = new float[n];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float k = t / 2.8f;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float cut = Mathf.Lerp(0.12f, 0.3f, k);
                lp += (noise - lp) * cut;
                lp2 += (lp - lp2) * 0.5f;
                float env = Mathf.Min(1f, t / 0.08f) * Mathf.Min(1f, (2.8f - t) / 0.3f);
                float bubble = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(9f, 15f, k) * t);
                d[i] = lp2 * env * bubble;
            }
            return d;
        }

        // Lowpassed noise rushing in fast, swirling (slow wobble), then draining away with a falling cutoff.
        private static float[] Flush(System.Random rng)
        {
            const float seconds = 1.6f;
            int n = (int)(seconds * Rate);
            var d = new float[n];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float k = t / seconds;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * Mathf.Lerp(0.35f, 0.08f, k);
                lp2 += (lp - lp2) * 0.45f;
                float env = Mathf.Min(1f, t / 0.05f) * Mathf.Pow(1f - k, 1.3f);
                float swirl = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(5f, 2f, k) * t);
                d[i] = lp2 * env * swirl;
            }
            return d;
        }

        // Latch click, then a short metallic ring (a few inharmonic partials).
        private static float[] MetalDoor(System.Random rng)
        {
            int n = (int)(0.4f * Rate);
            var d = new float[n];
            float f = 480f + (float)rng.NextDouble() * 80f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float click = noise * Mathf.Exp(-t / 0.004f);
                float ring = (Mathf.Sin(2f * Mathf.PI * f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * f * 2.41f * t)
                    + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 3.87f * t)) * Mathf.Exp(-t / 0.09f) * 0.45f;
                d[i] = click + ring;
            }
            return d;
        }

        // Spring click (bright, very short) then a hollow thunk.
        private static float[] Pop(System.Random rng)
        {
            int n = (int)(0.25f * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float click = noise * Mathf.Exp(-t / 0.003f) + 0.6f * Mathf.Sin(2f * Mathf.PI * 2400f * t) * Mathf.Exp(-t / 0.012f);
                float thunk = 0.8f * Mathf.Sin(2f * Mathf.PI * 210f * t) * Mathf.Exp(-t / 0.04f);
                d[i] = click + thunk;
            }
            return d;
        }

        // A short pop, then a hiss (highpassed noise) that fades.
        private static float[] Puncture(System.Random rng)
        {
            int n = (int)(0.75f * Rate);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.2f;
                float hiss = (noise - lp) * Mathf.Min(1f, t / 0.012f) * Mathf.Exp(-t / 0.28f) * 0.55f;
                float pop = lp * 1.4f * Mathf.Exp(-t / 0.005f);
                d[i] = pop + hiss;
            }
            return d;
        }

        private static void Normalize(float[] d, float peak)
        {
            float max = 0f;
            foreach (float v in d)
                max = Mathf.Max(max, Mathf.Abs(v));
            if (max <= 0f)
                return;
            float k = peak / max;
            for (int i = 0; i < d.Length; i++)
                d[i] *= k;
        }
    }
}
