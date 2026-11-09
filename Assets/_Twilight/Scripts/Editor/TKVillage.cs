using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Twilight.EditorTools
{
    /// <summary>窗中村（真实尺度，坐标相对窗洞中心：+Z 出窗、y = 0 是窗洞中心高度）。
    /// 构图（从座位看窗洞只有左右各约 13°、上下 -8° ~ +11°，像长焦镜头）：
    /// 左边山坡上的村子沿岸线由近及远，岸线斜着退向右后方，半岛尖上是钟楼（窗中线偏右）；
    /// 右边是开阔湖面直到远岸与远山；窗下前景是香蒲。湖面低于窗洞约 3 m。</summary>
    public static class TKVillage
    {
        public const float WaterY = -3.0f;
        /// <summary>村庄布局（岸线 / 山坡 / 摆放位置）整体推远的倍数；模型保持真实尺寸，所以在窗里显得更远、更小。</summary>
        public const float S = 1.8f;
        /// <summary>整个村庄布局绕窗户转的角度（负 = 向左）：让窗洞里左侧是村子、中间是钟楼半岛、右侧是开阔湖面。</summary>
        public const float LayoutYaw = -2f;
        /// <summary>布局横向压缩：村子沿岸线往纵深叠，整个落在座位看窗的左半边（窗洞只有左右各约 13°）。</summary>
        public const float Kx = 0.55f;

        /// <summary>世界（村庄局部）坐标 → 布局坐标（未乘 S）。</summary>
        static Vector2 ToLayout(float x, float z)
        {
            float a = -LayoutYaw * Mathf.Deg2Rad, c = Mathf.Cos(a), sn = Mathf.Sin(a);
            var r = new Vector2(x * c + z * sn, -x * sn + z * c) / S;
            r.x /= Kx;
            return r;
        }

        /// <summary>布局坐标（未乘 S）→ 世界（村庄局部）坐标。</summary>
        static Vector2 FromLayout(float lx, float lz)
        {
            float a = LayoutYaw * Mathf.Deg2Rad, c = Mathf.Cos(a), sn = Mathf.Sin(a);
            lx *= S * Kx; lz *= S;
            return new Vector2(lx * c + lz * sn, -lx * sn + lz * c);
        }
        const string Sky = "Assets/_Twilight/Art/Sky";

        // 左岸岸线：z → x（x 小于它是陆地）
        static readonly Vector2[] Shore =
        {
            new(10, -34), new(14, -26), new(26, -10), new(40, -2), new(55, 2), new(70, 4), new(82, 9), new(90, 13),
            new(98, 6), new(110, 2), new(125, 6), new(145, 12), new(170, 16), new(200, 22), new(260, 30), new(420, 60),
        };
        // 村里的土路（码头 → 上山）
        static readonly Vector2[] Path =
        {
            new(-3, 36), new(-6, 46), new(-7, 56), new(-10, 66), new(-9, 76), new(-13, 88), new(-17, 100), new(-21, 114), new(-24, 128),
        };

        // ---------------------------------------------------------------- 地形

        static float ShoreX(float z, out float cos)
        {
            cos = 1f;
            if (z <= Shore[0].x) return Shore[0].y;
            for (int i = 0; i < Shore.Length - 1; i++)
            {
                var a = Shore[i]; var b = Shore[i + 1];
                if (z <= b.x)
                {
                    float k = (b.y - a.y) / (b.x - a.x);
                    cos = 1f / Mathf.Sqrt(1 + k * k);
                    return a.y + (z - a.x) * k;
                }
            }
            return Shore[^1].y;
        }

        static float Noise(float x, float z, float s) => Mathf.PerlinNoise(x / s + 31.7f, z / s + 17.3f) * 2f - 1f;

        /// <summary>地面高度（村庄局部坐标）。</summary>
        public static float Height(float x, float z)
        {
            // 湖底
            float bed = WaterY - 1.6f;
            // 左岸山坡（布局坐标 = 世界 / S，高度再乘回 S）
            var l = ToLayout(x, z); float lx = l.x, lz = l.y;
            float d = (lx - ShoreX(lz, out float cos)) * cos;        // >0 在水里
            float inland = -d;
            float hill = WaterY + 0.9f + S * (0.6f * Mathf.SmoothStep(0, 1, inland / 6f) + Mathf.Min(0.3f * Mathf.Max(0, inland - 8f), 13f)
                         + Mathf.Max(0, lz - 100f) * 0.05f + Noise(lx, lz, 23f) * 1.3f) + Noise(x, z, 7f) * 0.35f;
            float h = Mathf.Lerp(bed, hill, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(4f / S, -14f / S, d)));   // 岸坡拉宽（原来 8 m 内从湖底升到山坡，切出一道硬脊）
            // 玩家所在的湖边草地（墙“化开”后玩家就坐在这里）：高度对齐真实地面；身后缓坡山丘、左边并入村庄山坡、右手边是绕过来的湖岸
            float home = HomeY + Noise(x, z, 9f) * 0.12f
                         + Mathf.Clamp((-z - 16f) * 0.2f, 0f, 15f) + Mathf.Max(0f, Noise(x, z, 50f)) * 9f * Mathf.InverseLerp(-40f, -90f, z)
                         + Mathf.Clamp((-x - 24f) * 0.16f, 0f, 12f);
            // 岸边缓缓坡进湖里（草地比水面高 1.7 m，坡太短会像一块切开的平板）
            float fz = FrontShore(x);                                                                                 // 前面：弯弯的岸线（原来是一条直线）
            home = Mathf.Lerp(bed, home, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(fz, fz - 15f, z)));
            home = Mathf.Lerp(bed, home, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(7f, -12f, x - HomeShoreX(z))));     // 右边
            float bank = home;
            // 远岸
            float zf = FarShore(x);
            float far = Mathf.Lerp(bed, WaterY + 0.6f + Mathf.Min(Mathf.Max(0, z - zf) * 0.06f, 7f) + Mathf.Max(0, Noise(x, z, 40f)) * 4f,
                Mathf.SmoothStep(0, 1, Mathf.InverseLerp(zf - 6f, zf + 6f, z)));
            // 平滑取最大值：三片地形交界处不出折痕 / 直脊（原来的 Max 会在山坡和草地之间切出一道直直的脊线）
            // 平滑宽度只在岸上起作用：湖里（几片都等于湖底）退回普通最大值，否则平滑会把湖底鼓起来
            float m1 = Mathf.Max(bank, far), k1 = 6f * Mathf.Clamp01((m1 - (WaterY - 0.6f)) / 3f);
            float b1 = SMax(bank, far, k1);
            float m2 = Mathf.Max(h, b1), k2 = 6f * Mathf.Clamp01((m2 - (WaterY - 0.6f)) / 3f);
            return SMax(h, b1, k2);
        }

        /// <summary>平滑最大值（多项式，k = 过渡宽度米数）。</summary>
        static float SMax(float a, float b, float k)
        {
            if (k < 1e-4f) return Mathf.Max(a, b);
            float hh = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
            return Mathf.Lerp(b, a, hh) + k * hh * (1f - hh);
        }

        /// <summary>玩家草地的地面高度（村庄局部）：真实地面在窗洞中心下方 1.25 m。</summary>
        public const float HomeY = -1.27f;

        /// <summary>玩家右手边的湖岸线（z → x）。越往身后越往右退。</summary>
        static float HomeShoreX(float z) => 24f + 6f * Mathf.Sin(z / 17f) + Mathf.Max(0f, -z - 30f) * 0.35f;

        /// <summary>玩家正前方的湖岸线（x → z）：两层起伏，桌子正前方约 17 m。</summary>
        static float FrontShore(float x) => 13f + 4.5f * Mathf.Sin(x / 11f + 0.6f) + 2.2f * Mathf.Sin(x / 4.7f + 2f);

        static float FarShore(float x) => 420f + 22f * Mathf.Sin(x / 60f) + 12f * Mathf.Sin(x / 23f + 1f);

        static float PathDist(float x, float z)
        {
            float best = 1e9f; var p = ToLayout(x, z);
            for (int i = 0; i < Path.Length - 1; i++)
            {
                var a = Path[i]; var b = Path[i + 1]; var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best * S;
        }

        static Mesh TerrainMesh(string name, float x0, float x1, float z0, float z1, float step, Rect? hole = null)
        {
            int nx = Mathf.CeilToInt((x1 - x0) / step) + 1, nz = Mathf.CeilToInt((z1 - z0) / step) + 1;
            var v = new Vector3[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = x0 + i * step, z = z0 + j * step;
                    v[j * nx + i] = new Vector3(x, Height(x, z), z);
                }
            var tris = new List<int>();
            float under = WaterY - 0.35f;
            for (int j = 0; j < nz - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    // 整格都在水下看不见，不要
                    if (v[a].y < under && v[b].y < under && v[c].y < under && v[d].y < under) continue;
                    // 和近景网格重叠的格子不要（两套网格对齐在 8 m 上，挖得严丝合缝，不会共面闪烁）
                    if (hole.HasValue && hole.Value.Contains(new Vector2(v[a].x, v[a].z)) && hole.Value.Contains(new Vector2(v[d].x, v[d].z))) continue;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            var m = new Mesh { name = name, indexFormat = v.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.vertices = v;
            m.SetTriangles(tris, 0);
            // 法线直接由高度函数的中心差分算：分块之后接缝两边一致，不会断光
            var nrm = new Vector3[v.Length];
            for (int k = 0; k < v.Length; k++)
            {
                float e = step * 0.5f, x = v[k].x, z = v[k].z;
                nrm[k] = new Vector3(Height(x - e, z) - Height(x + e, z), 2f * e, Height(x, z - e) - Height(x, z + e)).normalized;
            }
            m.normals = nrm;
            // 顶点色：R 土路 / 岸边湿泥，G 岩石（陡坡），B 环境遮蔽
            var n = m.normals; var col = new Color[v.Length];
            for (int k = 0; k < v.Length; k++)
            {
                float path = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.9f, 2.2f, PathDist(v[k].x, v[k].z)));
                float wet = Mathf.InverseLerp(WaterY + 0.7f, WaterY + 0.1f, v[k].y);
                float rock = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.74f, 0.58f, n[k].y));
                float ao = Mathf.Lerp(1f, 0.72f, wet);
                col[k] = new Color(Mathf.Max(path, wet * 0.8f), rock, ao, 1);
            }
            m.colors = col;
            m.RecalculateBounds();
            if (tris.Count == 0) return null;
            TKArt.EnsureFolder("Assets/_Twilight/Art/Generated/Terrain");
            var path2 = $"Assets/_Twilight/Art/Generated/Terrain/{name}.asset";
            AssetDatabase.DeleteAsset(path2);
            AssetDatabase.CreateAsset(m, path2);
            return m;
        }

        /// <summary>地形分块（视锥剔除）：每块 chunk 米见方，块之间共用边上的顶点高度，法线由高度函数算，接缝无缝。</summary>
        static void TerrainChunks(string name, Transform parent, Material mat, float x0, float x1, float z0, float z1, float step, float chunk, Rect? hole = null)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            int n = 0;
            for (float cz = z0; cz < z1 - 1e-3f; cz += chunk)
                for (float cx = x0; cx < x1 - 1e-3f; cx += chunk)
                {
                    float ex = Mathf.Min(cx + chunk, x1), ez = Mathf.Min(cz + chunk, z1);
                    if (hole.HasValue && hole.Value.xMin <= cx && hole.Value.xMax >= ex && hole.Value.yMin <= cz && hole.Value.yMax >= ez) continue;
                    var m = TerrainMesh($"{name}_{n}", cx, ex, cz, ez, step, hole);
                    if (m) MeshObj($"{name} {n}", root, m, mat);
                    n++;
                }
        }

        static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material mat)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g;
        }

        static Material ShaderMat(string name, string shader)
        {
            string path = $"{TKArt.MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        // ---------------------------------------------------------------- 天空 / 远山 / 湖

        /// <summary>天空图导入设置：SkyAtlas 切成 4×3 的 2D Array（每片 2048²）；远山每片 ≤2048。</summary>
        public static void SetupSkyTextures()
        {
            if (AssetImporter.GetAtPath($"{Sky}/SkyAtlas.png") is TextureImporter ti)
            {
                ti.textureShape = TextureImporterShape.Texture2DArray;
                var s = new TextureImporterSettings(); ti.ReadTextureSettings(s);
                s.flipbookColumns = 4; s.flipbookRows = 3;
                ti.SetTextureSettings(s);
                ti.maxTextureSize = 8192;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.sRGBTexture = true;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                // 头显：天空像素密度（约 22 像素/度）和屏幕相当，不需要 mip；ASTC 6×6 → 12 片约 22 MB
                ti.mipmapEnabled = false;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Android", overridden = true, maxTextureSize = 8192, format = TextureImporterFormat.ASTC_6x6 });
                ti.SaveAndReimport();
            }
            for (int i = 0; i < 4; i++)
                if (AssetImporter.GetAtPath($"{Sky}/Mountains_{i}.png") is TextureImporter mi)
                {
                    mi.maxTextureSize = 2048; mi.wrapMode = TextureWrapMode.Clamp; mi.alphaIsTransparency = true;
                    mi.textureCompression = TextureImporterCompression.CompressedHQ;
                    mi.SaveAndReimport();
                }
        }

        static Mesh Arc(string name, float radius, float fromDeg, float toDeg, int segs, float elBottom, float elTop, int parts)
        {
            // parts 段子网格，每段 UV 0..1（对应一张远山贴图）
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            var m = new Mesh { name = name, subMeshCount = parts };
            float yb = radius * Mathf.Tan(elBottom * Mathf.Deg2Rad), yt = radius * Mathf.Tan(elTop * Mathf.Deg2Rad);
            var tris = new List<int>[parts];
            for (int p = 0; p < parts; p++)
            {
                tris[p] = new List<int>();
                int start = v.Count;
                for (int s = 0; s <= segs; s++)
                {
                    float t = (float)s / segs;
                    float a = Mathf.Lerp(fromDeg, toDeg, (p + t) / parts) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    v.Add(dir * radius + Vector3.up * yb); uv.Add(new Vector2(t, 0));
                    v.Add(dir * radius + Vector3.up * yt); uv.Add(new Vector2(t, 1));
                }
                for (int s = 0; s < segs; s++)
                {
                    int a0 = start + s * 2;
                    tris[p].AddRange(new[] { a0, a0 + 1, a0 + 2, a0 + 2, a0 + 1, a0 + 3 });
                }
            }
            m.SetVertices(v); m.SetUVs(0, uv);
            for (int p = 0; p < parts; p++) m.SetTriangles(tris[p], p);
            m.RecalculateBounds();
            TKArt.EnsureFolder("Assets/_Twilight/Art/Generated");
            var path = $"Assets/_Twilight/Art/Generated/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---------------------------------------------------------------- 摆放

        struct Item
        {
            public string name; public float x, z, yaw, scale; public bool face, world;
            public Item(string n, float x, float z, float scale = 1f, float yaw = 0f, bool face = true) { world = false; name = n; this.x = x; this.z = z; this.scale = scale; this.yaw = yaw; this.face = face; }
        }

        // 预制体的标准尺寸（ImportP2 用）：高度 / 或最长水平边
        /// <summary>个别模型的颜色微调（在黄昏光下偏色的）。</summary>
        static readonly Dictionary<string, Color> Tints = new() { ["willow_tree"] = new Color(0.82f, 1f, 0.78f) };

        /// <summary>需要双面渲染的模型（单层薄片）。</summary>
        public static readonly HashSet<string> TwoSided = new() { "flower_box", "reeds", "sky_lantern", "bush_flower", "wildflower_patch", "lily_pads", "willow_tree" };

        public static readonly Dictionary<string, (float size, int axis, bool emissive)> Sizes = new()
        {
            ["cottage_tile"] = (7.5f, 0, true), ["cottage_slate"] = (7.2f, 0, true), ["cottage_cream"] = (7.6f, 0, true),
            ["house_tall"] = (11f, 0, true), ["windmill_tower"] = (11f, 0, true), ["bell_tower"] = (17f, 0, true),
            ["pine_tree"] = (11f, 0, false), ["round_tree"] = (8f, 0, false), ["bush_flower"] = (1.5f, 0, false),
            ["reeds"] = (1.9f, 0, false), ["shore_rocks"] = (2.4f, 0, false), ["lamp_post"] = (3.2f, 0, true),
            ["rowboat"] = (3.8f, 1, false),
            // 房间里的窗户装饰（不摆在村里）
            ["window_shutter"] = (0.94f, 0, false), ["flower_box"] = (1.36f, 1, false), ["sky_lantern"] = (0.42f, 0, true),
            // 玩家身边（世界铺开后近看）
            ["great_oak"] = (13f, 0, false), ["willow_tree"] = (10f, 0, false), ["garden_bench"] = (1.6f, 1, false),
            ["wildflower_patch"] = (0.55f, 0, false), ["picket_fence"] = (2.0f, 1, false), ["stepping_stones"] = (1.5f, 1, false),
            ["lily_pads"] = (1.8f, 1, false),
        };

        static readonly Item[] Items =
        {
            // 房子：由近及远沿路上山
            new("cottage_tile", -9, 44, 1f, 12), new("cottage_slate", -3.5f, 58, 1f, -8), new("cottage_cream", -14, 60, 1f, 20),
            new("cottage_tile", -6, 74, 0.95f, 160 - 180), new("house_tall", -16, 84, 1f, 10), new("cottage_slate", -24, 72, 1f, 30),
            new("cottage_cream", -11, 96, 0.95f, -15), new("house_tall", -28, 98, 0.95f, 25), new("cottage_tile", -20, 110, 0.9f, 5),
            new("cottage_slate", -34, 118, 0.9f, 20), new("cottage_cream", -40, 88, 1f, 35),
            new("windmill_tower", -27, 132, 1f, 20), new("bell_tower", 6, 86, 1f, 0),
            // 树
            new("pine_tree", 10.5f, 90, 1f), new("pine_tree", 2, 92, 1.15f), new("pine_tree", 8, 80, 0.85f),
            new("round_tree", -1, 66, 0.9f), new("round_tree", -14, 50, 0.85f), new("round_tree", -4, 84, 0.8f),
            new("pine_tree", -6, 106, 1.1f), new("pine_tree", -2, 118, 1.25f), new("round_tree", 3, 104, 1f),
            new("pine_tree", -32, 60, 1.2f), new("round_tree", -22, 92, 0.95f), new("pine_tree", -38, 104, 1.3f),
            new("pine_tree", -44, 130, 1.3f), new("round_tree", -15, 124, 1f), new("pine_tree", 4, 132, 1.2f),
            new("pine_tree", 14, 140, 1.3f), new("pine_tree", 22, 150, 1.1f),
            // 灌木 / 岩石 / 灯 / 船
            new("shore_rocks", -1, 42, 0.9f, 30, false), new("shore_rocks", 4.5f, 66, 0.8f, -40, false), new("shore_rocks", 12, 88, 1f, 10, false),
            new("shore_rocks", -12, 27, 1.1f, 70, false),
            new("lamp_post", 5.4f, 34.9f, 1f, 90, false), new("lamp_post", -4.5f, 41, 1f, 0),
            new("rowboat", 9.5f, 38.5f, 1f, 75, false),
            // 前景香蒲（窗下两角）
            new("reeds", -7, 14, 1.2f, 0, false), new("reeds", -9.5f, 17, 1f, 50, false), new("reeds", -5, 18.5f, 0.9f, 120, false),
            new("reeds", 7.5f, 13, 1.1f, 200, false), new("reeds", 9.8f, 16, 1.3f, 90, false), new("reeds", 5.5f, 17.5f, 0.85f, 10, false),
            new("reeds", -11, 22, 1f, 30, false), new("reeds", 12.5f, 20, 1f, 160, false),
        };

        static readonly Dictionary<string, Color> Placeholder = new()
        {
            ["cottage_tile"] = new Color(0.85f, 0.45f, 0.3f), ["cottage_slate"] = new Color(0.45f, 0.5f, 0.6f), ["cottage_cream"] = new Color(0.9f, 0.82f, 0.62f),
            ["house_tall"] = new Color(0.7f, 0.55f, 0.4f), ["windmill_tower"] = new Color(0.92f, 0.9f, 0.85f), ["bell_tower"] = new Color(0.62f, 0.6f, 0.56f),
            ["pine_tree"] = new Color(0.2f, 0.32f, 0.2f), ["round_tree"] = new Color(0.45f, 0.5f, 0.25f), ["bush_flower"] = new Color(0.4f, 0.55f, 0.3f),
            ["reeds"] = new Color(0.45f, 0.5f, 0.25f), ["shore_rocks"] = new Color(0.5f, 0.5f, 0.5f), ["lamp_post"] = new Color(0.2f, 0.2f, 0.18f),
            ["rowboat"] = new Color(0.55f, 0.32f, 0.18f),
            ["great_oak"] = new Color(0.3f, 0.42f, 0.2f), ["willow_tree"] = new Color(0.45f, 0.55f, 0.3f), ["garden_bench"] = new Color(0.5f, 0.35f, 0.22f),
            ["wildflower_patch"] = new Color(0.7f, 0.6f, 0.85f), ["picket_fence"] = new Color(0.92f, 0.9f, 0.85f), ["stepping_stones"] = new Color(0.55f, 0.55f, 0.52f),
            ["lily_pads"] = new Color(0.3f, 0.5f, 0.3f),
        };

        static GameObject Spawn(Item it, Transform parent)
        {
            // 圆树（一团球形树冠）远看像低模球，用户不要：全部换成松树（松树的远景替身是锥形，远看也自然）
            if (it.name == "round_tree") it = new Item("pine_tree", it.x, it.z, it.scale * 0.95f, it.yaw, it.face) { world = it.world };
            var pf = TKArt.P2Prefab(it.name);
            GameObject g;
            if (it.name == "lamp_post" && LampPostModel()) g = LampPostInstance(parent);   // 路灯：程序建模的新灯（旧 Tripo 灯杆是歪的、灯笼和吊臂糊成一团）
            else if (pf) g = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent);
            else
            {
                // 占位：按标准尺寸的方块 / 圆柱
                var sz = Sizes[it.name].size;
                bool tree = it.name.Contains("tree") || it.name == "reeds";
                g = new GameObject(it.name + " (占位)");
                g.transform.SetParent(parent, false);
                var body = GameObject.CreatePrimitive(tree ? PrimitiveType.Capsule : PrimitiveType.Cube);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(g.transform, false);
                var size = tree ? new Vector3(sz * 0.45f, sz * 0.5f, sz * 0.45f) : Sizes[it.name].axis == 1 ? new Vector3(sz * 0.35f, sz * 0.25f, sz) : new Vector3(sz * 0.9f, sz, sz * 0.75f);
                body.transform.localScale = size;
                body.transform.localPosition = Vector3.up * (tree ? sz * 0.5f : size.y / 2f);
                body.GetComponent<Renderer>().sharedMaterial = TKArt.Lit("Placeholder_" + it.name, Placeholder[it.name], 0.1f);
            }
            bool near = it.name == "reeds" || it.world;
            var w = near ? new Vector2(it.x, it.z) : FromLayout(it.x, it.z);
            float px = w.x, pz = w.y;
            float y = Height(px, pz);
            if (it.name == "rowboat") y = WaterY - 0.25f;
            if (it.name == "lamp_post" && it.x > 0) { var dp = DockB - (DockB - DockA).normalized * 1.2f; px = dp.x; pz = dp.z; y = DeckY + 0.03f; }   // 码头尽头的灯立在甲板中线上（之前偏到边外，底座悬在水上）
            if (it.name == "rowboat") { px = DockB.x + 2.6f; pz = DockB.z + 2.2f; }
            g.transform.localPosition = new Vector3(px, y - 0.15f * it.scale, pz);
            // 房子正面朝向窗户（再加一点自然的偏转）；其他按给定朝向
            float yaw = it.face ? Mathf.Atan2(-px, -pz) * Mathf.Rad2Deg + it.yaw : it.yaw;
            g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            g.transform.localScale = Vector3.one * it.scale;
            if (it.name == "windmill_tower" && pf) Sails(g.transform);
            // 第十四轮：路灯不再加光晕（用户：去掉后面灯下面那个点光）；灯笼靠模型自己的自发光
            return g;
        }

        const string LampFbx = "Assets/_Twilight/Art/Models/Props/LampPost.fbx";
        static GameObject LampPostModel()
        {
            // 要读顶点（量灯笼位置）+ 运行时合批：网格必须可读
            if (AssetImporter.GetAtPath(LampFbx) is ModelImporter mi && (!mi.isReadable || mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard))
            { mi.isReadable = true; mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; mi.SaveAndReimport(); }
            return TKArt.Model(LampFbx);
        }

        /// <summary>新路灯（Tools/blender_lamppost.py）：铸铁 + 灯笼玻璃（暖色自发光，跟着天色由 TwilightSky 调亮度）。
        /// 模型本地：底面中心在原点、吊臂伸向 Blender +X；导进 Unity 后吊臂朝 −X（FBX 轴转换），灯笼中心约 (−0.71, 2.42, 0)。</summary>
        static GameObject LampPostInstance(Transform parent)
        {
            // FBX 根节点自带轴转换旋转（−90° X），Spawn 会覆盖根节点朝向：包一层空节点，模型挂在下面保留自己的旋转
            var g = new GameObject("lamp_post");
            g.transform.SetParent(parent, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(LampPostModel(), g.transform);
            model.name = "Model";
            var iron = TKArt.Lit("LampPost_Iron", new Color(0.36f, 0.29f, 0.22f), 0.18f, 0f);   // 第十六轮：暖色旧铜、哑光（纯黑铸铁远看像一圈黑描边）
            var glass = TKArt.Lit("LampPost_Glass", new Color(1f, 0.86f, 0.62f), 0.6f, 0f, emission: new Color(1f, 0.66f, 0.32f) * 1.2f);
            glass.EnableKeyword("_EMISSION"); glass.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;   // 夜色控制器点亮之前先亮着
            EditorUtility.SetDirty(glass);
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = ms[i] && ms[i].name.StartsWith("Glass") ? glass : iron;
                r.sharedMaterials = ms;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            return g;
        }

        static void NewLampGlow(Transform lamp)
        {
            // 灯笼位置 = 玻璃那组子网格的顶点中心（世界坐标），光晕放在那里
            var r = lamp.GetComponentInChildren<MeshRenderer>(); var mf = r ? r.GetComponent<MeshFilter>() : null;
            Vector3 world = lamp.TransformPoint(new Vector3(-0.71f, 2.42f, 0f));
            if (mf && mf.sharedMesh && mf.sharedMesh.isReadable)
            {
                var ms = r.sharedMaterials; var mesh = mf.sharedMesh; var vs = mesh.vertices;
                for (int k = 0; k < ms.Length && k < mesh.subMeshCount; k++)
                {
                    if (!ms[k] || !ms[k].name.Contains("Glass")) continue;
                    var sum = Vector3.zero; var idx = mesh.GetTriangles(k);
                    foreach (var i in idx) sum += vs[i];
                    if (idx.Length > 0) world = mf.transform.TransformPoint(sum / idx.Length);
                }
            }
            LampGlow(lamp);
            var q = lamp.Find("LampGlow");
            if (q) { q.position = world; q.localScale = Vector3.one * 0.7f; }
        }

        /// <summary>路灯灯笼的光（入夜变亮）：灯笼挂在弯臂下面，P2 模型本地（尺寸 3.2 m 高）约 (0.07·3.2, 0.68·3.2, -0.09·3.2)。
        /// 之前路灯只靠贴图里的自发光，灯罩是暗的、夜里像熄了。</summary>
        internal static void LampGlow(Transform lamp)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "LampGlow";
            q.transform.SetParent(lamp, false);
            // 灯笼位置直接从网格量：高度 55%–80% 之间、离灯杆（底部 25% 的中心）较远的那团顶点的中心
            var pts = new List<Vector3>(); float y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var mf in lamp.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.name.Contains("LOD0")) continue;               // 只量最精细的那一级
                var mesh = mf.sharedMesh; if (!mesh || !mesh.isReadable) continue;
                foreach (var v in mesh.vertices) { var p = lamp.InverseTransformPoint(mf.transform.TransformPoint(v)); pts.Add(p); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
                break;
            }
            var at = new Vector3(0.23f, 2.2f, 0.29f);
            if (pts.Count > 0)
            {
                float H = y1 - y0; Vector3 axis = Vector3.zero; int na = 0;
                foreach (var p in pts) if ((p.y - y0) / H < 0.25f) { axis += new Vector3(p.x, 0, p.z); na++; }
                axis /= Mathf.Max(1, na);
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (var p in pts)
                {
                    float t = (p.y - y0) / H;
                    if (t > 0.57f && t < 0.69f && (new Vector3(p.x, 0, p.z) - axis).magnitude > 0.12f * H) { sum += p; n++; }   // 只取灯笼那段（再往上是弯臂）
                }
                if (n > 20) at = sum / n;
            }
            q.transform.localPosition = at;
            q.transform.localScale = Vector3.one * 0.9f;
            string mp = $"{TKArt.MatDir}/LampGlow.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m) { m = new Material(Shader.Find("Twilight/LampGlow")); AssetDatabase.CreateAsset(m, mp); }
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            q.layer = lamp.gameObject.layer;
        }

        /// <summary>窗户所在的老石墙残垣（Blender 程序建模）：只在世界铺开后出现（村庄层），窗户就嵌在这段断墙里，不再悬空。</summary>
        static void RuinWall(Transform root)
        {
            var src = TKArt.Model("Assets/_Twilight/Art/Models/Window/RuinWall.fbx");
            if (!src) return;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(src, root);
            g.name = "RuinWall";
            g.transform.localPosition = Vector3.zero;
            g.transform.localRotation = Quaternion.Euler(0, 180f, 0);     // 和窗户模型一样：房间一侧朝 -Z
            bool tex = Tex($"{TKArt.TexDir}/stone_wall.png");
            var stone = TKArt.Lit("Ruin_Stone", tex ? Color.white : new Color(0.62f, 0.58f, 0.52f), 0.08f,
                albedo: tex ? $"{TKArt.TexDir}/stone_wall.png" : null, tiling: new Vector2(1.2f, 1.2f));
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = stone;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }

        /// <summary>风车叶（Blender 程序建模）：装在塔身轴头（预制体本地 (-0.35, 7.6, 4.1)），慢慢转。</summary>
        internal static void Sails(Transform tower)
        {
            var src = TKArt.Model("Assets/_Twilight/Art/Models/Windmill/Sails.fbx");
            if (!src) return;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(src, tower);
            g.name = "Sails";
            g.transform.localPosition = new Vector3(-0.35f, 7.6f, 4.25f);
            g.transform.localRotation = Quaternion.Euler(0, 0, 20f);
            var wood = TKArt.Lit("Sail_Wood", new Color(0.62f, 0.5f, 0.4f), 0.1f, albedo: $"{TKArt.TexDir}/plank_wood.png", tiling: new Vector2(0.6f, 0.6f));
            var cloth = TKArt.Lit("Sail_Cloth", new Color(0.95f, 0.9f, 0.78f), 0.05f, albedo: $"{TKArt.TexDir}/cotton_white.png", tiling: new Vector2(2f, 2f));
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = r.name.StartsWith("Cloth") ? cloth : wood;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
            g.AddComponent<Spin>().speed = 12f;
        }

        /// <summary>玩家身边的湖边小花园（村庄局部坐标，玩家在 (0, -2.7)，面朝 +Z）：
        /// 左后大橡树 + 树下长椅和路灯、身后一圈白栅栏、脚边野花、踏脚石小路通到右前方湖边、右岸垂柳和香蒲、近岸睡莲、身后山坡树林。</summary>
        static void HomeGarden(Transform props)
        {
            var rnd = new System.Random(31);
            void Put(string n, float x, float z, float sc = 1f, float yaw = 0f, float? y = null)
            {
                var g = Spawn(new Item(n, x, z, sc, yaw, false) { world = true }, props);
                if (y.HasValue) { var p = g.transform.localPosition; p.y = y.Value; g.transform.localPosition = p; }
            }
            // 大橡树在栅栏外（树根不插进栅栏），底下那圈根盘压进土里 0.45 m（不露出一块“底座”）
            Put("great_oak", -12.5f, -19f, 1f, 30f, Height(-12.5f, -19f) - 0.45f);
            Put("garden_bench", -5.0f, -8.6f, 1f, 20f);                 // 面朝湖（+Z 略偏右），离栅栏留出空
            Put("lamp_post", -3.0f, -8.4f, 0.9f, 200f);
            // 白栅栏：身后半径 12 m 的弧，正后方留出口
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Lerp(-150f, -30f, i / 8f) * Mathf.Deg2Rad;
                if (i == 4) continue;
                float x = Mathf.Cos(a) * 12f, z = -2.7f + Mathf.Sin(a) * 12f;
                // 板面正对玩家（预制体正面 +Z 朝向圆心）
                Put("picket_fence", x, z, 1f, Mathf.Atan2(-x, -(z + 2.7f)) * Mathf.Rad2Deg + (float)(rnd.NextDouble() - 0.5) * 6f);
            }
            // 脚边野花
            for (int i = 0; i < 14; i++)
            {
                float r = 3.2f + (float)rnd.NextDouble() * 9f, a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * r, z = -2.7f + Mathf.Sin(a) * r;
                if (Mathf.Abs(x) < 1.2f && z > -3.5f && z < 1.5f) continue;          // 桌子底下不放
                if (Height(x, z) < WaterY + 0.5f) continue;
                Put("wildflower_patch", x, z, 0.8f + (float)rnd.NextDouble() * 0.6f, (float)rnd.NextDouble() * 360f);
            }
            // 踏脚石：从桌子右边通到右前方湖边
            // 第十四轮（用户：前景石头浮空）：原 P2 踏脚石模型几块石头高低不平，坡上总有一块悬着。
            // 改成一块块程序生成的扁石板：每块按脚下地面的高度和坡度摆，压进土里 40%（看不到底面，不可能悬空）
            FlatStones(props, new Vector2(2.2f, -3.5f), new Vector2(11f, 7.5f), rnd);

            // 右岸：垂柳、香蒲、岩石
            Put("willow_tree", HomeShoreX(-2f) - 4f, -2f, 1f, 70f);
            for (int i = 0; i < 9; i++)
            {
                float z = -24f + i * 3.6f + (float)rnd.NextDouble() * 1.5f;
                Put("reeds", HomeShoreX(z) + 2.5f + (float)rnd.NextDouble() * 2f, z, 0.9f + (float)rnd.NextDouble() * 0.4f, (float)rnd.NextDouble() * 360f);
            }
            foreach (var z in new[] { -14f, -5f, 4f })
                Put("shore_rocks", HomeShoreX(z) - 1f, z, 0.7f + (float)rnd.NextDouble() * 0.4f, (float)rnd.NextDouble() * 360f);
            // 近岸睡莲
            foreach (var (x, z) in new[] { (7f, 15f), (13f, 12f), (-5f, 17f), (19f, 9f), (24f, 3f) })
                Put("lily_pads", x + HomeShoreX(z) * 0f, z, 0.9f + (float)rnd.NextDouble() * 0.5f, (float)rnd.NextDouble() * 360f, WaterY + 0.02f);
            // 身后山坡树林
            int trees = 0;
            for (int guard = 0; trees < 34 && guard < 3000; guard++)
            {
                float x = -90f + (float)rnd.NextDouble() * 120f, z = -20f - (float)rnd.NextDouble() * 95f;
                if (x > HomeShoreX(z) - 6f) continue;
                if (new Vector2(x - -10.5f, z - -15f).magnitude < 9f) continue;   // 大橡树周围空出来
                Put(rnd.NextDouble() < 0.6 ? "pine_tree" : "round_tree", x, z, 0.85f + (float)rnd.NextDouble() * 0.5f, (float)rnd.NextDouble() * 360f);
                trees++;
            }
        }

    /// <summary>一串扁石板（踏脚石小路）：从 a 到 b 每 0.85 m 一块，左右交错一点，大小、朝向随机；
    /// 每块是压扁的低多边形卵石，按地面坡度倾斜、压进土里 40%。</summary>
    static void FlatStones(Transform props, Vector2 a, Vector2 b, System.Random rnd)
    {
        var mat = TKArt.Lit("Path_Stone", new Color(1.0f, 0.95f, 0.86f), 0.08f, 0f, albedo: $"{TKArt.TexDir}/ground_rock.png", tiling: new Vector2(0.5f, 0.5f));   // 第十七轮：自己的石头材质（原来借用小岛的白釉 Isle_Rock，变白发亮）
        var mesh = StoneMesh();
        float len = Vector2.Distance(a, b);
        int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.85f));
        var dir = (b - a).normalized; var side = new Vector2(-dir.y, dir.x);
        for (int i = 0; i <= n; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)n) + side * ((i % 2 == 0 ? 1 : -1) * 0.18f + (float)(rnd.NextDouble() - 0.5) * 0.12f);
            float h = Height(p.x, p.y);
            if (h < WaterY + 0.05f) continue;                        // 到水边为止
            float e = 0.25f;
            var nrm = new Vector3(Height(p.x - e, p.y) - Height(p.x + e, p.y), 2f * e, Height(p.x, p.y - e) - Height(p.x, p.y + e)).normalized;
            float w = 0.42f + (float)rnd.NextDouble() * 0.2f, d = w * (0.7f + (float)rnd.NextDouble() * 0.2f), t = 0.09f;
            var g = new GameObject("FlatStone");
            g.layer = props.gameObject.layer;                       // 和村子同一层（窗中村模板 / 铺开遮罩按层渲染）
            g.transform.SetParent(props, false);
            g.transform.localPosition = new Vector3(p.x, h - t * 0.4f, p.y);
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, nrm) * Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
            g.transform.localScale = new Vector3(w, t, d);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        }
    }

    /// <summary>单位卵石：上面微微鼓起、边缘圆滑、底面平（存成资源，各块共用）。</summary>
    static Mesh StoneMesh()
    {
        string path = "Assets/_Twilight/Art/Models/Props/FlatStone.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m) return m;
        m = new Mesh { name = "FlatStone" };
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        const int U = 18, V = 5;
        var rnd = new System.Random(5);
        var wob = new float[U];
        for (int i = 0; i < U; i++) wob[i] = 0.88f + (float)rnd.NextDouble() * 0.2f;
        for (int j = 0; j <= V; j++)
        {
            float ph = Mathf.PI / 2 * j / V;                           // 0 = 边缘，π/2 = 顶
            float rr = Mathf.Cos(ph), y = Mathf.Sin(ph);
            for (int i = 0; i < U; i++)
            {
                float th = 2 * Mathf.PI * i / U;
                v.Add(new Vector3(Mathf.Cos(th) * 0.5f * rr * wob[i], y, Mathf.Sin(th) * 0.5f * rr * wob[i]));
                uv.Add(new Vector2(Mathf.Cos(th) * rr * 0.5f + 0.5f, Mathf.Sin(th) * rr * 0.5f + 0.5f));
            }
        }
        for (int j = 0; j < V; j++)
            for (int i = 0; i < U; i++)
            {
                int a0 = j * U + i, a1 = j * U + (i + 1) % U, b0 = a0 + U, b1 = a1 + U;
                tri.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
            }
        int bot = v.Count; v.Add(new Vector3(0, -0.3f, 0)); uv.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < U; i++) tri.AddRange(new[] { bot, i, (i + 1) % U });
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
        /// <summary>山坡植被：在布局空间里随机撒树和花丛，避开房子（世界距离 9 m）、土路（3 m）和水边。</summary>
        static void Scatter(Transform props)
        {
            var rnd = new System.Random(11);
            var taken = new List<Vector2>();
            foreach (var it in Items)
                if (Sizes[it.name].emissive) { var w = FromLayout(it.x, it.z); taken.Add(w); }
            int trees = 0, bushes = 0, guard = 0;
            while (trees < 56 && guard++ < 8000)   // 第十四轮：山坡只撒松树，不撒花丛（远处看是一个个浅色球）
            {
                float lz = 6f + (float)rnd.NextDouble() * 174f;
                float lx = ShoreX(lz, out _) - 3f - (float)rnd.NextDouble() * 70f;
                var w = FromLayout(lx, lz);
                if (Height(w.x, w.y) < WaterY + 0.6f) continue;
                if (PathDist(w.x, w.y) < 3f) continue;
                bool near = false;
                foreach (var t in taken) if (Vector2.Distance(t, w) < 9f) { near = true; break; }
                if (near) continue;
                bool tree = true;
                string n = tree ? "pine_tree" : "bush_flower";   // 圆树的远处替身是一团球，不要
                float sc = tree ? 0.8f + (float)rnd.NextDouble() * 0.5f : 0.8f + (float)rnd.NextDouble() * 0.6f;
                Spawn(new Item(n, w.x, w.y, sc, (float)rnd.NextDouble() * 360f, false) { world = true }, props);
                taken.Add(w);
                if (tree) trees++; else bushes++;
            }
        }

        /// <summary>玩家周围的立体草叶（约 1.8 万片）：近处密、远处疏，水边和土路不长；少量叶尖开小花。</summary>
        static void Grass(Transform root)
        {
            var rnd = new System.Random(21);
            var center = new Vector2(0f, -2.7f);                  // 玩家（村庄局部）
            var v = new List<Vector3>(); var col = new List<Color>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tri = new List<int>();
            Color[] greens = { new(0.30f, 0.40f, 0.18f), new(0.36f, 0.45f, 0.20f), new(0.27f, 0.37f, 0.17f), new(0.42f, 0.47f, 0.22f), new(0.47f, 0.47f, 0.24f) };
            Color[] flowers = { new(0.97f, 0.95f, 0.88f), new(1f, 0.82f, 0.30f), new(0.72f, 0.66f, 0.95f), new(0.98f, 0.70f, 0.75f) };
            int blades = 0, clumps = 0;
            for (int guard = 0; blades < 24000 && guard < 300000; guard++)
            {
                // 一丛一丛长：丛中心按面积均匀撒，再按距离丢弃一部分 → 近处密
                float r = 20f * Mathf.Sqrt((float)rnd.NextDouble()), a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                if (rnd.NextDouble() > Mathf.Lerp(1f, 0.1f, r / 20f)) continue;
                float cx = center.x + Mathf.Cos(a) * r, cz = center.y + Mathf.Sin(a) * r;
                if (Height(cx, cz) < WaterY + 0.35f || PathDist(cx, cz) < 0.9f) continue;
                var c0 = greens[rnd.Next(greens.Length)];
                float clumpH = Mathf.Lerp(0.10f, 0.26f, (float)rnd.NextDouble()) * Mathf.Lerp(1f, 0.7f, r / 20f);
                int n = 5 + rnd.Next(5);
                clumps++;
                for (int k = 0; k < n; k++)
                {
                    float x = cx + ((float)rnd.NextDouble() - 0.5f) * 0.14f, z = cz + ((float)rnd.NextDouble() - 0.5f) * 0.14f;
                    float y = Height(x, z);
                    float hgt = clumpH * Mathf.Lerp(0.6f, 1.15f, (float)rnd.NextDouble());
                    float wid = Mathf.Lerp(0.016f, 0.03f, (float)rnd.NextDouble());
                    float yaw = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    // 向丛外倒：叶尖朝外散开
                    var outDir = new Vector3(x - cx, 0, z - cz).normalized;
                    var side = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * wid;
                    var bend = (outDir * 0.7f + new Vector3(-Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * 0.3f) * hgt * Mathf.Lerp(0.15f, 0.55f, (float)rnd.NextDouble());
                    var tint = c0 * Mathf.Lerp(0.88f, 1.1f, (float)rnd.NextDouble()); tint.a = 1;
                    var b0 = new Vector3(x, y - 0.02f, z);
                    int i0 = v.Count;
                    float seed = (float)rnd.NextDouble();
                    v.Add(b0 - side); v.Add(b0 + side);
                    v.Add(b0 + Vector3.up * hgt * 0.55f + bend * 0.35f - side * 0.7f); v.Add(b0 + Vector3.up * hgt * 0.55f + bend * 0.35f + side * 0.7f);
                    v.Add(b0 + Vector3.up * hgt + bend);
                    foreach (var hh in new[] { 0f, 0f, 0.55f, 0.55f, 1f }) { uv.Add(new Vector2(0, hh)); uv2.Add(new Vector2(seed, hgt)); col.Add(tint); }
                    tri.AddRange(new[] { i0, i0 + 2, i0 + 1, i0 + 1, i0 + 2, i0 + 3, i0 + 2, i0 + 4, i0 + 3 });
                    blades++;
                }
                // 约 6% 的丛里开一朵小花（两片小十字）
                if (rnd.NextDouble() < 0.06)
                {
                    var fc = flowers[rnd.Next(flowers.Length)];
                    var tip = new Vector3(cx, Height(cx, cz) + clumpH * 1.15f, cz); float fs = 0.022f; float seed = (float)rnd.NextDouble();
                    foreach (var dir in new[] { Vector3.right, Vector3.forward })
                    {
                        int f0 = v.Count;
                        v.Add(tip - dir * fs); v.Add(tip + dir * fs); v.Add(tip + dir * fs + Vector3.up * 0.032f); v.Add(tip - dir * fs + Vector3.up * 0.032f);
                        for (int q = 0; q < 4; q++) { uv.Add(new Vector2(0, 1)); uv2.Add(new Vector2(seed, clumpH)); col.Add(fc); }
                        tri.AddRange(new[] { f0, f0 + 2, f0 + 1, f0, f0 + 3, f0 + 2 });
                    }
                }
            }
            // 按玩家周围 8 个扇区拆成 8 张网格（视锥剔除：身后的草不画）
            var gRoot = new GameObject("Grass").transform; gRoot.SetParent(root, false);
            var mat = ShaderMat("Village_Grass", "Twilight/GrassBlades");
            TKArt.EnsureFolder("Assets/_Twilight/Art/Generated/Grass");
            var sv = new List<Vector3>[8]; var sc = new List<Color>[8]; var su = new List<Vector2>[8]; var su2 = new List<Vector2>[8]; var st = new List<int>[8];
            for (int k = 0; k < 8; k++) { sv[k] = new(); sc[k] = new(); su[k] = new(); su2[k] = new(); st[k] = new(); }
            for (int f = 0; f < tri.Count; f += 3)
            {
                // 一片草叶 / 一朵花的三角形按第一个顶点所在扇区分
                var p0 = v[tri[f]];
                float ang = Mathf.Atan2(p0.z - center.y, p0.x - center.x);
                int k = Mathf.Clamp(Mathf.FloorToInt((ang + Mathf.PI) / (Mathf.PI * 2f) * 8f), 0, 7);
                for (int q = 0; q < 3; q++)
                {
                    int src = tri[f + q];
                    st[k].Add(sv[k].Count);
                    sv[k].Add(v[src]); sc[k].Add(col[src]); su[k].Add(uv[src]); su2[k].Add(uv2[src]);
                }
            }
            for (int k = 0; k < 8; k++)
            {
                var m = new Mesh { name = "Grass" + k, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(sv[k]); m.SetColors(sc[k]); m.SetUVs(0, su[k]); m.SetUVs(1, su2[k]); m.SetTriangles(st[k], 0);
                m.RecalculateBounds(); m.bounds = new Bounds(m.bounds.center, m.bounds.size + Vector3.one * 0.5f);
                var path = $"Assets/_Twilight/Art/Generated/Grass/Grass{k}.asset";
                AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
                MeshObj("Grass " + k, gRoot, m, mat);
            }
            Debug.Log($"草叶 {blades} 片（{clumps} 丛），{tri.Count / 3} 三角");
        }

        /// <summary>萤火虫：沿岸（香蒲丛、码头、村边）撒 160 只，都在一张网格里。</summary>
        static void Fireflies(Transform root)
        {
            var rnd = new System.Random(5);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var seed = new List<Vector2>(); var tri = new List<int>();
            void Add(Vector3 c)
            {
                int b = v.Count; float s1 = (float)rnd.NextDouble(), s2 = (float)rnd.NextDouble();
                foreach (var q in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) }) { v.Add(c); uv.Add(q); seed.Add(new Vector2(s1, s2)); }
                tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            for (int i = 0; i < 160; i++)
            {
                Vector3 c;
                double k = rnd.NextDouble();
                if (k < 0.35) c = new Vector3(-14f + (float)rnd.NextDouble() * 30f, 0, 11f + (float)rnd.NextDouble() * 14f);   // 窗下香蒲一带
                else if (k < 0.6) { var d = Vector3.Lerp(DockA, DockB, (float)rnd.NextDouble()); c = d + new Vector3((float)rnd.NextDouble() * 8f - 4f, 0, (float)rnd.NextDouble() * 8f - 4f); }
                else
                {
                    float lz = 30f + (float)rnd.NextDouble() * 70f;
                    var w = FromLayout(ShoreX(lz, out _) + (float)rnd.NextDouble() * 10f - 7f, lz);
                    c = new Vector3(w.x, 0, w.y);
                }
                c.y = Mathf.Max(Height(c.x, c.z), WaterY) + 0.4f + (float)rnd.NextDouble() * 2.2f;
                Add(c);
            }
            var m = new Mesh { name = "Fireflies" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(1, seed); m.SetTriangles(tri, 0);
            m.bounds = new Bounds(new Vector3(0, 0, 60), new Vector3(200, 40, 200));     // 着色器里会移动，包围盒放大
            var path = "Assets/_Twilight/Art/Generated/Fireflies.asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
            MeshObj("Fireflies", root, m, ShaderMat("Village_Fireflies", "Twilight/Fireflies"));
        }

        // ---------------------------------------------------------------- 码头（程序化：木板 + 桩）

        const float DeckY = WaterY + 0.55f;
        static Vector3 DockA { get { var w = FromLayout(-5.8f, 36.2f); return new Vector3(w.x, DeckY, w.y); } }
        // 码头从岸边伸进湖里 13 m（世界坐标，方向朝右前方，侧面对着窗户更好看）
        static Vector3 DockB => DockA + new Vector3(1f, 0f, -0.12f).normalized * 13f;

        static void Dock(Transform parent)
        {
            var root = new GameObject("Dock").transform;
            root.SetParent(parent, false);
            var plank = Tex($"{TKArt.TexDir}/plank_wood.png");
            var wood = TKArt.Lit("Dock_Wood", plank ? Color.white : new Color(0.5f, 0.4f, 0.3f), 0.12f, albedo: plank ? $"{TKArt.TexDir}/plank_wood.png" : null, tiling: new Vector2(0.5f, 0.5f));
            var post = TKArt.Lit("Dock_Post", new Color(0.32f, 0.24f, 0.18f), 0.1f);
            // 甲板：从岸边 (-5, 36) 向右伸到 (6, 35)
            Vector3 a = DockA, b = DockB;
            var dir = (b - a).normalized; float len = Vector3.Distance(a, b);
            var rot = Quaternion.LookRotation(dir, Vector3.up);          // 木板：长边横跨码头（本地 x），薄边顺着码头（本地 z）
            int n = Mathf.RoundToInt(len / 0.24f);
            var rnd = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                var p = a + dir * ((i + 0.5f) * len / n);
                var g = Box("Plank", root, p + Vector3.up * (float)(rnd.NextDouble() * 0.015), new Vector3(2.2f + (float)rnd.NextDouble() * 0.1f, 0.06f, 0.22f), wood);
                g.transform.localRotation = rot * Quaternion.Euler(0, (float)(rnd.NextDouble() - 0.5) * 2f, 0);
            }
            // 桩：两排
            var side = rot * Vector3.right;
            int nPost = Mathf.Max(4, Mathf.RoundToInt(len / 2.6f));
            for (int i = 0; i <= nPost; i++)
            {
                var p = a + dir * (i * len / nPost);
                foreach (float s in new[] { -1.15f, 1.15f })
                {
                    var q = p + side * s;
                    float bottom = WaterY - 1.2f, top = DeckY + 0.35f + (i == nPost ? 0.3f : 0f);
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(c.GetComponent<Collider>());
                    c.name = "Post"; c.transform.SetParent(root, false);
                    c.transform.localPosition = new Vector3(q.x, (bottom + top) / 2f, q.z);
                    c.transform.localScale = new Vector3(0.2f, (top - bottom) / 2f, 0.2f);
                    c.GetComponent<Renderer>().sharedMaterial = post;
                }
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        // ---------------------------------------------------------------- 入口

        /// <summary>湖面倒影：在“座位眼睛关于水面的镜像点”烘一张立方体贴图（只有村子，天空透明），湖面按反射方向采样。
        /// 对固定的座位视角，这和真正的平面镜反射等价，只多一次贴图采样。天色变化时窗灯亮度不同 → 分别烘黄昏 / 夜两张。</summary>
        public static bool FlipRefl = false;
        public static Cubemap BakeReflection(Transform village, string name, int size = 512)
        {
            var window = village.parent;
            var eye = TKCapture.SeatEye;
            float waterWorld = window.TransformPoint(new Vector3(0, WaterY, 0)).y;
            var mirror = new Vector3(eye.x, 2f * waterWorld - eye.y, eye.z);
            var sky = village.Find("Sky"); var lake = village.Find("Lake");
            sky.gameObject.SetActive(false); lake.gameObject.SetActive(false);
            var go = new GameObject("__reflcam");
            var mask = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                // 模板：相机周围一圈窗洞遮罩（双面），村庄才会画出来
                Object.DestroyImmediate(mask.GetComponent<Collider>());
                mask.layer = TKRenderSetup.PortalLayer;
                var mm = new Material(Shader.Find("Twilight/PortalMask")); mm.SetFloat("_Cull", 0);
                mask.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/PortalMaskBoth.mat") ?? mm;
                mask.transform.position = mirror; mask.transform.localScale = Vector3.one * 1.2f;   // 半径 0.6 > 近裁面 0.3
                var cam = go.AddComponent<Camera>();
                cam.transform.position = mirror;
                cam.nearClipPlane = 0.3f; cam.farClipPlane = 2000f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.cullingMask = (1 << TKRenderSetup.VillageLayer) | (1 << TKRenderSetup.PortalLayer);
                // URP 下 RenderToCubemap(Cubemap) 不渲染，要先渲到立方体 RenderTexture 再逐面读回
                var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { dimension = UnityEngine.Rendering.TextureDimension.Cube };
                bool ok = cam.RenderToCubemap(rt);
                var cube = new Cubemap(size, TextureFormat.RGBA32, true);
                var tmp = new Texture2D(size, size, TextureFormat.RGBA32, false);
                foreach (CubemapFace f in System.Enum.GetValues(typeof(CubemapFace)))
                {
                    if (f == CubemapFace.Unknown) continue;
                    Graphics.SetRenderTarget(rt, 0, f);
                    tmp.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    tmp.Apply();
                    // ReadPixels 读出来是自下而上，立方体面是自上而下：竖向翻转
                    var src = tmp.GetPixels(); var dst = new Color[src.Length];
                    if (FlipRefl) for (int y = 0; y < size; y++) System.Array.Copy(src, y * size, dst, (size - 1 - y) * size, size);
                    else dst = src;
                    cube.SetPixels(dst, f);
                }
                RenderTexture.active = null;
                Object.DestroyImmediate(tmp); rt.Release(); Object.DestroyImmediate(rt);
                cube.Apply(true);
                Debug.Log($"倒影 {name}: RenderToCubemap = {ok}");
                TKArt.EnsureFolder("Assets/_Twilight/Art/Generated");
                var path = $"Assets/_Twilight/Art/Generated/{name}.cubemap";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(cube, path);
                return cube;
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(mask);
                sky.gameObject.SetActive(true); lake.gameObject.SetActive(true);
            }
        }

        /// <summary>把 P2 已完成的模型全部接入（没完成的保持占位）。</summary>
        [MenuItem("Twilight/2 接入已完成的 P2 模型")]
        public static void ImportAll()
        {
            foreach (var kv in Sizes)
            {
                var pf = TKArt.ImportP2(kv.Key, kv.Value.size, kv.Value.axis, 0.15f, kv.Value.emissive, TwoSided.Contains(kv.Key));
                if (pf && Tints.TryGetValue(kv.Key, out var tint))
                    AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/P2_{kv.Key}.mat").SetColor("_BaseColor", tint);
                Debug.Log($"{kv.Key}: {(pf ? "已接入" : "未完成")}");
            }
        }

        /// <summary>在窗户下建村：天空、远山、湖、地形、码头、房子和树。返回村庄根节点。</summary>
        public static Transform Build(Transform window, out TwilightSky skyCtl)
        {
            var root = new GameObject("Village").transform;
            root.SetParent(window, false);

            // 天色控制器
            skyCtl = root.gameObject.AddComponent<TwilightSky>();
            skyCtl.sky = AssetDatabase.LoadAssetAtPath<Texture2DArray>($"{Sky}/SkyAtlas.png");
            skyCtl.facing = window;
            skyCtl.presets = TwilightSky.DefaultPresets();

            // 天空球（无限远着色，球只是载体）
            var skyGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(skyGo.GetComponent<Collider>());
            skyGo.name = "Sky"; skyGo.transform.SetParent(root, false);
            skyGo.transform.localScale = Vector3.one * 1600f;
            var skyMat = ShaderMat("Village_Sky", "Twilight/SkyPano");
            skyMat.SetFloat("_Squash", 1.6f);
            var sr = skyGo.GetComponent<Renderer>(); sr.sharedMaterial = skyMat; sr.shadowCastingMode = ShadowCastingMode.Off; sr.receiveShadows = false;

            // 远山：150° 圆弧，顶在 +7°，底在水面以下
            var arc = Arc("MountainArc", 650f, -123.8f, 236.2f, 20, 9.5f - 35f, 9.5f, 8);
            var mats = new Material[4];
            for (int i = 0; i < 4; i++)
            {
                mats[i] = ShaderMat($"Mountains_{i}", "Twilight/PanoBand");
                mats[i].SetTexture("_MainTex", Tex($"{Sky}/Mountains_{i}.png"));
                mats[i].SetColor("_Tint", new Color(0.78f, 0.8f, 0.95f));
                mats[i].SetFloat("_Haze", 0.08f);
                mats[i].SetFloat("_HazeLow", 0.18f);
            }
            var mg = MeshObj("Mountains", root, arc, mats[0]);
            mg.transform.localPosition = new Vector3(0, WaterY + 3f, 0);
            mg.GetComponent<MeshRenderer>().sharedMaterials = new[] { mats[0], mats[1], mats[2], mats[3], mats[0], mats[1], mats[2], mats[3] };

            // 湖
            var lake = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Object.DestroyImmediate(lake.GetComponent<Collider>());
            lake.name = "Lake"; lake.transform.SetParent(root, false);
            lake.transform.localPosition = new Vector3(0, WaterY, 0f);
            lake.transform.localScale = new Vector3(200f, 1f, 200f);             // 2000 × 2000 m，四周都有水
            var lr = lake.GetComponent<Renderer>(); lr.sharedMaterial = ShaderMat("Village_Lake", "Twilight/Lake");
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;

            // 地形：近处 2 m 网格，远岸 8 m 网格
            var ground = ShaderMat("Village_Ground", "Twilight/Ground");
            ground.SetFloat("_Tile", 7f); ground.SetFloat("_TileNear", 1.8f); ground.SetFloat("_NearDist", 14f);
            ground.SetTexture("_Grass", Tex($"{TKArt.TexDir}/ground_grass.png"));
            ground.SetTexture("_Dirt", Tex($"{TKArt.TexDir}/ground_dirt.png"));
            ground.SetTexture("_Rock", Tex($"{TKArt.TexDir}/ground_rock.png"));
            // 旧的整块地形网格资产移到废纸篓（不直接删）
            foreach (var old in new[] { "TerrainNear", "TerrainFar" })
                if (AssetDatabase.LoadAssetAtPath<Mesh>($"Assets/_Twilight/Art/Generated/{old}.asset")) AssetDatabase.MoveAssetToTrash($"Assets/_Twilight/Art/Generated/{old}.asset");
            AssetDatabase.DeleteAsset("Assets/_Twilight/Art/Generated/Terrain");
            TerrainChunks("Terrain Near", root, ground, -152f, 120f, -120f, 272f, 2f, 64f);
            TerrainChunks("Terrain Far", root, ground, -560f, 600f, -520f, 560f, 8f, 128f, Rect.MinMaxRect(-152f, -120f, 120f, 272f));

            Dock(root);
            RuinWall(root);
            var props = new GameObject("Props").transform;
            props.SetParent(root, false);
            foreach (var it in Items) Spawn(it, props);
            Scatter(props);
            HomeGarden(props);
            Fireflies(root);
            Grass(root);
            // 远岸树线
            var rnd = new System.Random(3);
            for (int i = 0; i < 64; i++)
            {
                float x = -220f + i * 8f + (float)rnd.NextDouble() * 6f;
                float zf = FarShore(x);
                Spawn(new Item("pine_tree", x, zf + 6f + (float)rnd.NextDouble() * 20f, 0.75f + (float)rnd.NextDouble() * 0.45f, (float)rnd.NextDouble() * 360f, false) { world = true }, props);
            }

            // 窗灯材质交给天色控制器（新路灯的玻璃灯罩也在这里：入夜点亮）
            var glow = new List<Material>();
            if (AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/LampPost_Glass.mat") is Material lg) glow.Add(lg);
            foreach (var kv in Sizes)
                if (kv.Value.emissive && AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/P2_{kv.Key}.mat") is Material gm) glow.Add(gm);
            skyCtl.glowMaterials = glow.ToArray();

            SetLayerRecursive(root.gameObject, TKRenderSetup.VillageLayer);
            root.gameObject.AddComponent<RuntimeBatch>();
            // 湖面倒影：黄昏、夜各烘一张
            var lakeMat = lr.sharedMaterial;
            skyCtl.progress = 0f; skyCtl.Apply();
            lakeMat.SetTexture("_ReflCube", BakeReflection(root, "LakeRefl_Dusk"));
            skyCtl.progress = 2f; skyCtl.Apply();
            lakeMat.SetTexture("_ReflCubeNight", BakeReflection(root, "LakeRefl_Night"));
            lakeMat.SetFloat("_ReflStrength", 0.85f);
            skyCtl.progress = 0f; skyCtl.Apply();
            return root;
        }

        /// <summary>静态合批：打包时同材质的网格合并（码头木板、树、房子、地形……）。会动的（风车叶、萤火虫、草叶风吹在着色器里不算）除外。</summary>
        static void MarkStatic(Transform t)
        {
            if (t.GetComponent<Spin>() || t.name == "Fireflies" || t.name == "Sky" || t.name == "Lake") return;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            foreach (Transform c in t) MarkStatic(c);
        }

        public static void SetLayerRecursive(GameObject g, int layer)
        {
            g.layer = layer;
            foreach (Transform c in g.transform) SetLayerRecursive(c.gameObject, layer);
        }
    }
}
