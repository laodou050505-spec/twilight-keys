using Twilight.Logic;
using UnityEngine;

namespace Twilight
{
    /// <summary>节奏伴奏：演奏中按拍子自动走的轻打击（音色启动时用代码合成，无版权问题），给玩家一个能跟的拍子。
    /// 层次随连击叠加，失误（连击清零）就退回最简：
    /// 0 层：强拍一声闷鼓 + 其余拍木块；1 层（连击 ≥ 8）：+ 反拍沙锤；2 层（≥ 20）：+ 二四拍轻拍掌、四拍子第三拍补一声鼓；
    /// 3 层（≥ 40）：每小节第一拍加一声高音铃，木块更亮。
    /// 按 dspTime 提前约 0.1 秒排程（不受帧率抖动影响）；拍位跟 GameCore.Beat / Bps 走（变速时也对得上）。</summary>
    public class BeatTrack : MonoBehaviour
    {
        public GameDirector director;
        [Range(0, 1)] public float volume = 0.55f;
        public float lookahead = 0.1f;
        public static readonly int[] LayerCombo = { 0, 8, 20, 40 };

        AudioClip kick, tick, shaker, snap, bell;
        AudioSource[] pool;
        int next;
        float scheduledTo = float.NegativeInfinity;   // 已排到第几拍（半拍为单位推进）
        public int Layer { get; private set; }

        void Awake()
        {
            const int sr = 48000;
            kick = Make("Kick", sr, 0.38f, (t, r) =>
            {
                // 毛毡闷鼓：95 → 48 Hz 下滑的正弦 + 一点点低通噪声的敲击感
                float ph = 2 * Mathf.PI * (48f * t + 47f * 0.035f * (1 - Mathf.Exp(-t / 0.035f)));
                float body = Mathf.Sin(ph) * Mathf.Exp(-t / 0.13f);
                float click = r * Mathf.Exp(-t / 0.004f) * 0.18f;
                return (body + click) * Env(t, 0.002f) * 0.9f;
            });
            tick = Make("Tick", sr, 0.12f, (t, r) =>
            {
                // 木块：两个不谐和泛音快速衰减
                float s = Mathf.Sin(2 * Mathf.PI * 1180f * t) * 0.6f + Mathf.Sin(2 * Mathf.PI * 1730f * t) * 0.3f + r * 0.08f;
                return s * Mathf.Exp(-t / 0.022f) * Env(t, 0.0008f);
            });
            float hp = 0;
            shaker = Make("Shaker", sr, 0.11f, (t, r) =>
            {
                // 沙锤：高通白噪声，柔和起音
                float o = r - hp; hp = hp * 0.6f + r * 0.4f;
                return o * 0.5f * Mathf.Exp(-t / 0.03f) * Env(t, 0.006f);
            });
            float bp1 = 0, bp2 = 0;
            snap = Make("Snap", sr, 0.16f, (t, r) =>
            {
                // 轻拍掌：带通噪声 + 一点 200 Hz 的体感
                bp1 += (r - bp1) * 0.35f; bp2 += (bp1 - bp2) * 0.35f;
                float n = (bp1 - bp2) * 2.2f;
                float body = Mathf.Sin(2 * Mathf.PI * 205f * t) * Mathf.Exp(-t / 0.03f) * 0.3f;
                return (n * Mathf.Exp(-t / 0.045f) + body) * Env(t, 0.001f);
            });
            bell = Make("Bell", sr, 1.4f, (t, r) =>
            {
                // 高音铃（G6 + C7 + 一点非整数倍泛音）
                float s = Mathf.Sin(2 * Mathf.PI * 1567.98f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * 2093f * t) * 0.3f + Mathf.Sin(2 * Mathf.PI * 4186f * 1.003f * t) * 0.08f;
                return s * Mathf.Exp(-t / 0.45f) * Env(t, 0.002f) * 0.5f;
            });
            pool = new AudioSource[12];
            for (int i = 0; i < pool.Length; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false; src.spatialBlend = 0f; src.priority = 64;
                pool[i] = src;
            }
        }

        static float Env(float t, float attack) => Mathf.Clamp01(t / attack);

        static AudioClip Make(string name, int sr, float seconds, System.Func<float, float, float> f)
        {
            int n = Mathf.CeilToInt(sr * seconds);
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float r = (float)(rng.NextDouble() * 2 - 1);
                data[i] = f(t, r) * Mathf.Clamp01((seconds - t) / 0.01f);   // 尾部 10 ms 淡出，防爆音
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            for (int i = 0; i < n; i++) data[i] *= 0.9f / peak;
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Play(AudioClip c, float vol, double at, float pitch = 1f)
        {
            var src = pool[next]; next = (next + 1) % pool.Length;
            src.Stop();
            src.clip = c; src.volume = vol * volume; src.pitch = pitch;
            src.PlayScheduled(at);
        }

        void Update()
        {
            var g = director ? director.Game : null;
            if (g == null || g.State != GameState.Playing) { if (g == null || g.State != GameState.Won) scheduledTo = float.NegativeInfinity; return; }
            int combo = g.ComboValue;
            Layer = combo >= LayerCombo[3] ? 3 : combo >= LayerCombo[2] ? 2 : combo >= LayerCombo[1] ? 1 : 0;
            float beat = g.Beat, bps = Mathf.Max(0.1f, g.Bps);
            if (float.IsNegativeInfinity(scheduledTo) || beat < scheduledTo - 2f) scheduledTo = Mathf.Ceil(beat * 2f) / 2f - 0.5f;
            double now = AudioSettings.dspTime;
            float horizon = beat + lookahead * bps;
            while (scheduledTo + 0.5f <= horizon)
            {
                scheduledTo += 0.5f;
                float h = scheduledTo;
                if (h < beat - 0.02f) continue;                 // 已经过了（掉帧）就不补
                double at = now + (h - beat) / bps;
                Hit(g, h, at, bps);
            }
        }

        /// <summary>h = 半拍格点（绝对拍位）。</summary>
        void Hit(GameCore g, float h, double at, float bps)
        {
            float loop = Mathf.Max(1f, g.LoopBeats);
            float inLoop = h - Mathf.Floor(h / loop + 1e-4f) * loop;
            // 在第几小节、小节里第几拍
            var bars = g.Bars;
            int bi = 0;
            for (int i = 0; i < bars.Length; i++) if (bars[i] <= inLoop + 1e-3f) bi = i;
            float pos = inLoop - bars[bi];
            int meter = g.Meter;
            // 弱起小节（第一小节比拍子短）：按它在完整小节里的位置算
            if (bi == 0 && bars.Length > 1 && bars[1] < meter - 0.01f) pos += meter - bars[1];
            bool onBeat = Mathf.Abs(pos - Mathf.Round(pos)) < 0.01f;
            int beatInBar = Mathf.RoundToInt(pos);
            if (meter == 3 || meter == 4) beatInBar %= meter;     // 小节线不规整的谱（拍子不是 3 / 4）：只在每小节第一拍落鼓
            if (onBeat)
            {
                if (beatInBar == 0)
                {
                    Play(kick, 0.62f, at);
                    if (Layer >= 3) Play(bell, 0.18f, at);
                }
                else
                {
                    Play(tick, Layer >= 3 ? 0.3f : 0.22f, at);
                    if (Layer >= 2 && meter == 4 && beatInBar == 2) Play(kick, 0.4f, at);
                    if (Layer >= 2 && meter == 4 && (beatInBar == 1 || beatInBar == 3)) Play(snap, 0.26f, at);
                    if (Layer >= 2 && meter == 3 && beatInBar == 2) Play(snap, 0.2f, at);
                }
            }
            else if (Layer >= 1 && bps <= 3f) Play(shaker, 0.2f, at);
        }

        /// <summary>盖印的闷响（马上）。</summary>
        public void Thump() { if (kick) Play(kick, 0.7f, AudioSettings.dspTime + 0.01, 1.15f); }

        /// <summary>结算滚分的“嗒”声（马上）。</summary>
        public void Tick(float pitch, float vol) { if (tick) Play(tick, vol, AudioSettings.dspTime + 0.01, pitch); }

        /// <summary>连击里程碑：一声高音铃（马上）。</summary>
        public void Chime(float vol = 0.35f) { if (bell) Play(bell, vol, AudioSettings.dspTime + 0.01); }
    }
}
