using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Twilight.EditorTools
{
    /// <summary>渲染设置：Village / Portal 两个图层 + URP 渲染器里三个 RenderObjects 步骤（窗洞遮罩 → 村庄不透明 → 村庄透明），
    /// 都在主不透明绘制之前；Village / Portal 从普通绘制里排除。PC 与 Mobile 两个渲染器都改。</summary>
    public static class TKRenderSetup
    {
        public const int VillageLayer = 8;
        public const int PortalLayer = 9;

        [MenuItem("Twilight/0 渲染设置（传送门）")]
        public static void Apply()
        {
            SetLayer(VillageLayer, "Village");
            SetLayer(PortalLayer, "Portal");
            foreach (var path in new[] { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" })
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (!data) { Debug.LogWarning("找不到 " + path); continue; }
                int exclude = (1 << VillageLayer) | (1 << PortalLayer);
                data.opaqueLayerMask = ~exclude;
                data.transparentLayerMask = ~exclude;
                // 先删掉旧的同名步骤，再按顺序加
                var so = new SerializedObject(data);
                var feats = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                for (int i = feats.arraySize - 1; i >= 0; i--)
                {
                    var f = feats.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (f && f.name.StartsWith("TK_"))
                    {
                        feats.DeleteArrayElementAtIndex(i);
                        map.DeleteArrayElementAtIndex(i);
                        Object.DestroyImmediate(f, true);
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                Add(data, "TK_PortalMask", 1 << PortalLayer, RenderQueueType.Opaque, null);
                Add(data, "TK_VillageOpaque", 1 << VillageLayer, RenderQueueType.Opaque, CompareFunction.Equal);
                Add(data, "TK_VillageTransparent", 1 << VillageLayer, RenderQueueType.Transparent, CompareFunction.Equal);
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("渲染设置完成");
        }

        static void Add(UniversalRendererData data, string name, int layerMask, RenderQueueType queue, CompareFunction? stencil)
        {
            var f = ScriptableObject.CreateInstance<RenderObjects>();
            f.name = name;
            var s = f.settings;
            s.passTag = name;
            s.Event = RenderPassEvent.BeforeRenderingOpaques;
            s.filterSettings.RenderQueueType = queue;
            s.filterSettings.LayerMask = layerMask;
            if (stencil.HasValue)
            {
                s.stencilSettings.overrideStencilState = true;
                s.stencilSettings.stencilReference = 1;
                s.stencilSettings.stencilCompareFunction = stencil.Value;
                s.stencilSettings.passOperation = StencilOp.Keep;
                s.stencilSettings.failOperation = StencilOp.Keep;
                s.stencilSettings.zFailOperation = StencilOp.Keep;
            }
            AssetDatabase.AddObjectToAsset(f, data);
            var so = new SerializedObject(data);
            var feats = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            feats.arraySize++;
            feats.GetArrayElementAtIndex(feats.arraySize - 1).objectReferenceValue = f;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out string _, out long lid);
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = lid;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetLayer(int index, string name)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            layers.GetArrayElementAtIndex(index).stringValue = name;
            tm.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
