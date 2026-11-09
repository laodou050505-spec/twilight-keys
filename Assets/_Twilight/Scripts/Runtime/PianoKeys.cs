using UnityEngine;

namespace Twilight
{
    /// <summary>琴键表现：两个八度（C4~B5，Key_60 … Key_83，轴心在键的后下沿）。
    /// 旋律音：按下并点亮对应的键（超出两个八度按八度折进来），按的时长跟音的时值走；伴奏音只让键微微发光、不按下。
    /// 长按：键一直按着，直到松手 / 结束。</summary>
    public class PianoKeys : MonoBehaviour
    {
        public const int Low = 60, High = 83;
        [Tooltip("按下的角度（度）")] public float pressAngle = 3.6f;
        public float glowFade = 2.2f;

        class Key { public Transform t; public Renderer r; public Quaternion rest; public float press, glow, holdUntil; public Color color; public bool held; }
        readonly Key[] keys = new Key[High - Low + 1];
        MaterialPropertyBlock mpb;
        static readonly int IdEmission = Shader.PropertyToID("_EmissionColor");

        void Awake() => Bind();

        public void Bind()
        {
            mpb ??= new MaterialPropertyBlock();
            for (int m = Low; m <= High; m++)
            {
                var t = FindDeep(transform, "Key_" + m);
                if (!t) continue;
                var r = t.GetComponent<Renderer>();
                if (r) foreach (var mat in r.sharedMaterials) if (mat) mat.EnableKeyword("_EMISSION");
                keys[m - Low] = new Key { t = t, r = r, rest = t.localRotation };
            }
        }

        static Transform FindDeep(Transform p, string n)
        {
            foreach (Transform c in p) { if (c.name == n) return c; var f = FindDeep(c, n); if (f) return f; }
            return null;
        }

        public static int Fold(int midi)
        {
            while (midi < Low) midi += 12;
            while (midi > High) midi -= 12;
            return midi;
        }

        /// <summary>旋律音：按下 + 点亮，保持 seconds 秒（长按传长一点、松手时 Release）。</summary>
        public void NoteOn(int midi, Color color, float seconds, bool hold = false)
        {
            var k = keys[Fold(midi) - Low];
            if (k == null) return;
            k.press = 1f; k.glow = 1f; k.color = color; k.holdUntil = Time.time + Mathf.Max(0.12f, seconds); k.held = hold;
        }

        /// <summary>伴奏：只微微发光。</summary>
        public void Glow(int midi, Color color, float amount = 0.35f)
        {
            var k = keys[Fold(midi) - Low];
            if (k == null) return;
            k.glow = Mathf.Max(k.glow, amount); k.color = color;
        }

    // ---- 第十五轮（用户：四条轨道要对应下面的琴键）----
    // 两个八度的键盘从左到右等分成 4 段，每条轨道正下方那一段就是这条轨道的键。敲中哪条轨道，就在那一段里按下一个键：
    // 优先同音名的键（旋律的高低感还在），没有就选音高最近的。记住音符 → 实际按下的键，松手时放开同一个键。
    readonly System.Collections.Generic.Dictionary<int, int> laneKeyOf = new();

    /// <summary>轨道 lane（0 = 最左）正下方那一段琴键里，和 midi 最配的那个键（返回 midi 号）。</summary>
    public int LaneKey(int midi, int lane)
    {
        float xMin = float.MaxValue, xMax = float.MinValue;
        foreach (var k in keys) if (k != null) { float x = transform.InverseTransformPoint(k.t.position).x; xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x); }
        if (xMax <= xMin) return Fold(midi);
        // 低音（Key_60）在玩家左手边：本地 x 越大越靠左
        float leftX = transform.InverseTransformPoint(keys[0] != null ? keys[0].t.position : Vector3.zero).x;
        bool leftIsMax = Mathf.Abs(leftX - xMax) < Mathf.Abs(leftX - xMin);
        float pad = (xMax - xMin) / 46f;                         // 两头各补半个白键宽，四段等分整排键
        float a = xMin - pad, b = xMax + pad, w = (b - a) / 4f;
        int seg = leftIsMax ? 3 - Mathf.Clamp(lane, 0, 3) : Mathf.Clamp(lane, 0, 3);
        float s0 = a + seg * w, s1 = s0 + w;
        int best = -1, bestScore = int.MaxValue, pc = ((midi % 12) + 12) % 12;
        for (int m = Low; m <= High; m++)
        {
            var k = keys[m - Low];
            if (k == null) continue;
            float x = transform.InverseTransformPoint(k.t.position).x;
            if (x < s0 || x >= s1) continue;
            int score = (m % 12 == pc ? 0 : 100) + Mathf.Abs(m - midi);
            if (score < bestScore) { bestScore = score; best = m; }
        }
        return best >= 0 ? best : Fold(midi);
    }

    /// <summary>旋律音按轨道点亮：按下的是这条轨道正下方的键。</summary>
    public void NoteOnLane(int midi, int lane, Color color, float seconds, bool hold = false)
    {
        int key = LaneKey(midi, lane);
        laneKeyOf[midi] = key;
        NoteOn(key, color, seconds, hold);
    }
        public void Release(int midi)
        {
            if (laneKeyOf.TryGetValue(midi, out var mapped)) { laneKeyOf.Remove(midi); midi = mapped; }
            var k = keys[Fold(midi) - Low];
            if (k != null) { k.held = false; k.holdUntil = 0; }
        }

        public void ReleaseAll() { laneKeyOf.Clear(); foreach (var k in keys) if (k != null) { k.held = false; k.holdUntil = 0; } }

        void Update()
        {
            float dt = Time.deltaTime, now = Time.time;
            foreach (var k in keys)
            {
                if (k == null) continue;
                bool down = k.held || now < k.holdUntil;
                float target = down ? 1f : 0f;
                k.press = Mathf.MoveTowards(k.press, target, dt * (down ? 30f : 9f));
                k.t.localRotation = k.rest * Quaternion.Euler(-pressAngle * EaseOut(k.press), 0, 0);
                if (!down) k.glow = Mathf.MoveTowards(k.glow, 0f, dt * glowFade);
                if (k.r)
                {
                    k.r.GetPropertyBlock(mpb);
                    mpb.SetColor(IdEmission, k.color * (k.glow * k.glow * 1.6f));
                    k.r.SetPropertyBlock(mpb);
                }
            }
        }

        static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
    }
}
