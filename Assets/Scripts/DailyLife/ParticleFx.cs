using UnityEngine;
using UnityEngine.Rendering;

namespace CreatureExperiment.DailyLife
{
    /// <summary>
    /// Small runtime particle helpers shared by consumables (cigarette smoke, eating crumbs): one transparent soft-dot
    /// material made once from a base URP Particles material (or the shader by name), and a one-shot burst that cleans
    /// itself up. Nothing to author in the scene.
    /// </summary>
    public static class ParticleFx
    {
        private static Texture2D s_softDot;
        private static Material s_soft;

        /// <summary>Transparent, soft round particle material (shared). <paramref name="baseMaterial"/> keeps the shader in builds.</summary>
        public static Material SoftMaterial(Material baseMaterial = null)
        {
            if (s_soft != null)
                return s_soft;
            Shader shader = baseMaterial != null ? baseMaterial.shader : Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                return null;
            var m = baseMaterial != null ? new Material(baseMaterial) : new Material(shader);
            m.name = "SoftParticle (runtime)";
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BaseMap", SoftDot());
            m.SetColor("_BaseColor", Color.white);
            s_soft = m;
            return m;
        }

        private static Texture2D SoftDot()
        {
            if (s_softDot != null)
                return s_softDot;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDot (runtime)" };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            s_softDot = tex;
            return tex;
        }

        /// <summary>A handful of small bits thrown from <paramref name="position"/> along <paramref name="direction"/>, falling; gone by itself.</summary>
        public static void Burst(Vector3 position, Vector3 direction, Color color, int count, float size)
        {
            Material mat = SoftMaterial();
            if (mat == null || count <= 0)
                return;
            var go = new GameObject("CrumbBurst");
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.1f;
            main.startLifetime = 0.5f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = color;
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; // a paused (culled) burst would never finish and clean itself up
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            Vector3 dir = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.forward;
            for (int i = 0; i < count; i++)
            {
                p.position = position + Random.insideUnitSphere * 0.015f;
                p.velocity = dir * Random.Range(0.3f, 0.7f) + Random.insideUnitSphere * 0.25f + Vector3.up * 0.2f;
                ps.Emit(p, 1);
            }
        }
    }
}
