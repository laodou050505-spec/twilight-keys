using System.Collections.Generic;
using Twilight.Logic;
using UnityEngine;

namespace Twilight
{
    /// <summary>音块表现：把 GameCore 里的块摆到光河上。
    /// 单按块：磨砂玻璃块（Tile.fbx + GlassTile）；击中后放大淡出 + 光环。
    /// 长按块：每帧沿光河采样生成的弯曲玻璃光条（GlassHold），按住时从头开始被点亮、吃掉。
    /// s = 离琴键前沿的弧长：块的前沿在 beat == tile.beat 时到达判定线 hitS。</summary>
    public class TileField : MonoBehaviour
    {
        public LightRiver river;
        public GameObject tilePrefab;
        public Material tileMaterial, holdMaterial, burstMaterial;
        [Tooltip("每拍在光河上的长度（米）")] public float beatLen = 0.18f;
        [Tooltip("判定线位置（离琴键前沿，米）")] public float hitS = 0.055f;
        public float tapLen = 0.05f;
        public static readonly Color[] LaneColors =
        {
            new(1.0f, 0.77f, 0.42f), new(1.0f, 0.56f, 0.37f), new(0.95f, 0.40f, 0.55f), new(0.48f, 0.72f, 1.0f),
        };

        class TapView { public GameObject go; public Renderer r; public float hitAge = -1; public Vector3 hitPos; public Quaternion hitRot; }
        class HoldView { public GameObject go; public Mesh mesh; public MeshRenderer r; public float fadeOut = -1; }
        class Burst { public GameObject go; public Renderer r; public float age = 1; }

        readonly Dictionary<Tile, TapView> taps = new();
        readonly Dictionary<Tile, HoldView> holds = new();
        readonly Stack<TapView> tapPool = new();
        readonly Stack<HoldView> holdPool = new();
        readonly List<Burst> bursts = new();
        readonly HashSet<Tile> seen = new();
        readonly List<Tile> dead = new();
        MaterialPropertyBlock mpb;
        static readonly int IdColor = Shader.PropertyToID("_Color"), IdFade = Shader.PropertyToID("_Fade"), IdHit = Shader.PropertyToID("_Hit"),
            IdConsumed = Shader.PropertyToID("_Consumed"), IdActive = Shader.PropertyToID("_Active"), IdBroken = Shader.PropertyToID("_Broken"),
            IdAge = Shader.PropertyToID("_Age"), IdPower = Shader.PropertyToID("_Power");

        void OnEnable() { mpb ??= new MaterialPropertyBlock(); }

        public void Clear()
        {
            foreach (var v in taps.Values) Recycle(v);
            foreach (var v in holds.Values) Recycle(v);
            taps.Clear(); holds.Clear();
        }

        /// <summary>每帧调用：beat = 当前拍位。</summary>
        public void Sync(GameCore g, float dt)
        {
            if (!river) return;
            mpb ??= new MaterialPropertyBlock();
            float len = river.Length;
            seen.Clear();
            foreach (var t in g.Tiles)
            {
                float d = (t.beat - g.Beat) * beatLen;              // 前沿离判定线
                if (d > len) break;                                  // 还在窗外
                seen.Add(t);
                if (t.hold) SyncHold(t, g, d, dt);
                else SyncTap(t, d, dt);
            }
            // 已从 GameCore 移除（回收）的块
            dead.Clear();
            foreach (var kv in taps) if (!seen.Contains(kv.Key) && kv.Value.hitAge < 0) dead.Add(kv.Key);
            foreach (var t in dead) { Recycle(taps[t]); taps.Remove(t); }
            dead.Clear();
            foreach (var kv in holds) if (!seen.Contains(kv.Key) && kv.Value.fadeOut < 0) dead.Add(kv.Key);
            foreach (var t in dead) { Recycle(holds[t]); holds.Remove(t); }
            // 击中后飞散中的块（不跟谱面走了）
            dead.Clear();
            foreach (var kv in taps)
            {
                var v = kv.Value;
                if (v.hitAge < 0) continue;
                v.hitAge += dt / 0.28f;
                float k = Mathf.Clamp01(v.hitAge);
                v.go.transform.SetPositionAndRotation(v.hitPos + Vector3.up * 0.03f * k, v.hitRot);
                v.go.transform.localScale = new Vector3(TileWidth / MeshWidth, 1f, 1f) * Scale(hitS) * (1f + 0.6f * k);
                SetTap(v, kv.Key.lane, 1f - k, 1f);
                if (v.hitAge >= 1f) dead.Add(kv.Key);
            }
            foreach (var t in dead) { Recycle(taps[t]); taps.Remove(t); }
            UpdateBursts(dt);
        }

        float Scale(float s) => river.WidthAt(Mathf.Max(0, s)) / river.widthNear;

        // ---- 第十七轮（用户：音块在对应的琴键上了，但要待在自己的轨道里，不要混出去）----
        // 音块收窄到轨道宽的 42%（约 1.5 个白键），横向位置尽量正对它的琴键，但整块始终落在本轨道两条分界线之间
        public const float TileWidth = 0.88f;                 // 占轨道宽的比例（音块和长条一样）；第十八轮（用户）：和轨道差不多宽
        const float MeshWidth = 0.8f;                         // 音块模型原本占轨道宽的比例（0.066 / 0.082）
        static float Pos(Tile t) { float m = Mathf.Max(0f, 0.5f - TileWidth / 2f - 0.03f); return t.lane + Mathf.Clamp(t.keyPos - t.lane, -m, m); }   // 宽到占满轨道时就是轨道正中
        float FadeAt(float s) => Mathf.Clamp01((river.Length - s) / 0.35f);

        void SyncTap(Tile t, float d, float dt)
        {
            taps.TryGetValue(t, out var v);
            if (t.state == TileState.Hit)
            {
                if (v != null && v.hitAge < 0) { v.hitAge = 0; v.hitPos = v.go.transform.position; v.hitRot = v.go.transform.rotation; }
                return;
            }
            float raw = hitS + d + tapLen / 2f;
            // 漏掉的块（宽松模式下游戏继续）：越过判定线后很快淡出
            float missFade = t.state == TileState.Missed ? Mathf.Clamp01((raw + 0.06f) / 0.1f) : 1f;
            if (missFade <= 0f) { if (v != null) { Recycle(v); taps.Remove(t); } return; }
            if (v == null) { v = Get(); taps[t] = v; }
            float s = Mathf.Max(raw, -0.2f);
            v.go.transform.SetPositionAndRotation(river.Point(s, Pos(t), 0.0015f), river.Rotation(s) * tilePrefab.transform.localRotation);
            v.go.transform.localScale = new Vector3(TileWidth / MeshWidth, 1f, 1f) * Scale(s);
            // 第三十九轮（用户：漏了要有反馈）：过了判定线还没敲到（还在宽容时间里），马上开始褪成灰、变淡——一眼看出“这块要漏了”
            float overdue = t.state == TileState.Pending ? Mathf.Clamp01((hitS - raw) / 0.07f) : 0f;
            SetTap(v, t.lane, FadeAt(s) * missFade * (1f - 0.45f * overdue), t.state == TileState.Missed ? 1f : 0f, t.state == TileState.Missed, overdue);
        }

        void SetTap(TapView v, int lane, float fade, float hit, bool missed = false, float overdue = 0f)
        {
            v.r.GetPropertyBlock(mpb);
            mpb.SetColor(IdColor, missed ? new Color(0.62f, 0.56f, 0.54f) : Color.Lerp(LaneColors[lane], new Color(0.62f, 0.58f, 0.56f), overdue));   // 漏掉 = 灰（不刺眼的红）
            mpb.SetFloat(IdFade, fade);
            mpb.SetFloat(IdHit, hit);
            v.r.SetPropertyBlock(mpb);
        }

        TapView Get()
        {
            if (tapPool.Count > 0) { var p = tapPool.Pop(); p.go.SetActive(true); p.hitAge = -1; return p; }
            var go = Instantiate(tilePrefab, transform);
            var r = go.GetComponentInChildren<Renderer>();
            r.sharedMaterial = tileMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return new TapView { go = go, r = r };
        }

        void Recycle(TapView v) { v.go.SetActive(false); v.hitAge = -1; tapPool.Push(v); }
        void Recycle(HoldView v) { v.go.SetActive(false); v.fadeOut = -1; holdPool.Push(v); }

        void SyncHold(Tile t, GameCore g, float d, float dt)
        {
            holds.TryGetValue(t, out var v);
            if (v == null)
            {
                if (holdPool.Count > 0) { v = holdPool.Pop(); v.go.SetActive(true); }
                else
                {
                    var go = new GameObject("HoldBar");
                    go.transform.SetParent(transform, false);
                    var mesh = new Mesh { name = "HoldBar" }; mesh.MarkDynamic();
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = holdMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    v = new HoldView { go = go, mesh = mesh, r = r };
                }
                holds[t] = v;
            }
            float total = t.dur * beatLen - 0.025f;                  // 和下一块之间留一点缝
            float consumed = t.state == TileState.Holding || (t.state == TileState.Hit && t.holdDone) ? Mathf.Clamp(t.held * beatLen, 0, total) : 0f;
            if (t.state == TileState.Holding) consumed = Mathf.Clamp((g.Beat - t.beat) * beatLen, 0, total);
            float head = hitS + d, tail = head + total;
            if (t.state != TileState.Pending && t.state != TileState.Missed) head = Mathf.Max(head, hitS - 0.01f);   // 按住时头部钉在判定线
            if (t.state == TileState.Missed) head = Mathf.Max(head, 0f);                // 漏掉的长条：从琴键前沿开始收掉
            if (tail <= head + 0.005f) { v.go.SetActive(false); return; }
            v.go.SetActive(true);
            BuildBar(v.mesh, head, Mathf.Min(tail, river.Length), Pos(t));
            v.r.GetPropertyBlock(mpb);
            mpb.SetColor(IdColor, t.state == TileState.Missed ? new Color(1f, 0.35f, 0.3f) : LaneColors[t.lane]);
            float barLen = Mathf.Max(0.001f, Mathf.Min(tail, river.Length) - head);
            float lit = t.state == TileState.Holding ? 0.06f / barLen : (t.holdDone ? 1f : 0f);    // 头部点燃的一小段
            mpb.SetFloat(IdConsumed, Mathf.Clamp01(lit));
            mpb.SetFloat(IdActive, t.state == TileState.Holding ? 1f : 0f);
            mpb.SetFloat(IdBroken, t.holdBroken ? 1f : 0f);
            float fade = FadeAt(tail) * 0.6f + 0.4f;
            if (t.state == TileState.Hit) fade *= Mathf.Clamp01(1f - (g.Beat - (t.beat + t.dur)) * 2f) * (t.holdDone ? 1f : 0.7f);
            if (t.state == TileState.Missed) fade *= Mathf.Clamp01((tail - head) / 0.08f);
            mpb.SetFloat(IdFade, fade);
            v.r.SetPropertyBlock(mpb);
        }

        readonly List<Vector3> bv = new(); readonly List<Vector3> bn = new(); readonly List<Vector2> bu = new(); readonly List<int> bt = new();

        /// <summary>沿光河从 s0 到 s1 生成一条带侧壁的玻璃条（世界坐标）。</summary>
        void BuildBar(Mesh m, float s0, float s1, float lane)
        {
            bv.Clear(); bn.Clear(); bu.Clear(); bt.Clear();
            int n = Mathf.Clamp(Mathf.CeilToInt((s1 - s0) / 0.02f), 2, 60);
            float h = 0.011f;
            for (int i = 0; i <= n; i++)
            {
                float s = Mathf.Lerp(s0, s1, (float)i / n), v = (float)i / n;
                float w = river.WidthAt(Mathf.Max(0, s)) / LightRiver.Lanes * TileWidth;
                // 网格挂在本节点下（World 的子节点）：必须转成本地坐标。原来直接写世界坐标，
                // 真机上 World 被放到真实桌面（平移 + 旋转）后长条被变换了两次，跑到银河外面去了
                var c = transform.InverseTransformPoint(river.Point(s, lane, 0.001f));
                var rot = Quaternion.Inverse(transform.rotation) * river.Rotation(s);
                var up = rot * Vector3.up; var right = rot * Vector3.right;
                // 截面：左下、左上、右上、右下
                bv.Add(c - right * w / 2); bv.Add(c - right * w / 2 + up * h); bv.Add(c + right * w / 2 + up * h); bv.Add(c + right * w / 2);
                bn.Add(-right); bn.Add(up); bn.Add(up); bn.Add(right);
                bu.Add(new Vector2(0, v)); bu.Add(new Vector2(0.02f, v)); bu.Add(new Vector2(0.98f, v)); bu.Add(new Vector2(1, v));
            }
            for (int i = 0; i < n; i++)
            {
                int a = i * 4, b = a + 4;
                bt.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                bt.AddRange(new[] { a + 1, a + 2, b + 1, b + 1, a + 2, b + 2 });
                bt.AddRange(new[] { a + 2, a + 3, b + 2, b + 2, a + 3, b + 3 });
            }
            m.Clear();
            m.SetVertices(bv); m.SetNormals(bn); m.SetUVs(0, bu); m.SetTriangles(bt, 0);
            m.RecalculateBounds();
        }

        /// <summary>击中光效：在判定线上 lane 的位置炸开一圈光环和火花。power：PERFECT 1.2 / GREAT 1 / GOOD 0.8。</summary>
        public void BurstAt(int lane, float power)
        {
            mpb ??= new MaterialPropertyBlock();
            Burst b = bursts.Find(x => x.age >= 1f);
            if (b == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                if (Application.isPlaying) Destroy(go.GetComponent<Collider>()); else DestroyImmediate(go.GetComponent<Collider>());
                go.name = "HitBurst"; go.transform.SetParent(transform, false);
                var r = go.GetComponent<Renderer>(); r.sharedMaterial = burstMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b = new Burst { go = go, r = r };
                bursts.Add(b);
            }
            b.age = 0;
            b.go.SetActive(true);
            b.go.transform.SetPositionAndRotation(river.Point(hitS, lane, 0.004f), river.Rotation(hitS) * Quaternion.Euler(90, 0, 0));
            b.go.transform.localScale = Vector3.one * 0.1f * Mathf.Lerp(0.8f, 1.2f, power - 0.6f);
            b.r.GetPropertyBlock(mpb);
            mpb.SetColor(IdColor, LaneColors[lane]); mpb.SetFloat(IdPower, power); mpb.SetFloat(IdAge, 0);
            b.r.SetPropertyBlock(mpb);
        }

        void UpdateBursts(float dt)
        {
            foreach (var b in bursts)
            {
                if (b.age >= 1f) continue;
                b.age += dt / 0.5f;
                if (b.age >= 1f) { b.go.SetActive(false); continue; }
                b.r.GetPropertyBlock(mpb); mpb.SetFloat(IdAge, b.age); b.r.SetPropertyBlock(mpb);
            }
        }
    }
}
