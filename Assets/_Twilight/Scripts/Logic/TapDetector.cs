using System;
using System.Collections.Generic;

namespace Twilight.Logic
{
    public enum Finger { Thumb, Index, Middle, Ring, Pinky }
    public enum Side { Left, Right }

    public struct V3 { public float x, y, z; public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }

    /// <summary>差一点没认出来的一次下落（教程提示 + 真机日志调参用）。</summary>
    public struct NearMiss
    {
        public Side side; public Finger finger; public int lane;
        /// <summary>high = 没落到桌面附近；slow = 落得太慢；zone = 不在琴键前面；locked = 刚敲过 / 手指没抬起来。</summary>
        public string reason;
        public float t, minY, drop, speed;
    }

    public struct TapEvent
    {
        public bool release;          // false = 敲击，true = 抬起（校准 / 长按用）
        public int lane;
        public Side side;
        public Finger finger;
        public float t, x, z, speed, minY;
    }

    [Serializable]
    public class TapConfig
    {
        public int lanes = 4;
        public float laneW = 0.082f;       // 每条轨道宽（米）= 两个八度键盘宽 / 4
        public float sideMargin = 0.10f;   // 两侧多给一点：超出的算最近的那条（第三十七轮 0.07 → 0.10）
        public float zoneFar = 0.30f;      // 判定线远侧（朝窗户）；第三十七轮（用户：手势识别范围放大）0.24 → 0.30
        public float zoneNear = 0.18f;     // 判定线近侧（朝玩家）；第三十七轮 0.14 → 0.18
        public float zoneTop = 0.06f;    // 第三十七轮（用户：按下去有时没反应）4.5 → 6 cm；     // 落点要在接触面以上这么高以内（11-02 真机两分钟只认出 5 下：3 cm 太严，追踪的指尖常停在桌面上方 3–4 cm）
        public float minDrop = 0.004f;   // 第三十七轮 6 → 4 mm（手抬得不高也认）；     // 最近 0.15 秒里至少往下落了这么多（只是“低”不算——搭在桌上的手指不会误触）
        public float minDownSpeed = 0.065f; // 第三十七轮 0.1 → 0.065（轻轻落下也认）； // 往下的速度（米 / 秒，平滑后）
        public float rearmRise = 0.0045f; // 第三十七轮 9 → 4.5 mm（抬一点点就能敲下一下；抖动靠 smoothTau 滤掉）；   // 敲完要从最低点抬起这么多才能再敲（追踪抖动 ±5 mm 不会重新武装）
        public float refractory = 0.15f;   // 同一根手指两次敲击至少隔这么久
        public float laneRefractory = 0.12f; // 同一条轨道两次敲击至少隔这么久（不同手指同时落也只算一次）
        public float release = 0.026f;     // 长按：离最低点抬起超过这么多算松手（第二十三轮：3.2 → 2.6 cm，抖 1–2 cm 不断、拿开就松）
        public float handLock = 0.07f;   // 第三十七轮 0.1 → 0.07；      // 同一只手触发后这么久内其他手指不触发
        public bool ignoreThumb = true;    // 大拇指常搭在桌上：不参与判定
        public bool autoCalibrate = true;  // 用每次敲击的最低点慢慢校准桌面高度
        [UnityEngine.Tooltip("指尖高度平滑的时间常数（秒，0 = 不平滑）：落了多少 / 多快 / 抬了多少都用平滑后的高度算，追踪抖动不会被当成敲击")]
        public float smoothTau = 0.025f;
        [UnityEngine.Tooltip("手指刚被追踪到（或丢了又找回）时，离接触面这么高就算“准备好了”，第一下就能敲")]
        public float armOnAcquire = 0.02f;
    }

    /// <summary>指尖敲击检测。坐标 = 琴面坐标：原点在判定线中心、y 是高出接触面的米数，x 朝右，z 朝玩家。
    /// 规则（第二次真机日志后收紧：手指搭在桌上抖动会被当成连按、大拇指乱触发）：
    /// - 一次敲击 = 最近 0.15 秒里指尖往下落了 ≥ minDrop、速度 ≥ minDownSpeed、落点在接触面以上 zoneTop 以内、在判定区里——只是“低”不算；
    /// - 敲完要从最低点抬起 rearmRise 才能再敲；同一根手指 refractory、同一条轨道 laneRefractory 内只认一次；
    /// - 大拇指不参与（ignoreThumb）；同一只手触发后 handLock 内其他手指不触发；同帧多指取最深的那根；
    /// - 长按：触发那根手指离最低点没抬起超过 release 就算按着（IsLaneHeld）；
    /// - 自动校准：记最近 12 次敲击的最低点，取中位数，慢慢把接触面挪过去（±2.5 cm 以内）。</summary>
    public class TapDetector
    {
        class FingerState
        {
            public bool armed, down; public float y, t, x, minY, vy, sy, minSy, lastTap = -9f; public int lane = -1;
            // 一次下落的记录（诊断用）：从开始往下到开始往上
            public bool falling, fired, armedAtFall; public float fallTop, fallMin, fallMaxV; public V3 fallP;
            public readonly float[] hy = new float[16], ht = new float[16]; public int hn;
            /// <summary>最近 window 秒里的最高点。</summary>
            public float Recent(float now, float window)
            {
                float m = y;
                for (int k = 0; k < hn && k < hy.Length; k++) if (now - ht[k] <= window && hy[k] > m) m = hy[k];
                return m;
            }
            public void Push(float tt, float yy) { int k = hn % hy.Length; hy[k] = yy; ht[k] = tt; hn++; }
        }
        readonly float[] laneLast = { -9f, -9f, -9f, -9f, -9f, -9f, -9f, -9f };

        public TapConfig cfg;
        public bool multi;
        /// <summary>自动校准出来的接触面偏移（米，加在 y 上之前减掉）。</summary>
        public float Offset { get; private set; }
        readonly Dictionary<(Side, Finger), FingerState> fingers = new();
        readonly float[] lockUntil = { -1, -1 };
        readonly List<float> bottoms = new();
        static readonly Finger[] All = { Finger.Thumb, Finger.Index, Finger.Middle, Finger.Ring, Finger.Pinky };

        /// <summary>最近一帧里差一点没认出来的下落（每次 Update 清空重填）。</summary>
        public readonly List<NearMiss> NearMisses = new();

        public TapDetector(TapConfig c = null) { cfg = c ?? new TapConfig(); }

        public void Reset() { fingers.Clear(); lockUntil[0] = lockUntil[1] = -1; for (int i = 0; i < laneLast.Length; i++) laneLast[i] = -9f; }
        public void ResetCalibration() { Offset = 0; bottoms.Clear(); }

        public int LaneAt(float x)
        {
            float half = cfg.lanes * cfg.laneW / 2f;
            if (x < -half - cfg.sideMargin || x > half + cfg.sideMargin) return -1;
            return Math.Min(cfg.lanes - 1, Math.Max(0, (int)Math.Floor((x + half) / cfg.laneW)));
        }

        public bool InZone(V3 p) => LaneAt(p.x) >= 0 && p.z >= -cfg.zoneFar && p.z <= cfg.zoneNear;

        /// <summary>这条轨道上，触发它的手指是否还按着。</summary>
        public bool IsLaneHeld(int lane)
        {
            foreach (var s in fingers.Values) if (s.down && s.lane == lane) return true;
            // 第二十三轮（用户：长按别太松——手已经拿开了还显示按着）：
            // 只有“按下的那根手指追踪丢了”（不在列表里）时才找替补：同一只手的别的手指贴着琴面（2 cm 内）、横向还在这条轨道里（左右各放宽 0.25 条）。
            // 那根手指还在追踪、只是抬起来 / 移开了，就是真的松手，不找替补。
            if (!heldFinger.TryGetValue(lane, out var key) || fingers.ContainsKey(key)) return false;
            float half = cfg.lanes * cfg.laneW / 2f;
            float x0 = -half + lane * cfg.laneW - cfg.laneW * 0.25f, x1 = x0 + cfg.laneW * 1.5f;
            foreach (var kv in fingers)
            {
                var s = kv.Value;
                if (kv.Key.Item2 == Finger.Thumb || kv.Key.Item1 != key.Item1) continue;
                if (s.y <= 0.02f && s.x >= x0 && s.x <= x1) return true;
            }
            return false;
        }
        /// <summary>每条轨道当前长按是哪根手指按下的。</summary>
        readonly Dictionary<int, (Side, Finger)> heldFinger = new();

        /// <summary>tips[side] 为 null 表示这只手没追踪到；否则是 5 根手指的指尖（缺失的手指传 null）。</summary>
        public List<TapEvent> Update(float t, V3?[][] tips)
        {
            var events = new List<TapEvent>();
            NearMisses.Clear();
            var multiList = new List<TapEvent>();
            for (int si = 0; si < 2; si++)
            {
                var side = (Side)si;
                var hand = tips[si];
                TapEvent? pick = null; float pickY = 0;
                multiList.Clear();
                foreach (var f in All)
                {
                    var key = (side, f);
                    V3? pp = hand?[(int)f];
                    fingers.TryGetValue(key, out var s);
                    if (!pp.HasValue) { fingers.Remove(key); continue; }
                    var p = pp.Value; p.y -= Offset;
                    if (s == null)
                    {
                        fingers[key] = s = new FingerState { armed = p.y > cfg.armOnAcquire, y = p.y, t = t, minY = p.y, sy = p.y, minSy = p.y };
                        s.Push(t, p.y);
                        continue;
                    }
                    if (cfg.ignoreThumb && f == Finger.Thumb) { s.y = p.y; s.t = t; continue; }
                    float dt = Math.Max(1e-4f, t - s.t);
                    // 第三十七轮（用户：有时按下去没反应、手抬得不高认不出）：阈值放宽的同时，先把指尖高度平滑一下，
                    // 速度 / 下落量 / 抬起量都用平滑后的高度算——真敲击是连续几帧往下走，平滑后照样明显；追踪抖动忽上忽下，平滑后抵消
                    float sy = cfg.smoothTau <= 0f ? p.y : s.sy + (p.y - s.sy) * (1f - (float)Math.Exp(-dt / cfg.smoothTau));
                    float vy = (s.sy - sy) / dt;                       // 往下为正
                    s.vy = s.vy * 0.25f + vy * 0.75f;
                    s.sy = sy;
                    float drop = s.Recent(t, 0.15f) - sy;              // 最近 0.15 秒落了多少
                    Diagnose(s, side, f, p, t);
                    if (s.armed && p.y < cfg.zoneTop && drop >= cfg.minDrop && s.vy >= cfg.minDownSpeed && t - s.lastTap >= cfg.refractory)
                    {
                        s.armed = false; s.down = true; s.minY = p.y; s.minSy = sy; s.lane = -1; s.lastTap = t; s.fired = true;
                        int ln = LaneAt(p.x);
                        if (InZone(p) && (multi || t >= lockUntil[si]) && (ln < 0 || t - laneLast[ln] >= cfg.laneRefractory))
                        {
                            var cand = new TapEvent { lane = ln, side = side, finger = f, t = t, x = p.x, z = p.z, speed = s.vy };
                            if (ln >= 0) heldFinger[ln] = (side, f);
                            s.lane = cand.lane;
                            if (multi) multiList.Add(cand);
                            else if (pick == null || Better(cand, p.y, pick.Value, pickY)) { pick = cand; pickY = p.y; }
                        }
                    }
                    if (s.down || !s.armed)
                    {
                        s.minY = Math.Min(s.minY, p.y);
                        s.minSy = Math.Min(s.minSy, sy);
                        float rise = p.y - s.minY, sRise = sy - s.minSy;
                        // 长按松手：离最低点抬起超过 release
                        // 长按松手：抬起超过 release，或者横向滑出这条轨道（超过半条轨道宽）
                        float laneMid = s.lane >= 0 ? -cfg.lanes * cfg.laneW / 2f + (s.lane + 0.5f) * cfg.laneW : p.x;
                        if (s.down && (rise > cfg.release || Math.Abs(p.x - laneMid) > cfg.laneW))
                        {
                            events.Add(new TapEvent { release = true, side = side, finger = f, t = t, minY = s.minY, lane = s.lane });
                            s.down = false; s.lane = -1;
                        }
                        // 抬起 rearmRise 才能再敲（追踪抖动不会重新武装）；离开桌面区域也重新武装
                        if (!s.armed && (sRise > cfg.rearmRise || sy > cfg.zoneTop + 0.015f))
                        {
                            if (s.lane >= 0 && cfg.autoCalibrate) Calibrate(s.minY);
                            s.armed = true;
                        }
                    }
                    s.Push(t, sy);
                    s.y = p.y; s.t = t; s.x = p.x;
                }
                if (multiList.Count > 0)
                {
                    bool hasNonThumb = multiList.Exists(e => e.finger != Finger.Thumb);
                    foreach (var e in multiList) if (!hasNonThumb || e.finger != Finger.Thumb) events.Add(e);
                }
                if (pick.HasValue)
                {
                    events.Add(pick.Value);
                    lockUntil[si] = t + cfg.handLock;
                    if (pick.Value.lane >= 0) laneLast[pick.Value.lane] = t;
                    foreach (var kv in fingers)
                        if (kv.Key.Item1 == side && kv.Key.Item2 != pick.Value.finger && kv.Value.lane == pick.Value.lane) kv.Value.lane = -1;
                }
            }
            return events;
        }

        /// <summary>跟踪每次“往下落”：开始下落（速度 &gt; 4 cm/s）记起点，开始往上时结算——落了 5 mm 以上却没触发，就记一条原因。</summary>
        void Diagnose(FingerState s, Side side, Finger f, V3 p, float t)
        {
            if (f == Finger.Thumb) return;
            if (!s.falling)
            {
                if (s.vy > 0.04f) { s.falling = true; s.fired = false; s.armedAtFall = s.armed; s.fallTop = s.y; s.fallMin = p.y; s.fallMaxV = s.vy; s.fallP = p; }
                return;
            }
            if (p.y < s.fallMin) { s.fallMin = p.y; s.fallP = p; }
            s.fallMaxV = Math.Max(s.fallMaxV, s.vy);
            if (s.vy > -0.03f) return;                                   // 还在往下 / 停着
            s.falling = false;
            float drop = s.fallTop - s.fallMin;
            if (s.fired || drop < 0.005f) return;
            string why = s.fallMin > cfg.zoneTop ? "high" : !InZone(s.fallP) ? "zone" : s.fallMaxV < cfg.minDownSpeed ? "slow" : "locked";
            NearMisses.Add(new NearMiss { side = side, finger = f, lane = LaneAt(s.fallP.x), reason = why, t = t, minY = s.fallMin, drop = drop, speed = s.fallMaxV });
        }

        /// <summary>一次敲击的最低点（已减去偏移）：理想值约 +4 mm（指尖关节中心在桌面上方）。取最近 12 次的中位数慢慢挪。</summary>
        void Calibrate(float bottom)
        {
            bottoms.Add(bottom + Offset);
            if (bottoms.Count > 12) bottoms.RemoveAt(0);
            if (bottoms.Count < 4) return;
            var sorted = new List<float>(bottoms); sorted.Sort();
            float med = sorted[sorted.Count / 2];
            float want = Math.Max(-0.025f, Math.Min(0.025f, med - 0.004f));
            Offset += (want - Offset) * 0.3f;
        }

        static bool Better(TapEvent a, float ay, TapEvent b, float by)
        {
            bool at = a.finger == Finger.Thumb, bt = b.finger == Finger.Thumb;
            if (at != bt) return bt;
            return ay < by;
        }

        /// <summary>延迟校准：每次敲击离最近节拍的偏差，去掉前 skip 下，取中位数（移植 latencyFromTaps）。</summary>
        public static (float offset, float spread, int n)? LatencyFromTaps(IList<float> taps, IList<float> beats, int skip = 2, float min = -0.06f, float max = 0.25f)
        {
            var offs = new List<float>();
            foreach (var t in taps)
            {
                float best = float.PositiveInfinity;
                foreach (var b in beats) if (Math.Abs(t - b) < Math.Abs(best)) best = t - b;
                if (!float.IsInfinity(best)) offs.Add(best);
            }
            if (offs.Count > skip) offs.RemoveRange(0, skip); else offs.Clear();
            if (offs.Count < 3) return null;
            offs.Sort();
            int mid = offs.Count / 2;
            float med = offs.Count % 2 == 1 ? offs[mid] : (offs[mid - 1] + offs[mid]) / 2f;
            return (Math.Min(max, Math.Max(min, med)), offs[^1] - offs[0], offs.Count);
        }
    }
}
