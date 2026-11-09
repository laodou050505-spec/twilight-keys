using Twilight.Logic;
using UnityEngine;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace Twilight
{
    /// <summary>头显手部输入：PICO 手部追踪 26 个关节 → 世界坐标 → 琴面坐标（原点在判定线中心、y = 高出接触面、x 朝右、z 朝玩家）
    /// → TapDetector（移植自原型）→ GameDirector.Tap；长按问 TapDetector.IsLaneHeld（触发那根手指还没抬起）。
    /// 每帧也把两只手的关键关节公开出来（放琴、暂停手势用）。比 GameDirector 先跑。</summary>
    [DefaultExecutionOrder(-50)]
    public class HandTapInput : MonoBehaviour
    {
        [Tooltip("追踪空间原点（XR Origin）")] public Transform trackingOrigin;
        public GameDirector director;
        [Tooltip("琴（Stage）：本地原点 = 琴键前沿中点，+Z 指向窗户")] public Transform stage;
        [Tooltip("判定线在 Stage 本地的 z")] public float hitZ = 0.055f;
        public TapConfig config = new();

        /// <summary>接触高度（世界 y，指尖关节贴着桌面时的高度）；放琴时设置。NaN = 还没放好，不检测敲击。</summary>
        [System.NonSerialized] public float contactY = float.NaN;
        /// <summary>测试用的假手（编辑器里验证拨小岛 / 按钮；非空时代替真手）。</summary>
        [System.NonSerialized] public HandJoints simLeft, simRight;
        /// <summary>差一点没认出来的敲击（教程据此提示“低一点 / 干脆一点”）。</summary>
        public event System.Action<NearMiss> Missed;
        public HandJoints Left { get; private set; }
        public HandJoints Right { get; private set; }
        public bool Tracked => Left != null || Right != null;
        public TapDetector Detector { get; private set; }
        public int TapCount { get; private set; }

        readonly V3?[][] tips = { new V3?[5], new V3?[5] };
        readonly V3?[][] none = { null, null };
#if ENABLE_PICO_XR_SDK
        HandJointLocations locs = new() { jointLocations = new HandJointLocation[(int)HandJoint.JointMax] };
#endif

        void Awake() { Detector = new TapDetector(config); }

        void OnEnable() { if (director) director.HandHeld = lane => Detector != null && Detector.IsLaneHeld(lane); }

        void Update()
        {
            Detector ??= new TapDetector(config);                // 运行中重新编译后会丢
            Left = simLeft ?? Read(false);
            Right = simRight ?? Read(true);
            if (float.IsNaN(contactY) || !stage) { Detector.Reset(); return; }
            var t = new[] { Left != null ? Fill(0, Left) : null, Right != null ? Fill(1, Right) : null };
            foreach (var e in Detector.Update(Time.time, t))
            {
                if (e.release || e.lane < 0) continue;
                TapCount++;
                Debug.Log($"[TK] 敲 轨道{e.lane} {e.side}/{e.finger} 速度{e.speed:F2} x{e.x:F3} z{e.z:F3} 校准{Detector.Offset * 1000f:F0}mm");
                if (director) director.Tap(e.lane);
            }
            foreach (var m in Detector.NearMisses)
            {
                Missed?.Invoke(m);
                Debug.Log($"[TK] 未认 {m.reason} 轨道{m.lane} {m.side}/{m.finger} 最低{m.minY * 1000f:F0}mm 落{m.drop * 1000f:F0}mm 速度{m.speed:F2}");
            }
        }

        V3?[] Fill(int side, HandJoints j)
        {
            var a = tips[side];
            a[0] = ToKeys(j.thumbTip); a[1] = ToKeys(j.indexTip); a[2] = ToKeys(j.middleTip); a[3] = ToKeys(j.ringTip); a[4] = ToKeys(j.pinkyTip);
            return a;
        }

        /// <summary>世界坐标 → 琴面坐标（x 右，y 高出接触面，z 朝玩家；原点在判定线中心）。</summary>
        public V3 ToKeys(Vector3 w)
        {
            var l = stage.InverseTransformPoint(w);
            return new V3(l.x, w.y - contactY, -(l.z - hitZ));
        }

        HandJoints Read(bool right)
        {
#if ENABLE_PICO_XR_SDK && !UNITY_EDITOR
            if (PXR_HandTracking.GetActiveInputDevice() != ActiveInputDevice.HandTrackingActive) return null;
            if (!PXR_HandTracking.GetJointLocations(right ? HandType.HandRight : HandType.HandLeft, ref locs) || locs.isActive == 0 || locs.jointLocations == null) return null;
            const ulong need = (ulong)HandLocationStatus.PositionValid;
            bool ok = true;
            Vector3 P(HandJoint jn)
            {
                var l = locs.jointLocations[(int)jn];
                if (((ulong)l.locationStatus & need) == 0) ok = false;
                var p = l.pose.Position.ToVector3();
                return trackingOrigin ? trackingOrigin.TransformPoint(p) : p;
            }
            var j = new HandJoints
            {
                right = right,
                wrist = P(HandJoint.JointWrist), thumbTip = P(HandJoint.JointThumbTip),
                indexTip = P(HandJoint.JointIndexTip), indexProx = P(HandJoint.JointIndexProximal),
                middleTip = P(HandJoint.JointMiddleTip), middleProx = P(HandJoint.JointMiddleProximal),
                ringTip = P(HandJoint.JointRingTip), ringProx = P(HandJoint.JointRingProximal),
                pinkyTip = P(HandJoint.JointLittleTip), pinkyProx = P(HandJoint.JointLittleProximal),
            };
            return ok ? j : null;
#else
            return null;
#endif
        }
    }
}
