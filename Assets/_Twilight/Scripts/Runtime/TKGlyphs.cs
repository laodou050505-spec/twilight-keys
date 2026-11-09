using System.Collections.Generic;
using UnityEngine;

namespace Twilight
{
    /// <summary>字形点云（编辑器烘焙，Resources/TKGlyphs）：每个字在字形里均匀撒的点（以字高 = 1 归一化，原点在基线左端），
    /// 萤火虫 / 花瓣“写字”用。</summary>
    public class TKGlyphs : ScriptableObject
    {
        [System.Serializable]
        public class Glyph { public string ch; public float advance; public Vector2[] points; }
        public List<Glyph> glyphs = new();

        static TKGlyphs instance;
        public static TKGlyphs Instance => instance ? instance : instance = Resources.Load<TKGlyphs>("TKGlyphs");

        public Glyph Find(string ch) { foreach (var g in glyphs) if (g.ch == ch) return g; return null; }

        /// <summary>一串字 → 点（字高 height 米，按 anchor 对齐：0 左 / 0.5 中 / 1 右；y 居中）。spacing = 字间距（字高的倍数）。</summary>
        public List<Vector3> Layout(string text, float height, float anchor = 0.5f, float spacing = 0.06f)
        {
            var pts = new List<Vector3>();
            float x = 0f;
            foreach (char c in text)
            {
                var g = Find(c.ToString());
                if (g == null) { x += 0.4f + spacing; continue; }
                foreach (var p in g.points) pts.Add(new Vector3((x + p.x) * height, (p.y - 0.5f) * height, 0));
                x += g.advance + spacing;
            }
            float w = (x - spacing) * height;
            for (int i = 0; i < pts.Count; i++) pts[i] -= new Vector3(w * anchor, 0, 0);
            return pts;
        }
    }
}
