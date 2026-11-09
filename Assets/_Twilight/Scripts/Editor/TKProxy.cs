using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Twilight.EditorTools
{
    /// <summary>远景替身网格：松树 = 树干 + 三层圆锥；圆树 / 花丛 = 树干 + 一团压扁的球。颜色写在顶点色里，共用 Twilight/VertexLit。
    /// 尺寸按预制体包围盒（底面中心在原点）。只给树和花丛做（远处成片出现、数量多的东西）。</summary>
    public static class TKProxy
    {
        const string Dir = "Assets/_Twilight/Art/Generated/Proxy";

        public static Material Material()
        {
            string path = $"{TKArt.MatDir}/Proxy_VertexLit.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Twilight/VertexLit")); m.enableInstancing = true; AssetDatabase.CreateAsset(m, path); }
            return m;
        }

        public static Mesh For(string name, Vector3 size)
        {
            Mesh m = name switch
            {
                "pine_tree" => Pine(size, new Color(0.22f, 0.3f, 0.19f), new Color(0.36f, 0.24f, 0.17f)),
                "round_tree" => Blob(size, new Color(0.58f, 0.52f, 0.27f), new Color(0.42f, 0.28f, 0.2f), 0.3f),
                "bush_flower" => Blob(size, new Color(0.45f, 0.53f, 0.32f), Color.clear, 0f),
                "great_oak" => Blob(size, new Color(0.25f, 0.36f, 0.2f), new Color(0.38f, 0.24f, 0.17f), 0.35f),
                "willow_tree" => Blob(size, new Color(0.45f, 0.52f, 0.3f), new Color(0.4f, 0.27f, 0.18f), 0.25f),
                _ => null,
            };
            if (!m) return null;
            m.name = name + "_proxy";
            TKArt.EnsureFolder(Dir);
            var path = $"{Dir}/{name}_proxy.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void Cone(List<Vector3> v, List<Color> c, List<int> t, Vector3 baseC, float r, float h, int sides, Color col)
        {
            int b = v.Count;
            v.Add(baseC + Vector3.up * h); c.Add(col * 1.1f);
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                v.Add(baseC + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)); c.Add(col * 0.85f);
            }
            for (int i = 0; i < sides; i++) t.AddRange(new[] { b, b + 2 + i, b + 1 + i });
            int ctr = v.Count; v.Add(baseC); c.Add(col * 0.6f);
            for (int i = 0; i < sides; i++) t.AddRange(new[] { ctr, b + 1 + i, b + 2 + i });
        }

        static void Trunk(List<Vector3> v, List<Color> c, List<int> t, float r, float h, Color col)
        {
            const int sides = 5;
            int b = v.Count;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var o = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                v.Add(o); c.Add(col); v.Add(o * 0.7f + Vector3.up * h); c.Add(col);
            }
            for (int i = 0; i < sides; i++) { int a = b + i * 2; t.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 }); }
        }

        static Mesh Build(List<Vector3> v, List<Color> c, List<int> t)
        {
            var m = new Mesh();
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh Pine(Vector3 size, Color leaf, Color bark)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            float H = size.y, R = Mathf.Max(size.x, size.z) * 0.5f;
            Trunk(v, c, t, R * 0.09f, H * 0.3f, bark);
            Cone(v, c, t, Vector3.up * H * 0.18f, R * 0.95f, H * 0.42f, 8, leaf * 0.9f);
            Cone(v, c, t, Vector3.up * H * 0.42f, R * 0.72f, H * 0.36f, 8, leaf);
            Cone(v, c, t, Vector3.up * H * 0.64f, R * 0.45f, H * 0.36f, 7, leaf * 1.1f);
            return Build(v, c, t);
        }

        static Mesh Blob(Vector3 size, Color leaf, Color bark, float trunkFrac)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            float H = size.y, R = Mathf.Max(size.x, size.z) * 0.5f;
            if (trunkFrac > 0) Trunk(v, c, t, R * 0.1f, H * (trunkFrac + 0.1f), bark);
            // 压扁的低模球（经纬 8×5）
            float cy = trunkFrac > 0 ? H * (trunkFrac + (1 - trunkFrac) * 0.5f) : H * 0.5f, ry = trunkFrac > 0 ? H * (1 - trunkFrac) * 0.5f : H * 0.5f;
            int b = v.Count; const int lon = 8, lat = 5;
            for (int j = 0; j <= lat; j++)
            {
                float phi = Mathf.PI * j / lat;
                for (int i = 0; i <= lon; i++)
                {
                    float th = Mathf.PI * 2f * i / lon;
                    v.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(th) * R, cy + Mathf.Cos(phi) * ry, Mathf.Sin(phi) * Mathf.Sin(th) * R));
                    c.Add(leaf * Mathf.Lerp(1.1f, 0.75f, (float)j / lat));
                }
            }
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = b + j * (lon + 1) + i, d = a + lon + 1;
                    t.AddRange(new[] { a, a + 1, d, d, a + 1, d + 1 });
                }
            return Build(v, c, t);
        }
    }
}
