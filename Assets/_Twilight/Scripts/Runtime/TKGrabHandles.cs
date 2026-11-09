using System.Collections.Generic;
using UnityEngine;

namespace Twilight
{
    /// <summary>第三十四轮（用户：双手调桌子的位置可以有两个钮，让玩家捏住钮来调）：
    /// 琴左右两端各浮一颗抓手圆钮（金边 + 象牙底 + 金色四向箭头，和曲目面板的移动钮同一套），朝玩家斜 35°。
    /// 只在能调位置的时候出现（菜单 / 暂停 / 结算 / 教学）；手靠近时放大，捏住时按下去。拖动本身在 TKSession.PianoGrab 里。</summary>
    public class TKGrabHandles : MonoBehaviour
    {
        public readonly Transform[] knobs = new Transform[2];
        readonly Transform[] pads = new Transform[2];
        float show;
        readonly float[] hover = new float[2];
        /// <summary>该不该出现（会话每帧给）。</summary>
        public bool Shown { get; set; }
        /// <summary>正在被双手捏着拖。</summary>
        public bool Held { get; set; }
        /// <summary>新手教学“摆琴”那一步：两颗钮一下一下地鼓起来招手。</summary>
        public bool Beckon { get; set; }
        /// <summary>手（捏合点）离第 i 颗钮多近：0 远 … 1 就在上面（会话每帧给）。</summary>
        public void SetHover(int i, float v) => hover[i] = Mathf.MoveTowards(hover[i], v, Time.deltaTime * 6f);
        public bool Visible => show > 0.5f;
        public Vector3 Pos(int i) => knobs[i] ? knobs[i].position : Vector3.zero;
        const float SideGap = 0.12f;

        public static TKGrabHandles Create(Transform world, Transform keys, Material gold, Material ivory)
        {
            var h = world.gameObject.AddComponent<TKGrabHandles>();
            // 琴（PianoKeys 下所有网格）在世界本地坐标里的范围：钮放在左右两端外侧、琴面上方 1.5 cm、前后居中
            float minX = 9f, maxX = -9f, top = -9f, minZ = 9f, maxZ = -9f;
            foreach (var mf in keys.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var b = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    var l = world.InverseTransformPoint(mf.transform.TransformPoint(c));
                    minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x); top = Mathf.Max(top, l.y);
                    minZ = Mathf.Min(minZ, l.z); maxZ = Mathf.Max(maxZ, l.z);
                }
            }
            // 第三十五轮（用户：位置要靠近桌子边）：钮贴着虚拟桌（= 对齐后的真实桌边）靠人那一侧的边沿，往里 2 cm、高出桌面 1.2 cm
            float edgeZ = minZ - 0.035f, padY = top + 0.015f;
            var vt = world.Find("VirtualTable");
            if (vt)
            {
                float vMin = 9f, vTop = -9f;
                foreach (var vmf in vt.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!vmf.sharedMesh) continue;
                    var vb = vmf.sharedMesh.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        var vc = vb.center + Vector3.Scale(vb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                        var vl = world.InverseTransformPoint(vmf.transform.TransformPoint(vc));
                        vMin = Mathf.Min(vMin, vl.z); vTop = Mathf.Max(vTop, vl.y);
                    }
                }
                if (vMin < minZ) { edgeZ = vMin + 0.02f; padY = vTop + 0.012f; }
            }
            var arrows = ArrowMesh();
            for (int i = 0; i < 2; i++)
            {
                var root = new GameObject(i == 0 ? "GrabKnobL" : "GrabKnobR").transform;
                root.SetParent(world, false);
                root.localPosition = new Vector3(i == 0 ? minX - SideGap : maxX + SideGap, padY, edgeZ);   // 第三十六轮（用户：再往两边挪一点）：离琴两端 4.5 → 12 cm
                root.localRotation = Quaternion.Euler(-35f, 0f, 0f);       // 圆钮正面（本地 +Y）朝上、往玩家那边斜 35°
                Cyl(root, "Rim", gold, new Vector3(0.042f, 0.0035f, 0.042f), 0f);
                var pad = new GameObject("Pad").transform; pad.SetParent(root, false);
                Cyl(pad, "Face", ivory, new Vector3(0.0365f, 0.002f, 0.0365f), 0.0022f);
                var icon = new GameObject("Arrows"); icon.transform.SetParent(pad, false);
                icon.transform.localPosition = new Vector3(0f, 0.0044f, 0f);
                icon.AddComponent<MeshFilter>().sharedMesh = arrows;
                var r = icon.AddComponent<MeshRenderer>(); r.sharedMaterial = gold; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                h.knobs[i] = root; h.pads[i] = pad;
                root.localScale = Vector3.one * 0.001f;
                root.gameObject.SetActive(false);
            }
            return h;
        }

        static void Cyl(Transform parent, string n, Material m, Vector3 scale, float y)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(c.GetComponent<Collider>());
            c.name = n; c.transform.SetParent(parent, false);
            c.transform.localPosition = new Vector3(0f, y, 0f);
            c.transform.localScale = scale;
            var r = c.GetComponent<MeshRenderer>(); if (m) r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>四向箭头（十字 + 四个三角），平躺在 XZ 平面、只朝上（+Y）一面：每个三角按叉乘自动摆正绕向。
        /// （正反两面共用顶点的话，重算法线会互相抵消成零，金属 matcap 查出来是黑的。）</summary>
        static Mesh ArrowMesh()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            void Tri(Vector2 a, Vector2 b, Vector2 c)
            {
                Vector3 A = new(a.x, 0, a.y), B = new(b.x, 0, b.y), C = new(c.x, 0, c.y);
                if (Vector3.Cross(B - A, C - A).y < 0f) (B, C) = (C, B);      // 法线朝上
                int k = v.Count; v.Add(A); v.Add(B); v.Add(C);
                t.AddRange(new[] { k, k + 1, k + 2 });
            }
            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d) { Tri(a, b, c); Tri(a, c, d); }
            const float L = 0.0125f, w = 0.0017f, hw = 0.0048f, hl = 0.0055f;
            Quad(new(-L + hl, -w), new(L - hl, -w), new(L - hl, w), new(-L + hl, w));
            Quad(new(-w, -L + hl), new(w, -L + hl), new(w, L - hl), new(-w, L - hl));
            Tri(new(L, 0), new(L - hl, hw), new(L - hl, -hw));
            Tri(new(-L, 0), new(-L + hl, -hw), new(-L + hl, hw));
            Tri(new(0, L), new(-hw, L - hl), new(hw, L - hl));
            Tri(new(0, -L), new(hw, -L + hl), new(-hw, -L + hl));
            var m = new Mesh { name = "GrabArrows" };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            show = Mathf.MoveTowards(show, Shown ? 1f : 0f, dt * (Shown ? 2.5f : 4f));
            float e = show * show * (3f - 2f * show);
            for (int i = 0; i < 2; i++)
            {
                if (!knobs[i]) continue;
                knobs[i].gameObject.SetActive(show > 0.001f);
                float hv = Held ? 1f : Mathf.Max(hover[i], Beckon ? 0.5f + 0.5f * Mathf.Sin(Time.time * 4.5f) : 0f);
                knobs[i].localScale = Vector3.one * Mathf.Max(0.001f, e * (1f + 0.18f * hv));
                if (pads[i]) pads[i].localPosition = new Vector3(0f, Held ? -0.0016f : 0.0008f * hv, 0f);   // 手靠近微微浮起，捏住按下去
            }
        }
    }
}
