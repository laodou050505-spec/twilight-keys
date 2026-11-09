using System.Collections.Generic;
using UnityEngine;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace Twilight
{
    /// <summary>真实房间里的桌子（第十七轮，用户：琴的位置要根据玩家的桌子）。
    /// 读 PICO 系统“空间扫描”里标成 Table 的三维框（PXR_MixedReality 场景识别），换算成世界坐标下的桌面：
    /// 桌面高度 = 框八个角里最高的 y；桌面四边 = 框在水平面上的两根轴。玩家没在系统里扫描过房间 / 没有桌子时 Found = false，
    /// TKSession 退回原来的估法（看手搭在哪、按坐姿估）。只在头显上读，编辑器里不动。</summary>
    public class TKRoomTable : MonoBehaviour
    {
        public struct Table
        {
            public Vector3 center;      // 桌面中心（世界坐标，y = 桌面高度）
            public Vector3 axisA, axisB; // 桌面两根水平轴（单位向量）
            public float halfA, halfB;   // 两个方向的半宽
        }

        [Tooltip("XR 追踪空间（XR Origin）：PICO 给的锚点坐标在这个空间里")] public Transform trackingSpace;
        public readonly List<Table> tables = new();
        public bool Found => tables.Count > 0;
        public string Status { get; private set; } = "未开始";

        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR && ENABLE_PICO_XR_SDK
            Load();
#endif
        }

#if ENABLE_PICO_XR_SDK
        async void Load()
        {
            try
            {
                var r0 = await PXR_MixedReality.StartSenseDataProvider(PxrSenseDataProviderType.SceneCapture);
                if (r0 != PxrResult.SUCCESS) { Status = "场景识别启动失败 " + r0; Debug.Log("[TK] " + Status); return; }
                var r = await PXR_MixedReality.QuerySceneAnchorAsync(new[] { PxrSemanticLabel.Table });
                if (r.result != PxrResult.SUCCESS) { Status = "查询桌子失败 " + r.result; Debug.Log("[TK] " + Status); return; }
                tables.Clear();
                foreach (var h in r.anchorHandleList)
                {
                    if (PXR_MixedReality.GetSceneBox3DData(h, out var pos, out var rot, out var ext) != PxrResult.SUCCESS) continue;
                    if (PXR_MixedReality.LocateAnchor(h, out var ap, out var ar) != PxrResult.SUCCESS) continue;
                    tables.Add(FromBox(ap, ar, pos, ext));
                }
                Status = $"系统扫描里有 {tables.Count} 张桌子";
                Debug.Log("[TK] " + Status);
            }
            catch (System.Exception e) { Status = "场景识别异常 " + e.Message; Debug.LogWarning("[TK] " + Status); }
        }
#endif

        /// <summary>锚点位姿 + 框（中心偏移、整尺寸）→ 世界坐标下的桌面。</summary>
        Table FromBox(Vector3 anchorPos, Quaternion anchorRot, Vector3 offset, Vector3 extent)
        {
            var sp = trackingSpace;
            Vector3 W(Vector3 p) => sp ? sp.TransformPoint(p) : p;
            Vector3 D(Vector3 d) => sp ? sp.TransformDirection(d) : d;
            var c = anchorPos + anchorRot * offset;
            var ax = new[] { anchorRot * Vector3.right, anchorRot * Vector3.up, anchorRot * Vector3.forward };
            var half = new[] { extent.x * 0.5f, extent.y * 0.5f, extent.z * 0.5f };
            // 竖直的那根轴 = 和世界上方向最接近的；另外两根是桌面的水平轴
            int up = 0; float best = -1;
            for (int i = 0; i < 3; i++) { float d = Mathf.Abs(Vector3.Dot(D(ax[i]).normalized, Vector3.up)); if (d > best) { best = d; up = i; } }
            int a = (up + 1) % 3, b = (up + 2) % 3;
            float topY = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                var p = c + ax[0] * half[0] * ((k & 1) == 0 ? -1 : 1) + ax[1] * half[1] * ((k & 2) == 0 ? -1 : 1) + ax[2] * half[2] * ((k & 4) == 0 ? -1 : 1);
                topY = Mathf.Max(topY, W(p).y);
            }
            var wc = W(c);
            Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.right; }
            return new Table { center = new Vector3(wc.x, topY, wc.z), axisA = Flat(D(ax[a])), axisB = Flat(D(ax[b])), halfA = half[a], halfB = half[b] };
        }

        /// <summary>离玩家最近、在玩家前方（±75°）的那张桌子；找不到返回 false。</summary>
        public bool Nearest(Vector3 head, Vector3 forward, out Table t)
        {
            t = default; float best = float.MaxValue; bool ok = false;
            forward.y = 0; forward.Normalize();
            foreach (var tb in tables)
            {
                var d = tb.center - head; d.y = 0;
                if (d.magnitude > 2.5f) continue;
                if (d.sqrMagnitude > 1e-4f && Vector3.Angle(forward, d) > 75f) continue;
                if (d.magnitude < best) { best = d.magnitude; t = tb; ok = true; }
            }
            return ok;
        }
    }
}
