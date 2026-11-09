using System.Collections.Generic;
using UnityEngine;

namespace Twilight
{
    /// <summary>游戏里的字：随游戏打包的开源字体（另有圆体 Round ← Nunito）（SIL OFL，子集化后改名，见 Resources/Fonts/README）——
    /// Kai = 文楷（正文、提示、结算；源自霞鹜文楷）；Brush = 毛笔楷书（标题、判定字、印章；源自马善政）；
    /// Serif / Italic = 衬线体（英文、数字；源自 Cormorant Garamond）。找不到资源时退回系统字体。
    /// 材质是 Twilight/Letter 的运行时副本；动态字体加新字时贴图会重建，Sync() 每帧把新贴图塞回去。</summary>
    public static class TKFont
    {
        static Font kai, brush, serif, italic, round;
        static readonly List<(Material m, Font f)> mats = new();

        static Font Load(ref Font cache, string res, params string[] os)
        {
            if (cache) return cache;
            cache = Resources.Load<Font>("Fonts/" + res);
            if (!cache) cache = Font.CreateDynamicFontFromOSFont(os, 64);
            return cache;
        }

        public static Font Kai => Load(ref kai, "TKKai", "Noto Sans CJK SC", "PingFang SC", "Droid Sans Fallback");
        // 第十三轮（用户：全游戏字体统一，不要有低级错误——马善政的拉丁字母“G”像“6”）：所有字一律霞鹜文楷（中、英、数字都用它）。
        // 旧名字保留成别名，调用处不用改。
        public static Font Brush => Kai;
        public static Font Serif => Kai;
        public static Font Italic => Kai;
        /// <summary>圆体（Nunito，数字、英文、暖光判定字）。</summary>
        public static Font Round => Kai;
        /// <summary>默认字（文楷）。</summary>
        public static Font Font => Kai;

        /// <summary>按模板做一个材质副本（queue &gt; 0 时改渲染队列），记住它用哪款字体（同步贴图用）。</summary>
        public static Material Mat(Material template, int queue = 0, Font f = null)
        {
            f ??= Kai;
            var m = template ? new Material(template) : new Material(f.material);
            if (queue > 0) m.renderQueue = queue;
            mats.Add((m, f));
            Sync();
            return m;
        }

        public static void Sync()
        {
            for (int i = mats.Count - 1; i >= 0; i--)
            {
                var (m, f) = mats[i];
                if (!m || !f) { mats.RemoveAt(i); continue; }
                var t = f.material.mainTexture;
                if (t && m.mainTexture != t) m.mainTexture = t;
            }
        }

        public static TextMesh Text(string name, Transform parent, Material mat, float size, Font f = null, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = f ? f : Kai; tm.fontSize = 64; tm.fontStyle = FontStyle.Normal; tm.characterSize = size;
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.UpperLeft || anchor == TextAnchor.LowerLeft ? TextAlignment.Left
                         : anchor == TextAnchor.MiddleRight || anchor == TextAnchor.UpperRight || anchor == TextAnchor.LowerRight ? TextAlignment.Right : TextAlignment.Center;
            tm.richText = true; tm.lineSpacing = 1f;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tm;
        }

        public static void Alpha(TextMesh t, Color c, float a) { c.a *= a; t.color = c; }
    }

    /// <summary>一句中英双语（中文用文楷、英文用斜体，分两个文字对象排）。</summary>
    public readonly struct Bi
    {
        public readonly string zh, en;
        public Bi(string zh, string en) { this.zh = zh ?? ""; this.en = en ?? ""; }
        public bool Empty => string.IsNullOrEmpty(zh) && string.IsNullOrEmpty(en);
        /// <summary>按当前界面语言取一句。</summary>
        public string Text => TKLang.En ? en : zh;
        public static readonly Bi None = new("", "");
        public bool Same(Bi o) => zh == o.zh && en == o.en;
    }

    /// <summary>阻尼弹簧（数字 / 物件弹跳用）：value 被 Kick 推开后弹回 1。</summary>
    public struct Spring
    {
        public float value, vel;
        public void Kick(float amount) { vel += amount; }
        public void Step(float dt, float k = 320f, float damp = 18f)
        {
            if (value == 0f && vel == 0f) value = 1f;
            float steps = Mathf.Ceil(dt / 0.005f), h = dt / Mathf.Max(1f, steps);
            for (int i = 0; i < steps; i++) { vel += ((1f - value) * k - vel * damp) * h; value += vel * h; }
        }
    }

}
