using System.IO;
using UnityEditor;
using UnityEngine;

namespace Twilight.EditorTools
{
    /// <summary>美术辅助：建 / 更新 URP 材质、加载贴图。</summary>
    public static class TKArt
    {
        public const string MatDir = "Assets/_Twilight/Art/Materials";
        public const string TexDir = "Assets/_Twilight/Art/Textures";

        static Shader UrpLit => Shader.Find("Universal Render Pipeline/Lit");

        /// <summary>建或更新一个 URP Lit 材质（同名覆盖参数，保留 GUID，引用不丢）。</summary>
        public static Material Lit(string name, Color color, float smooth = 0.4f, float metal = 0f,
            string albedo = null, string normal = null, Vector2? tiling = null, float normalScale = 1f, Color? emission = null)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(UrpLit); AssetDatabase.CreateAsset(m, path); }
            if (m.shader != UrpLit) m.shader = UrpLit;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            var t = tiling ?? Vector2.one;
            var tex = albedo != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(albedo) : null;
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", t);
            var nrm = normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normal) : null;
            m.SetTexture("_BumpMap", nrm);
            m.SetTextureScale("_BumpMap", t);
            m.SetFloat("_BumpScale", normalScale);
            if (nrm) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            // 第十五轮（用户：场景模型反光太强，比如身后的树）：哑光材质（光滑度 ≤ 0.2、非金属）一律关掉高光和环境反射——
            // 天空很亮，URP Lit 默认的掠射反射会让树叶、房顶边缘泛一层白光；琴键 / 玻璃 / 铁这些本来就该亮的不受影响
            bool matte = smooth <= 0.2f && metal <= 0.01f;
            m.SetFloat("_SpecularHighlights", matte ? 0f : 1f);
            m.SetFloat("_EnvironmentReflections", matte ? 0f : 1f);
            if (matte) { m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
            else { m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        public static GameObject Model(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        /// <summary>第十五轮：材质目录里所有哑光 URP Lit 材质（光滑度 ≤ 0.2、非金属）统一关掉高光和环境反射。
        /// P2 模型的材质只在“接入 P2”时建一次，搭场景不会重建，所以每次搭场景都过一遍，保证规则对所有模型生效。</summary>
        public static int MatteAll()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MatDir }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!m || m.shader != UrpLit) continue;
                bool matte = m.GetFloat("_Smoothness") <= 0.2f && m.GetFloat("_Metallic") <= 0.01f;
                if (!matte) continue;
                m.SetFloat("_SpecularHighlights", 0f); m.SetFloat("_EnvironmentReflections", 0f);
                m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                EditorUtility.SetDirty(m); n++;
            }
            AssetDatabase.SaveAssets();
            return n;
        }

        public const string P2Src = "ArtSource/ModelsP2";
        public const string P2Dst = "Assets/_Twilight/Art/Models/P2";
        public const string PrefabDir = "Assets/_Twilight/Prefabs/P2";
        // 村子只透过一扇小窗、在几十到几百米外被看到，屏幕占比小：阈值收紧，远处用 5% 面数的 lod3
        public static readonly float[] LodHeights = { 0.25f, 0.10f, 0.035f, 0.002f };

        public static GameObject P2Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{name}.prefab");

        /// <summary>接入一个 P2 模型（final/report.json 全部合格才接）：拷 lod0..2 + albedo（+ 自发光图 emit.png），
        /// 建 URP Lit 材质，缩放到 size（axis：0 = 高、1 = 最长水平边），底面中心落在原点，正面朝 +Z。
        /// 返回预制体；材质名 P2_名称（自发光材质由 TwilightSky 统一调亮度）。</summary>
        public static GameObject ImportP2(string name, float size, int axis = 0, float smooth = 0.15f, bool emissive = false, bool twoSided = false)
        {
            var src = Path.Combine(P2Src, name, "final");
            var rep = Path.Combine(src, "report.json");
            if (!File.Exists(Path.Combine(src, "lod0.fbx")) || !File.Exists(rep) || !File.ReadAllText(rep).Contains("\"pass\": true")) return null;
            var dst = $"{P2Dst}/{name}";
            Directory.CreateDirectory(dst);
            bool changed = false;
            foreach (var f in new[] { "lod0.fbx", "lod1.fbx", "lod2.fbx", "lod3.fbx", "albedo.png" })
            {
                var s = Path.Combine(src, f); var d = Path.Combine(dst, f);
                if (File.Exists(s) && (!File.Exists(d) || File.GetLastWriteTimeUtc(s) > File.GetLastWriteTimeUtc(d))) { File.Copy(s, d, true); changed = true; }
            }
            var emitPath = $"{dst}/emit.png";
            if (emissive && (changed || !File.Exists(emitPath)))
            {
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/python3", $"Tools/emit_mask.py {dst}/albedo.png {emitPath}") { UseShellExecute = false, RedirectStandardOutput = true });
                p.WaitForExit();
                Debug.Log($"{name} 自发光像素 {p.StandardOutput.ReadToEnd().Trim()}");
                changed = true;
            }
            if (changed) AssetDatabase.Refresh();
            // 运行时合批要求网格可读
            foreach (var f in new[] { "lod0.fbx", "lod1.fbx", "lod2.fbx", "lod3.fbx" })
                if (AssetImporter.GetAtPath($"{dst}/{f}") is ModelImporter mi && !mi.isReadable) { mi.isReadable = true; mi.SaveAndReimport(); }
            foreach (var t in new[] { $"{dst}/albedo.png", emitPath })
            {
                if (AssetImporter.GetAtPath(t) is TextureImporter ti && ti.maxTextureSize != 2048)
                { ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
            }
            var mat = Lit("P2_" + name, Color.white, smooth, 0f, albedo: $"{dst}/albedo.png",
                emission: emissive ? new Color(1f, 0.9f, 0.8f) : (Color?)null);
            if (emissive) mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emitPath));
            // 花瓣 / 叶片 / 天灯纸这类单层薄片：双面渲染，背面不会消失
            mat.SetFloat("_Cull", twoSided ? 0f : 2f);
            mat.doubleSidedGI = twoSided;

            var root = new GameObject(name);
            var lods = new System.Collections.Generic.List<LOD>();
            Bounds b = default; bool hasB = false;
            for (int i = 0; i < LodHeights.Length; i++)
            {
                var m = Model($"{dst}/lod{i}.fbx");
                if (!m) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(m);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "LOD" + i;
                inst.transform.SetParent(root.transform, false);
                // P2 模型正面朝 −X：转 90° 让预制体正面朝 +Z
                inst.transform.localRotation = Quaternion.Euler(0, 90, 0) * inst.transform.localRotation;
                inst.transform.localPosition = Quaternion.Euler(0, 90, 0) * inst.transform.localPosition;
                var rs = inst.GetComponentsInChildren<Renderer>();
                foreach (var r in rs)
                {
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    if (!hasB) { b = r.bounds; hasB = true; } else if (i == 0) b.Encapsulate(r.bounds);
                }
                lods.Add(new LOD(LodHeights[i], rs));
            }
            if (!hasB) { Object.DestroyImmediate(root); return null; }
            float cur = axis == 0 ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            float s0 = size / Mathf.Max(cur, 1e-4f);
            foreach (Transform c in root.transform) { c.localScale *= s0; c.localPosition = (c.localPosition - new Vector3(b.center.x, b.min.y, b.center.z)) * s0; }
            // 远景替身（树 / 花丛）：几十个三角形的顶点色网格，作最远一级 LOD
            var proxy = TKProxy.For(name, b.size * s0);
            if (proxy)
            {
                var pg = new GameObject("LODProxy");
                pg.transform.SetParent(root.transform, false);
                pg.AddComponent<MeshFilter>().sharedMesh = proxy;
                var pr = pg.AddComponent<MeshRenderer>();
                pr.sharedMaterial = TKProxy.Material();
                pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pr.receiveShadows = false;
                // 原来的最后一级改成在稍大一点时就切到替身
                var last = lods[lods.Count - 1];
                lods[lods.Count - 1] = new LOD(0.03f, last.renderers);
                lods.Add(new LOD(0.0015f, new Renderer[] { pr }));
            }
            var lg = root.AddComponent<LODGroup>();
            lg.SetLODs(lods.ToArray());
            lg.RecalculateBounds();
            EnsureFolder(PrefabDir);
            var pf = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return pf;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
