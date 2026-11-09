using Twilight.Logic;
using UnityEngine;

namespace Twilight
{
    /// <summary>指尖光点（让玩家看见“系统认到我的手指在哪”）：手伸到琴键上方时，每根手指（不含大拇指）正下方的桌面上亮一个小光点——
    /// 离桌面越高越是一个大而淡的空心圈，越靠近越收成实心小点；进到能敲的高度时变成那条轨道的颜色、最亮。
    /// 教程里全亮；正式弹奏时淡一点（边玩边看）。</summary>
    public class TKFingerMarks : MonoBehaviour
    {
        public HandTapInput hands;
        public Transform stage;
        public Material material;
        [Range(0, 1)] public float strength = 0.45f;

        static readonly Color[] Lane = { new(1.0f, 0.77f, 0.42f), new(1.0f, 0.56f, 0.37f), new(0.95f, 0.40f, 0.55f), new(0.48f, 0.72f, 1.0f) };
        const int Max = 8;
        Mesh mesh;
        readonly Vector3[] v = new Vector3[Max * 4];
        readonly Vector2[] uv2 = new Vector2[Max * 4];
        readonly Color[] col = new Color[Max * 4];
        readonly float[] vis = new float[Max];

        void Awake()
        {
            mesh = new Mesh { name = "FingerMarks" };
            mesh.MarkDynamic();
            var uv = new Vector2[Max * 4]; var tri = new int[Max * 6];
            for (int i = 0; i < Max; i++)
            {
                uv[i * 4] = new Vector2(-1, -1); uv[i * 4 + 1] = new Vector2(1, -1); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(-1, 1);
                int a = i * 4, t = i * 6;
                tri[t] = a; tri[t + 1] = a + 2; tri[t + 2] = a + 1; tri[t + 3] = a; tri[t + 4] = a + 3; tri[t + 5] = a + 2;
            }
            mesh.vertices = v; mesh.uv = uv; mesh.uv2 = uv2; mesh.colors = col; mesh.triangles = tri;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        void LateUpdate()
        {
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); transform.localScale = Vector3.one;
            bool ok = hands && stage && !float.IsNaN(hands.contactY) && strength > 0.001f;
            var cfg = ok ? hands.Detector.cfg : null;
            int k = 0;
            for (int side = 0; side < 2; side++)
            {
                var j = !ok ? null : side == 0 ? hands.Left : hands.Right;
                var tips = j == null ? null : new[] { j.indexTip, j.middleTip, j.ringTip, j.pinkyTip };
                for (int f = 0; f < 4; f++, k++)
                {
                    float target = 0f; Vector3 c = default; float r = 0.01f, ring = 1f; Color cc = Color.white;
                    if (tips != null)
                    {
                        var p = hands.ToKeys(tips[f]);
                        float h = p.y - hands.Detector.Offset;
                        bool over = hands.Detector.InZone(new V3(p.x, 0, p.z));
                        if (over && h < 0.16f && h > -0.03f)
                        {
                            float n = Mathf.Clamp01(h / 0.12f);
                            r = Mathf.Lerp(0.006f, 0.022f, n);
                            ring = Mathf.Clamp01((h - cfg.zoneTop * 0.6f) / 0.05f);              // 能敲的高度以内 = 实心
                            int lane = hands.Detector.LaneAt(p.x);
                            cc = lane >= 0 ? Lane[lane] : Color.white;
                            if (h > cfg.zoneTop) cc = Color.Lerp(cc, new Color(1f, 0.95f, 0.85f), 0.55f);
                            target = Mathf.Lerp(1f, 0.35f, n);
                            // 指尖正下方（琴面坐标的 x / z，高度贴着桌面上方 2 mm）
                            var local = new Vector3(p.x, 0, -p.z + hands.hitZ);
                            c = stage.TransformPoint(local); c.y = hands.contactY + hands.Detector.Offset - 0.005f;
                        }
                    }
                    vis[k] = Mathf.MoveTowards(vis[k], target, Time.deltaTime * 6f);
                    float a = vis[k] * strength;
                    var right = stage.right * r; var fwd = stage.forward * r;
                    v[k * 4] = c - right - fwd; v[k * 4 + 1] = c + right - fwd; v[k * 4 + 2] = c + right + fwd; v[k * 4 + 3] = c - right + fwd;
                    cc.a = a;
                    for (int q = 0; q < 4; q++) { col[k * 4 + q] = cc; uv2[k * 4 + q] = new Vector2(ring, 0); }
                }
            }
            mesh.vertices = v; mesh.colors = col; mesh.uv2 = uv2;
        }
    }
}
