using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Twilight.EditorTools
{
    /// <summary>界面烘焙：计数器滚轮用的数字条（0..9 竖排一列，第 0 格在最上；白字、alpha = 字形），用打包的衬线体 TK Serif。</summary>
    public static class TKUIBake
    {
        public const string DigitPath = "Assets/_Twilight/Art/Textures/UI/DigitStrip.png";
        public const string GlyphPath = "Assets/_Twilight/Resources/TKGlyphs.asset";

        /// <summary>字形点云：(字, 字体, 每个字大约多少个点)。</summary>
        static readonly (string text, string font, int perGlyph)[] GlyphSets =
        {
            ("0123456789", "TKKai", 80), (",", "TKKai", 9), ("×", "TKKai", 24),
            ("黄昏琴村", "TKKai", 110), ("TwilightKeys", "TKKai", 34),   // 统一字体：全部霞鹜文楷
        };

        [MenuItem("Twilight/界面/烘焙字形点云")]
        public static TKGlyphs Glyphs()
        {
            const int size = 256, layer = 31;
            var asset = ScriptableObject.CreateInstance<TKGlyphs>();
            var letter = new Material(Shader.Find("Twilight/Letter"));
            letter.SetColor("_HaloColor", new Color(0, 0, 0, 0)); letter.SetFloat("_Rough", 0f);
            var camGO = new GameObject("BakeCam"); var cam = camGO.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 0.5f; cam.aspect = 1f;
            cam.transform.position = new Vector3(0, 0, -5); cam.cullingMask = 1 << layer;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.nearClipPlane = 0.1f; cam.farClipPlane = 20f;
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(7);
            foreach (var (text, fontName, per) in GlyphSets)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>($"Assets/_Twilight/Resources/Fonts/{fontName}.ttf");
                foreach (char c in text)
                {
                    var go = new GameObject("G"); go.layer = layer;
                    var tm = go.AddComponent<TextMesh>();
                    tm.font = font; tm.fontSize = 128; tm.characterSize = 0.058f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;   // 行高约 0.74 个画面高
                    tm.text = c.ToString(); tm.color = Color.white;
                    font.RequestCharactersInTexture(tm.text, 128);
                    letter.mainTexture = font.material.mainTexture;
                    go.GetComponent<MeshRenderer>().sharedMaterial = letter;
                    var b = go.GetComponent<MeshRenderer>().bounds;
                    cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, size, size), 0, 0); tex.Apply(); RenderTexture.active = null;
                    Object.DestroyImmediate(go);
                    var px = tex.GetPixels();
                    // 字形范围（像素）
                    int minX = size, maxX = 0;
                    var inside = new List<Vector2Int>();
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (px[y * size + x].r > 0.5f) { inside.Add(new Vector2Int(x, y)); minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
                    var g = new TKGlyphs.Glyph { ch = c.ToString() };
                    // 字高 = 画面高（1 单位 = size 像素，对应 1 个“字高”）；原点：x 从字形左缘、y 从画面底
                    float minDist = Mathf.Sqrt(inside.Count / (float)Mathf.Max(1, per)) * 0.85f;
                    var chosen = new List<Vector2>();
                    for (int tries = 0; tries < per * 60 && chosen.Count < per && inside.Count > 0; tries++)
                    {
                        var p = inside[rng.Next(inside.Count)];
                        var v = new Vector2(p.x + (float)rng.NextDouble(), p.y + (float)rng.NextDouble());
                        bool ok = true;
                        foreach (var q in chosen) if ((q - v).sqrMagnitude < minDist * minDist) { ok = false; break; }
                        if (ok) chosen.Add(v);
                    }
                    var pts = new Vector2[chosen.Count];
                    for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((chosen[i].x - minX) / size, chosen[i].y / size);
                    g.points = pts;
                    g.advance = inside.Count > 0 ? (maxX - minX) / (float)size : 0.3f;
                    asset.glyphs.Add(g);
                }
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(camGO); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(letter);
            AssetDatabase.DeleteAsset(GlyphPath);
            AssetDatabase.CreateAsset(asset, GlyphPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        [MenuItem("Twilight/界面/烘焙数字条")]
        public static Texture2D DigitStrip()
        {
            const int cw = 128, ch = 160, layer = 31;
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Twilight/Resources/Fonts/TKKai.ttf");   // 统一字体
            var letter = new Material(Shader.Find("Twilight/Letter"));
            letter.SetColor("_HaloColor", new Color(0, 0, 0, 0)); letter.SetFloat("_Rough", 0f);
            letter.mainTexture = font.material.mainTexture;
            var root = new GameObject("DigitBake");
            for (int d = 0; d < 10; d++)
            {
                var go = new GameObject("D" + d); go.layer = layer;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0, (4.5f - d) * 1f, 0);
                var tm = go.AddComponent<TextMesh>();
                tm.font = font; tm.fontSize = 64; tm.characterSize = 0.112f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
                tm.text = d.ToString(); tm.color = Color.white;
                go.GetComponent<MeshRenderer>().sharedMaterial = letter;
                font.RequestCharactersInTexture(tm.text, 64);
            }
            letter.mainTexture = font.material.mainTexture;
            var camGO = new GameObject("BakeCam"); var cam = camGO.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 5f; cam.aspect = cw / (float)(ch * 10);
            cam.transform.position = new Vector3(0, 0, -5); cam.cullingMask = 1 << layer;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.nearClipPlane = 0.1f; cam.farClipPlane = 20f;
            var rt = new RenderTexture(cw, ch * 10, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(cw, ch * 10, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, cw, ch * 10), 0, 0);
            RenderTexture.active = null; cam.targetTexture = null;
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++) px[i] = new Color(1, 1, 1, px[i].r);
            tex.SetPixels(px); tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(DigitPath));
            File.WriteAllBytes(DigitPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); Object.DestroyImmediate(rt); Object.DestroyImmediate(camGO); Object.DestroyImmediate(root); Object.DestroyImmediate(letter);
            AssetDatabase.ImportAsset(DigitPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(DigitPath);
            imp.alphaSource = TextureImporterAlphaSource.FromInput; imp.alphaIsTransparency = true;
            imp.wrapModeU = TextureWrapMode.Clamp; imp.wrapModeV = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true; imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(DigitPath);
        }
    }
}
