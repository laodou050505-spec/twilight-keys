using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Twilight.EditorTools
{
    /// <summary>场景巡检（菜单 Twilight/巡检）。
    /// 浮空：给地形临时加碰撞体，对村里每个模型在它的占地范围内往下打 9 条射线，取脚下地面的最低点；
    /// 模型最低点比地面高出阈值（随模型大小，≥ 4 cm）= 浮空。水面上的东西按水面算（睡莲要贴水）。
    /// fix = true 时直接把浮空的模型往下放到贴地（再往下沉 1.5% 高度，避免边缘露缝）。</summary>
    public static class TKAudit
    {
        static readonly string[] Skip = { "Sky", "Lake", "Mountains", "Fireflies", "Terrain", "Grass", "Sails", "RuinWall", "Reveal", "Proxy", "Treeline", "Dock" };     // 码头本来就架在桩上

        [MenuItem("Twilight/巡检/浮空模型")]
        public static string Floating() => Floating(false);

        public static string Floating(bool fix)
        {
            var village = GameObject.Find("Village");
            if (!village) return "没找到 Village";
            var cols = new List<MeshCollider>();
            foreach (var mf in village.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.StartsWith("Terrain") || (mf.transform.parent && mf.transform.parent.name.StartsWith("Terrain")))
                    cols.Add(mf.gameObject.AddComponent<MeshCollider>());
            Physics.SyncTransforms();
            float waterY = village.transform.TransformPoint(new Vector3(0, TKVillage.WaterY, 0)).y;
            var sb = new StringBuilder();
            int n = 0, bad = 0;
            var done = new HashSet<Transform>();
            foreach (var lod in village.GetComponentsInChildren<LODGroup>(true)) Check(lod.transform, Bounds(lod.gameObject));
            foreach (var r in village.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponentInParent<LODGroup>()) continue;
                Check(r.transform, r.bounds);
            }
            foreach (var c in cols) Object.DestroyImmediate(c);
            sb.Insert(0, $"检查 {n} 个模型，浮空 {bad} 个{(fix ? "（已放下）" : "")}\n");
            return sb.ToString();

            void Check(Transform t, Bounds b)
            {
                if (done.Contains(t) || b.size.y < 0.05f) return;
                for (var p = t; p && p != village.transform; p = p.parent)
                    foreach (var s in Skip) if (p.name.StartsWith(s)) return;
                done.Add(t); n++;
                float ground = float.MaxValue; bool any = false;
                // 树 / 灌木 / 路灯 / 花：根很细，只测正下方一小圈；房子、石头、栅栏等整块落地的测整个占地
                string nm = t.name.ToLowerInvariant();
                bool thin = nm.Contains("tree") || nm.Contains("bush") || nm.Contains("lamp") || nm.Contains("flower") || nm.Contains("reed") || nm.Contains("willow") || nm.Contains("oak") || nm.Contains("pine");
                float spread = thin ? 0.08f : 0.6f;
                var baseC = thin ? new Vector3(t.position.x, b.center.y, t.position.z) : b.center;
                for (int i = -1; i <= 1; i++)
                    for (int j = -1; j <= 1; j++)
                    {
                        var o = new Vector3(baseC.x + i * b.extents.x * spread, b.max.y + 50f, baseC.z + j * b.extents.z * spread);
                        if (Physics.Raycast(o, Vector3.down, out var hit, 500f)) { ground = Mathf.Min(ground, Mathf.Max(hit.point.y, waterY)); any = true; }
                    }
                if (!any) ground = waterY;
                float gap = b.min.y - ground;
                float tol = Mathf.Max(0.04f, b.size.y * 0.02f);
                if (gap > tol)
                {
                    bad++;
                    if (bad <= 80) sb.AppendLine($"  {Path(t)}  离地 {gap:F2} m（高 {b.size.y:F1} m，原点比最低点高 {t.position.y - b.min.y:F2}）");
                    if (fix)
                    {
                        Undo.RecordObject(t, "放下浮空模型");
                        t.position += Vector3.down * (gap + b.size.y * 0.015f);
                    }
                }
            }
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            // 只用最精细一级 LOD 的包围盒
            var lod = go.GetComponent<LODGroup>();
            if (lod && lod.GetLODs().Length > 0 && lod.GetLODs()[0].renderers.Length > 0) rs = lod.GetLODs()[0].renderers;
            var b = rs[0].bounds;
            foreach (var r in rs) if (r) b.Encapsulate(r.bounds);
            return b;
        }

        static string Path(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p && p.name != "Village"; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
