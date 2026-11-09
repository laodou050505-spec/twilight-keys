using System.Collections.Generic;
using UnityEngine;

namespace Twilight
{
    /// <summary>光河：从窗口流到琴键前沿的四条轨道。四条轨道始终平行（玩法要求），竖直方向起伏：
    /// 琴键上一段贴着键面压平（判定区）→ 越过琴盖 → 缓缓升到窗洞下部。
    /// 挂在 Stage 上（本地原点 = 琴键前沿中点、桌面高度；+Z 指向窗户）。窗户距离变了调 Rebuild()。
    /// 第三十六轮（用户：挪桌子时整个场景跟着动很别扭）：琴台和场景拆开后，窗户不一定在琴的正前方——
    /// 离琴键 bendStart 以内保持笔直（判定区、琴盖坡度都在这一段，手感不变），往后用五次缓动平滑地弯向窗户的左右位置。
    /// 横向（轨道排开的方向）逐点取“水平切线 × 竖直”，不再写死成本地 X。
    /// 玩法用 Point(s, lane) 取音块位置：s = 离琴键前沿的弧长（米）。</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class LightRiver : MonoBehaviour
    {
        public const int Lanes = 4;

        [Tooltip("窗户（窗洞中心）；没有就用 fallbackDistance")]
        public Transform window;
        public float windowHalfHeight = 0.45f;
        public float fallbackDistance = 2.3f;
        [Header("琴（Stage 本地，米）")]
        public float widthNear = 0.329f;      // = 两个八度键盘宽
        public float widthFar = 0.25f;
        public float keyTop = 0.045f;         // 黑键顶面 0.040 + 余量
        public float flatLen = 0.11f;         // 琴键上压平的一段（判定区）
        [Tooltip("坡从这里开始往上抬（比判定区末端早一点，坡更缓）")] public float rampStart = 0.09f;
        public float lidZ = 0.16f, lidTop = 0.072f;
        [Header("窗口端")]
        [Tooltip("光河在窗洞里的起点高度：窗洞底边往上多少")]
        public float sourceAboveSill = 0.14f;
        public int samples = 180;
        [Tooltip("离琴键多远之后才开始弯向窗户（米）")] public float bendStart = 0.5f;
        [Tooltip("最多弯多少度（超出就由会话把场景搬到琴的正前方）")] public float maxBendDeg = 35f;
        [Header("黄铜分隔线")]
        public Material brass;
        public float lineWidth = 0.0024f;
        [Header("光晕 / 流光 / 桌面光影")]
        public Material haloMaterial;
        public Material motesMaterial;
        public Material tableMaterial;
        public int motes = 180;
        [Tooltip("桌子远端（Stage 本地 z）：桌面光影铺到这里")]
        public float tableFarZ = 0.72f;
        [Tooltip("桌面比 Stage 原点高多少（琴下沉了）")]
        public float tableTop = 0.02f;

        [Header("天上那段（银河）：从窗口接着往村子的天空升上去")]
        public Material skyMaterial;
        [Tooltip("相对窗口端的控制点（本地，米）")] public Vector3 skyMid = new(-9f, 24f, 48f), skyTop = new(22f, 90f, 125f);
        public float skyWidthTop = 60f;
        [Range(0, 1), Tooltip("开场“从天上飘下来”：1 = 全藏，0 = 全显（全局 _TK_RiverHide）")] public float hide;

        readonly List<Vector3> path = new();   // 本地中心线
        readonly List<Vector3> sides = new();  // 每个采样点的横向（单位向量，水平）
        readonly List<float> arc = new();
        public float Length { get; private set; }

        Mesh mesh, lineMesh;
        Transform lines;

        void OnEnable() => Rebuild();
        void OnValidate() { if (isActiveAndEnabled) Rebuild(); }

        float Distance()
        {
            if (!window) return fallbackDistance;
            return Mathf.Max(0.8f, transform.InverseTransformPoint(window.position).z - 0.02f);
        }

        /// <summary>窗户在琴台本地的左右偏移（限制在 maxBendDeg 以内）。</summary>
        float BendX(float D)
        {
            if (!window) return 0f;
            float wx = transform.InverseTransformPoint(window.position).x;
            float lim = Mathf.Tan(maxBendDeg * Mathf.Deg2Rad) * Mathf.Max(0.1f, D - bendStart);
            return Mathf.Clamp(wx, -lim, lim);
        }

        /// <summary>窗户相对琴台是否在光河能弯过去的范围里（会话据此决定要不要把场景搬到琴的正前方）。</summary>
        public bool WindowReachable(out float angleDeg)
        {
            angleDeg = 0f;
            if (!window) return true;
            var w = transform.InverseTransformPoint(window.position);
            angleDeg = Mathf.Atan2(w.x, Mathf.Max(0.01f, w.z - bendStart)) * Mathf.Rad2Deg;
            return w.z > 1.0f && w.z < 4.5f && Mathf.Abs(angleDeg) <= maxBendDeg && w.y > 0.1f && w.y < 1.4f;
        }

        float SourceHeight()
        {
            if (!window) return 0.2f;
            return transform.InverseTransformPoint(window.position).y - windowHalfHeight + sourceAboveSill;
        }

        /// <summary>中心线高度（本地 z → y）。</summary>
        float ProfileY(float z, float D, float hw)
        {
            // 琴键上贴着键面（判定区）→ 用一段很长的平滑坡（五次缓动，起点和终点坡度都是 0）越过琴盖 → 再缓缓升到窗洞。
            // 之前坡只有 5 cm、最陡近 40°，像在琴盖上折了一下；现在坡从 9 cm 拉到 24 cm，最陡约 20°。
            float rampEnd = lidZ + 0.08f, top = lidTop + 0.004f;
            if (z <= rampStart) return keyTop;
            if (z <= rampEnd) return Mathf.Lerp(keyTop, top, Smoother(Mathf.InverseLerp(rampStart, rampEnd, z)));
            float t = Mathf.InverseLerp(rampEnd, D, z);
            return Mathf.Lerp(top, hw, t * t * (0.6f + 0.4f * t));        // 起步坡度 0，越靠近窗越往上翘
        }

        static float Smooth(float t) => t * t * (3 - 2 * t);
        static float Smoother(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        public float WidthAt(float s) => Mathf.Lerp(widthNear, widthFar, Smooth(Mathf.Clamp01(s / Mathf.Max(Length, 1e-3f))));

        public void Rebuild()
        {
            float D = Distance(), hw = SourceHeight(), bx = BendX(D);
            path.Clear(); arc.Clear(); sides.Clear();
            // 前沿往外多出 1 cm（淡出用），采样在判定区更密
            for (int i = 0; i <= samples; i++)
            {
                float u = (float)i / samples;
                float z = -0.01f + (D + 0.01f) * Mathf.Pow(u, 1.6f);
                float x = z <= bendStart ? 0f : bx * Smoother(Mathf.InverseLerp(bendStart, D, z));
                path.Add(new Vector3(x, ProfileY(z, D, hw), z));
            }
            for (int i = 0; i < path.Count; i++)
            {
                var d = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]; d.y = 0f;
                sides.Add(d.sqrMagnitude > 1e-10f ? Vector3.Cross(Vector3.up, d.normalized) : Vector3.right);
            }
            float acc = 0;
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) acc += Vector3.Distance(path[i - 1], path[i]);
                arc.Add(acc);
            }
            // 弧长从琴键前沿（z = 0）算起
            float zeroArc = ArcAtZ(0f);
            for (int i = 0; i < arc.Count; i++) arc[i] -= zeroArc;
            Length = arc[^1];
            BuildRibbon();
            BuildLines();
            BuildHalo();
            BuildMotes();
            BuildTableGlow();
            BuildSky();
        }

        static readonly int IdHide = Shader.PropertyToID("_TK_RiverHide");
        void Update() => Shader.SetGlobalFloat(IdHide, hide);
        void OnDisable() => Shader.SetGlobalFloat(IdHide, 0f);

        /// <summary>天上那段：窗口端顺着切线出去，再沿三次贝塞尔升到夜空，越往上越宽（Village 层：只在窗洞 / 已铺开处看得见）。</summary>
        void BuildSky()
        {
            var c = Child("SkyRiver");
            int village = LayerMask.NameToLayer("Village");
            if (village >= 0) c.gameObject.layer = village;
            var r = c.GetComponent<MeshRenderer>();
            r.sharedMaterial = skyMaterial; r.enabled = skyMaterial;
            var m = Dyn(c, "SkyRiver");
            if (!skyMaterial || path.Count < 2) return;
            var p0 = path[^1];
            var d0 = (path[^1] - path[^2]).normalized;
            var skySide = sides[^1];
            Vector3 p1 = p0 + d0 * 6f + Vector3.up * 0.8f, p2 = p0 + skyMid, p3 = p0 + skyTop;
            const int n = 160, cols = 16;
            var pts = new List<Vector3>(n + 1); var len = new List<float>(n + 1);
            float acc = 0;
            for (int i = 0; i <= n; i++)
            {
                float t = Mathf.Pow((float)i / n, 1.5f);                     // 窗口附近采样密一点
                float u = 1 - t;
                var p = u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
                if (i > 0) acc += Vector3.Distance(pts[^1], p);
                pts.Add(p); len.Add(acc);
            }
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float t = len[i] / acc;
                float w = Mathf.Lerp(widthFar, skyWidthTop, Mathf.Pow(t, 1.15f));
                for (int k = 0; k <= cols; k++)
                {
                    float x = (float)k / cols;
                    v.Add(pts[i] + skySide * (x - 0.5f) * w);
                    uv.Add(new Vector2(x, t)); uv2.Add(new Vector2(len[i], acc));
                }
            }
            int stride = cols + 1;
            for (int i = 0; i < n; i++)
                for (int k = 0; k < cols; k++)
                {
                    int a = i * stride + k, b = a + 1, d = a + stride, e = d + 1;
                    tri.AddRange(new[] { a, d, b, b, d, e });
                }
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(1, uv2); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
        }

        Transform Child(string name)
        {
            var c = transform.Find(name);
            if (!c)
            {
                c = new GameObject(name).transform;
                c.SetParent(transform, false);
                c.gameObject.AddComponent<MeshFilter>(); var r = c.gameObject.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
            return c;
        }

        static Mesh Dyn(Transform c, string name)
        {
            var mf = c.GetComponent<MeshFilter>();
            var m = mf.sharedMesh;
            if (!m || m.name != name) { m = new Mesh { name = name, hideFlags = HideFlags.DontSave }; mf.sharedMesh = m; }
            m.Clear();
            return m;
        }

        /// <summary>光晕：河面下 3 mm、宽 1.7 倍的淡光带（模拟泛光）。</summary>
        void BuildHalo()
        {
            if (!haloMaterial) return;
            var c = Child("Halo");
            c.GetComponent<MeshRenderer>().sharedMaterial = haloMaterial;
            var m = Dyn(c, "RiverHalo");
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tri = new List<int>();
            const int cols = 8;
            for (int i = 0; i < path.Count; i += 2)
            {
                float s = arc[i], w = WidthAt(Mathf.Max(0, s)) * 1.7f;
                for (int k = 0; k <= cols; k++)
                {
                    float x = (float)k / cols;
                    v.Add(path[i] + sides[i] * (x - 0.5f) * w - Vector3.up * 0.003f);
                    uv.Add(new Vector2(x, s)); uv2.Add(new Vector2(Length, 0));
                }
            }
            int rows = v.Count / (cols + 1);
            for (int r = 0; r < rows - 1; r++)
                for (int k = 0; k < cols; k++)
                {
                    int a = r * (cols + 1) + k, b = a + cols + 1;
                    tri.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(1, uv2); m.SetTriangles(tri, 0); m.RecalculateBounds();
        }

        Texture2D pathTex;

        /// <summary>流光：路径烘进 128×1 贴图（本地中心线 + 宽度），光点在着色器里沿路径流动。</summary>
        void BuildMotes()
        {
            if (!motesMaterial) return;
            if (!pathTex) pathTex = new Texture2D(128, 1, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < 128; i++)
            {
                float s = Mathf.Lerp(0f, Length, i / 127f);
                Sample(s, out var p, out _);
                pathTex.SetPixel(i, 0, new Color(p.x, p.y, p.z, WidthAt(s)));
            }
            pathTex.Apply();
            var c = Child("Motes");
            var r = c.GetComponent<MeshRenderer>(); r.sharedMaterial = motesMaterial;
            var mpb = new MaterialPropertyBlock();
            mpb.SetTexture("_Path", pathTex); mpb.SetFloat("_Len", Length);
            r.SetPropertyBlock(mpb);
            var m = Dyn(c, "RiverMotes");
            var rnd = new System.Random(9);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i < motes; i++)
            {
                float seed = (float)rnd.NextDouble(), lat = (float)rnd.NextDouble() - 0.5f;
                int b = v.Count;
                foreach (var q in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) }) { v.Add(Vector3.zero); uv.Add(q); uv2.Add(new Vector2(seed, lat)); }
                tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(1, uv2); m.SetTriangles(tri, 0);
            m.bounds = new Bounds(new Vector3(0, 0.2f, Length / 2f), new Vector3(1f, 1f, Length + 1f));   // 位置在着色器里算
        }

        /// <summary>桌面光影：光河投在木桌上的彩色焦散，从琴盖后面铺到桌子远端（沿光河中心线，跟着弯）。</summary>
        void BuildTableGlow()
        {
            if (!tableMaterial) return;
            var c = Child("TableGlow");
            c.GetComponent<MeshRenderer>().sharedMaterial = tableMaterial;
            var m = Dyn(c, "TableGlow");
            float z0 = lidZ + 0.06f, z1 = Mathf.Min(tableFarZ, Distance()), w = widthNear * 1.35f;
            if (z1 <= z0) return;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            const int rows = 12;
            for (int r = 0; r <= rows; r++)
            {
                float z = Mathf.Lerp(z0, z1, r / (float)rows);
                int k = 1; while (k < path.Count - 1 && path[k].z < z) k++;
                float t = Mathf.InverseLerp(path[k - 1].z, path[k].z, z);
                var cp = Vector3.Lerp(path[k - 1], path[k], t); cp.y = tableTop + 0.0015f;
                var sd = Vector3.Slerp(sides[k - 1], sides[k], t).normalized;
                v.Add(cp - sd * w / 2); v.Add(cp + sd * w / 2);
                float vv = r / (float)rows; uv.Add(new Vector2(0, vv)); uv.Add(new Vector2(1, vv));
                if (r > 0) { int a = (r - 1) * 2; tri.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
        }

        float ArcAtZ(float z)
        {
            for (int i = 1; i < path.Count; i++)
                if (path[i].z >= z) return Mathf.Lerp(arc[i - 1], arc[i], Mathf.InverseLerp(path[i - 1].z, path[i].z, z));
            return arc[^1];
        }

        /// <summary>本地中心线上弧长 s 处的点和切线。</summary>
        void Sample(float s, out Vector3 p, out Vector3 tan) => Sample(s, out p, out tan, out _);

        void Sample(float s, out Vector3 p, out Vector3 tan, out Vector3 side)
        {
            int i = 1;
            while (i < arc.Count - 1 && arc[i] < s) i++;
            float t = Mathf.InverseLerp(arc[i - 1], arc[i], s);
            p = Vector3.LerpUnclamped(path[i - 1], path[i], t);
            tan = (path[i] - path[i - 1]).normalized;
            side = Vector3.Slerp(sides[i - 1], sides[i], Mathf.Clamp01(t)).normalized;
        }

        /// <summary>世界坐标：第 lane 条轨道（0..3，可带小数）中线上、离前沿 s 米处，抬高 lift 米（沿河面法线）。</summary>
        public Vector3 Point(float s, float lane, float lift = 0f)
        {
            Sample(s, out var p, out var tan, out var side);
            float w = WidthAt(s);
            var n = Vector3.Cross(tan, side);   // 河面法线（朝上）
            if (n.y < 0) n = -n;
            p += side * ((lane + 0.5f) / Lanes - 0.5f) * w + n * lift;
            return transform.TransformPoint(p);
        }

        /// <summary>音块朝向：+Z 顺着河往窗口，+Y 河面法线。</summary>
        public Quaternion Rotation(float s)
        {
            Sample(s, out _, out var tan, out var side);
            var n = Vector3.Cross(tan, side); if (n.y < 0) n = -n;
            return transform.rotation * Quaternion.LookRotation(tan, n);
        }

        void BuildRibbon()
        {
            const int cols = Lanes * 6;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i < path.Count; i++)
            {
                float s = arc[i], w = WidthAt(Mathf.Max(0, s));
                for (int c = 0; c <= cols; c++)
                {
                    float x = (float)c / cols;
                    v.Add(path[i] + sides[i] * (x - 0.5f) * w);
                    uv.Add(new Vector2(x, s)); uv2.Add(new Vector2(Length, 0));
                }
            }
            int stride = cols + 1;
            for (int i = 0; i < path.Count - 1; i++)
                for (int c = 0; c < cols; c++)
                {
                    int a = i * stride + c, b = a + 1, d = a + stride, e = d + 1;
                    tri.AddRange(new[] { a, d, b, b, d, e });
                }
            if (!mesh) { mesh = new Mesh { name = "LightRiver" }; mesh.hideFlags = HideFlags.DontSave; }
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetUVs(1, uv2); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        /// <summary>五根黄铜细线（两侧边 + 三条分隔），比河面高 1 mm，带一点厚度。</summary>
        void BuildLines()
        {
            if (!lines)
            {
                lines = transform.Find("BrassLines");
                if (!lines)
                {
                    lines = new GameObject("BrassLines").transform;
                    lines.SetParent(transform, false);
                    lines.gameObject.AddComponent<MeshFilter>(); lines.gameObject.AddComponent<MeshRenderer>();
                }
            }
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            float h = 0.0012f;
            for (int k = 0; k <= Lanes; k++)
            {
                float x01 = (float)k / Lanes;
                int start = v.Count;
                for (int i = 0; i < path.Count; i++)
                {
                    float s = arc[i];
                    if (s < 0) continue;
                    var sd = sides[i];
                    var c = path[i] + sd * (x01 - 0.5f) * WidthAt(s);
                    var tan = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                    var up = Vector3.Cross(tan, sd); if (up.y < 0) up = -up;
                    // 窗口端 40 cm 内渐细到 0（河面在那里淡出）
                    float lw = lineWidth * (k == 0 || k == Lanes ? 1.4f : 1f) * Smooth(Mathf.Clamp01((Length - s) / 0.4f)) * Smooth(Mathf.Clamp01((s - 0.005f) / 0.07f));   // 两端都渐细：琴键前沿不再突然冒出一根铜线
                    // 截面：左下、左上、右上、右下
                    v.Add(c - sd * lw / 2); v.Add(c - sd * lw / 2 + up * h);
                    v.Add(c + sd * lw / 2 + up * h); v.Add(c + sd * lw / 2);
                    n.Add(-sd); n.Add(up); n.Add(up); n.Add(sd);
                }
                int rows = (v.Count - start) / 4;
                for (int r = 0; r < rows - 1; r++)
                {
                    int a = start + r * 4, b = a + 4;
                    // 左侧面、顶面、右侧面
                    tri.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                    tri.AddRange(new[] { a + 1, b + 1, a + 2, a + 2, b + 1, b + 2 });
                    tri.AddRange(new[] { a + 2, b + 2, a + 3, a + 3, b + 2, b + 3 });
                }
            }
            if (!lineMesh) { lineMesh = new Mesh { name = "RiverLines" }; lineMesh.hideFlags = HideFlags.DontSave; }
            lineMesh.Clear();
            lineMesh.SetVertices(v); lineMesh.SetNormals(n); lineMesh.SetTriangles(tri, 0); lineMesh.RecalculateBounds();
            lines.GetComponent<MeshFilter>().sharedMesh = lineMesh;
            var mr = lines.GetComponent<MeshRenderer>();
            mr.sharedMaterial = brass;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
