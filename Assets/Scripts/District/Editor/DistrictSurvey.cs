using System.Text;
using UnityEngine;

namespace CreatureExperiment.DistrictEditor
{
    /// <summary>Read-only scene survey helpers for the District rebuild (called from the editor / MCP).</summary>
    public static class DistrictSurvey
    {
        public static string Children(string path, int depth = 1)
        {
            var go = GameObject.Find(path);
            if (go == null) return "missing " + path;
            var sb = new StringBuilder();
            Walk(go.transform, path, depth, sb);
            return sb.ToString();
        }

        private static void Walk(Transform t, string path, int depth, StringBuilder sb)
        {
            foreach (Transform c in t)
            {
                sb.AppendLine($"{path}/{c.name} act={c.gameObject.activeSelf} pos={c.position:F1} rotY={c.eulerAngles.y:F0} n={c.childCount} {BoundsText(c)} {Comps(c)}");
                if (depth > 1) Walk(c, path + "/" + c.name, depth - 1, sb);
            }
        }

        public static string BoundsText(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return "";
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return $"b={b.min:F1}..{b.max:F1}";
        }

        private static string Comps(Transform t)
        {
            var sb = new StringBuilder();
            foreach (var m in t.GetComponents<MonoBehaviour>())
                if (m != null) sb.Append(m.GetType().Name).Append(',');
            return sb.Length > 0 ? "[" + sb + "]" : "";
        }

        /// <summary>Every object whose name contains <paramref name="part"/> (case-insensitive).</summary>
        public static string Find(string part, bool includeInactive = true)
        {
            var sb = new StringBuilder();
            part = part.ToLowerInvariant();
            foreach (var t in Object.FindObjectsByType<Transform>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (t.name.ToLowerInvariant().Contains(part))
                    sb.AppendLine($"{PathOf(t)} act={t.gameObject.activeInHierarchy} pos={t.position:F1} rotY={t.eulerAngles.y:F0} {BoundsText(t)}");
            return sb.ToString();
        }

        public static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        /// <summary>Scene components of the given type name with their paths.</summary>
        public static string OfType(string typeName)
        {
            var sb = new StringBuilder();
            foreach (var m in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (m != null && m.GetType().Name == typeName)
                    sb.AppendLine($"{PathOf(m.transform)} act={m.gameObject.activeInHierarchy} pos={m.transform.position:F1}");
            return sb.ToString();
        }
    }
}
