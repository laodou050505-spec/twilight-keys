using System.Collections.Generic;
using UnityEngine;

namespace Twilight
{
    /// <summary>光点写字（萤火虫 / 花瓣）：一堆光点合成一个网格（一次绘制），按字形点云（TKGlyphs）排成文字。
    /// - 换字：光点按从左到右的顺序就近分到新字形上，带一点先后延迟飞过去——数字“重新长出来”；
    /// - 到位后像萤火虫一样轻轻悬停、闪烁（花瓣则微微摆动）；
    /// - 用不上的光点：萤火虫在周围慢慢游荡，花瓣绕着文字转圈（连击外面那一圈花瓣）；
    /// - Scatter：全部散开往下飘落（断连击），一会儿后重新聚起来。
    /// 坐标在 root 的本地空间（root 朝向玩家，x 右 y 上）。</summary>
    public class DotText
    {
        public readonly Transform root;
        public float height = 0.05f, anchor = 0.5f, spacing = 0.06f;
        /// <summary>整体可见度（淡入淡出）。</summary>
        public float visible = 1f;
        /// <summary>游荡 / 绕圈的范围（本地，米）。</summary>
        public Vector2 roam = new(0.12f, 0.05f);
        public bool petals;
        /// <summary>空闲时还露在外面游荡 / 绕圈的光点数（其余隐藏在原地，避免满屏乱飞）。</summary>
        public int idle = 24;

        struct Dot { public Vector3 pos, vel, target; public bool has, falling; public float phase, size, rot, spin, delay, fall; public Color col; }
        readonly Dot[] dots;
        readonly Mesh mesh;
        readonly Vector3[] verts; readonly Vector2[] uv2; readonly Color[] cols;
        readonly Color colA, colB;
        string text;
        float punch, time;
        readonly System.Random rng;

        public DotText(string name, Transform parent, Material mat, int max, bool petals, Color a, Color b, float sizeMin, float sizeMax, int seed = 1)
        {
            this.petals = petals; colA = a; colB = b; rng = new System.Random(seed);
            root = new GameObject(name).transform; root.SetParent(parent, false);
            dots = new Dot[max];
            verts = new Vector3[max * 4]; uv2 = new Vector2[max * 4]; cols = new Color[max * 4];
            var uv = new Vector2[max * 4]; var tri = new int[max * 6];
            for (int i = 0; i < max; i++)
            {
                uv[i * 4] = new Vector2(0, 0); uv[i * 4 + 1] = new Vector2(1, 0); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(0, 1);
                int v = i * 4, t = i * 6;
                tri[t] = v; tri[t + 1] = v + 2; tri[t + 2] = v + 1; tri[t + 3] = v; tri[t + 4] = v + 3; tri[t + 5] = v + 2;
                ref var d = ref dots[i];
                d.phase = R() * 100f; d.size = Mathf.Lerp(sizeMin, sizeMax, R()); d.rot = R() * 6.28f; d.spin = (R() - 0.5f) * (petals ? 3f : 0f);
                d.col = Color.Lerp(a, b, R());
                d.pos = new Vector3((R() - 0.5f) * roam.x * 2, (R() - 0.5f) * roam.y * 2, (R() - 0.5f) * 0.02f);
            }
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            mesh.vertices = verts; mesh.uv = uv; mesh.uv2 = uv2; mesh.colors = cols; mesh.triangles = tri;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            var go = TKMesh.Make(name + "Mesh", root, mesh, mat);
            go.GetComponent<MeshRenderer>().sortingOrder = 0;
        }

        float R() => (float)rng.NextDouble();

        public string Text => text;

        /// <summary>一组光点 = 一个字符（换字时只有变了的字符重新聚形，没变的不动）。</summary>
        class Slot { public string ch; public float x; public readonly List<int> dots = new(); }
        readonly List<Slot> slots = new();

        /// <summary>数字等宽（位数变了、某一位变了，其余位置都不挪）。</summary>
        const float DigitAdvance = 0.62f;

        /// <summary>写字（不变就什么都不做）：逐字符比对，只重排变了的字符。</summary>
        public void SetText(string s)
        {
            if (s == text) return;
            text = s ?? "";
            var g = TKGlyphs.Instance;
            if (!g) return;
            // 排版：数字等宽，其它按字形宽度
            var items = new List<(string ch, float x, TKGlyphs.Glyph glyph, float adv)>();
            float x = 0f;
            foreach (char c in text)
            {
                var gl = g.Find(c.ToString());
                bool digit = c >= '0' && c <= '9';
                float adv = gl == null ? 0.4f : digit ? DigitAdvance : gl.advance;
                items.Add((c.ToString(), x, gl, adv));
                x += adv + spacing;
            }
            float w = Mathf.Max(0f, x - spacing);
            // 匹配：同一个字符、同一个位置（米）的组原样保留
            var keep = new List<Slot>();
            var newSlots = new List<Slot>();
            var old = new List<Slot>(slots);
            foreach (var it in items)
            {
                float xm = (it.x - w * anchor) * height;
                Slot match = null;
                foreach (var o in old) if (o.ch == it.ch && Mathf.Abs(o.x - xm) < 1e-4f) { match = o; break; }
                if (match != null) { old.Remove(match); newSlots.Add(match); continue; }
                var ns = new Slot { ch = it.ch, x = xm };
                newSlots.Add(ns);
                keep.Add(ns);
            }
            // 没用上的旧组：光点放回空闲
            foreach (var o in old) foreach (int i in o.dots) dots[i].has = false;
            // 新组：从空闲光点里分配，带一点先后延迟飞过去
            int free = 0;
            foreach (var ns in keep)
            {
                var it = items[newSlots.IndexOf(ns)];
                if (it.glyph == null) continue;
                float cx = it.adv > 0f && (it.ch[0] >= '0' && it.ch[0] <= '9') ? (DigitAdvance - it.glyph.advance) * 0.5f : 0f;   // 等宽格里居中
                foreach (var p in it.glyph.points)
                {
                    while (free < dots.Length && (dots[free].has || dots[free].falling)) free++;
                    if (free >= dots.Length) break;
                    ref var d = ref dots[free];
                    d.target = new Vector3(ns.x + (p.x + cx) * height, (p.y - 0.5f) * height, 0);
                    d.has = true; d.delay = R() * 0.08f;
                    ns.dots.Add(free);
                }
            }
            slots.Clear(); slots.AddRange(newSlots);
            if (keep.Count > 0) punch = Mathf.Max(punch, 0.5f);
        }

        /// <summary>直接给目标点（本地坐标，不分字符；进度条等用）。</summary>
        public void SetPoints(List<Vector3> targets)
        {
            slots.Clear(); text = null;
            targets.Sort((p, q) => p.x.CompareTo(q.x));
            var order = new List<int>();
            for (int i = 0; i < dots.Length; i++) if (dots[i].has && !dots[i].falling) order.Add(i);
            order.Sort((p, q) => dots[p].pos.x.CompareTo(dots[q].pos.x));
            for (int i = 0; i < dots.Length; i++) if (!dots[i].has && !dots[i].falling) order.Add(i);
            for (int k = 0; k < order.Count; k++)
            {
                ref var d = ref dots[order[k]];
                if (k < targets.Count) { d.target = targets[k]; d.has = true; d.delay = R() * 0.1f; }
                else d.has = false;
            }
            punch = 1f;
        }

        /// <summary>全部散开往下飘落（断连击），fall 秒后重新聚起来。</summary>
        public void Scatter(float fall = 1.2f)
        {
            for (int i = 0; i < dots.Length; i++)
            {
                ref var d = ref dots[i];
                if (!d.has) continue;
                d.has = false; d.falling = true; d.fall = fall * (0.7f + 0.6f * R());
                d.vel = new Vector3((R() - 0.5f) * 0.25f, R() * 0.12f, (R() - 0.5f) * 0.1f);
            }
            text = null; slots.Clear();
        }

        public void Kick(float s = 1f) { punch = Mathf.Max(punch, s); }

        public void Update(float dt)
        {
            time += dt;
            punch = Mathf.MoveTowards(punch, 0f, dt * 2.5f);
            int idleSeen = 0;
            for (int i = 0; i < dots.Length; i++)
            {
                ref var d = ref dots[i];
                float a;
                if (d.falling)
                {
                    d.vel += new Vector3(Mathf.Sin(time * 2f + d.phase) * 0.08f, -0.35f, 0) * dt;
                    d.vel *= 1f - dt * 1.2f;
                    d.pos += d.vel * dt;
                    d.fall -= dt;
                    a = Mathf.Clamp01(d.fall / 0.5f);
                    if (d.fall <= 0f) { d.falling = false; d.pos = Roam(i) + new Vector3(0, 0, 0.02f); }
                }
                else if (d.has)
                {
                    if (d.delay > 0f) { d.delay -= dt; }
                    else
                    {
                        // 悬停：萤火虫轻轻晃，花瓣微微飘
                        var hover = new Vector3(Mathf.Sin(time * 1.7f + d.phase), Mathf.Sin(time * 2.3f + d.phase * 1.3f), 0) * (petals ? 0.0007f : 0.0008f);
                        var to = d.target * (1f + punch * 0.08f) + hover;
                        d.vel += (to - d.pos) * (petals ? 70f : 90f) * dt;
                        d.vel *= 1f - Mathf.Min(1f, dt * 11f);
                        d.pos += d.vel * dt;
                    }
                    a = 1f;
                }
                else
                {
                    // 游荡 / 绕圈（只露 idle 个，其余隐藏）
                    if (idleSeen++ >= idle) { a = 0f; d.pos = Roam(i); goto write; }
                    var to = Roam(i);
                    d.vel += (to - d.pos) * 6f * dt;
                    d.vel *= 1f - Mathf.Min(1f, dt * 3f);
                    d.pos += d.vel * dt;
                    a = petals ? 0.85f : 0.45f;
                }
                write:
                d.rot += d.spin * dt;
                float flick = petals ? 1f : 0.7f + 0.3f * Mathf.Sin(time * (3f + (d.phase % 3f)) + d.phase);
                var c = d.col; c.a *= a * flick * visible;
                float size = d.size * (d.has ? 1f + punch * 0.25f : 0.85f);
                int v = i * 4;
                for (int k = 0; k < 4; k++) { verts[v + k] = d.pos; uv2[v + k] = new Vector2(size, d.rot); cols[v + k] = c; }
            }
            mesh.vertices = verts; mesh.uv2 = uv2; mesh.colors = cols;
        }

        /// <summary>空闲光点的位置：萤火虫在一个椭圆区域里慢慢游荡；花瓣在文字外面绕一圈。</summary>
        Vector3 Roam(int i)
        {
            float ph = dots[i].phase;
            if (petals)
            {
                float ang = time * 0.9f + ph;
                return new Vector3(Mathf.Cos(ang) * roam.x, Mathf.Sin(ang) * roam.y * 0.8f + Mathf.Sin(ang * 3f + ph) * 0.004f, Mathf.Sin(ang) * 0.01f);
            }
            return new Vector3(Mathf.Sin(time * 0.31f + ph) * roam.x, Mathf.Sin(time * 0.43f + ph * 1.7f) * roam.y, Mathf.Sin(time * 0.27f + ph * 0.6f) * 0.03f);
        }
    }

    /// <summary>界面用的小网格工具。</summary>
    public static class TKMesh
    {
        public static GameObject Make(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static GameObject Quad(string name, Transform parent, Material mat, Vector3 pos, Vector2 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = new Vector3(size.x, size.y, 1);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
