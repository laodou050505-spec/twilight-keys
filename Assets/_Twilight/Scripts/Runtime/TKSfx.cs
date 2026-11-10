using UnityEngine;

namespace Twilight
{
    /// <summary>第三十九轮（用户：漏掉音块要有反馈，不然玩家不明白）：程序合成的两个短音效（无版权问题），和八音盒同一种温和的木质感，不刺耳。
    /// - Miss：一声闷闷的木头“咚”（约 170 Hz 衰减 0.12 秒 + 很短的木头拍击噪声，往下滑一点点音高，听着像“落空了”）；
    /// - Wrong：敲到没有音块的轨道 / 错的轨道，一声更轻、更短的“嗒”。</summary>
    public static class TKSfx
    {
        static AudioClip miss, wrong;
        public static AudioClip Miss => miss ? miss : miss = Make("TK_Miss", 170f, 120f, 0.13f, 0.32f, 0.55f, 11);
        public static AudioClip Wrong => wrong ? wrong : wrong = Make("TK_Wrong", 420f, 380f, 0.05f, 0.08f, 0.35f, 7);

        static AudioClip Make(string name, float f0, float f1, float decay, float len, float noiseAmt, int seed)
        {
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            int n = Mathf.CeilToInt(len * sr);
            var d = new float[n];
            var rng = new System.Random(seed);
            float ph = 0f, lp = 0f, peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / len));            // 音高轻轻往下滑
                ph += 2f * Mathf.PI * f / sr;
                float body = Mathf.Sin(ph) * Mathf.Exp(-t / decay) + 0.35f * Mathf.Sin(ph * 2.31f) * Mathf.Exp(-t / (decay * 0.5f));
                float w = (float)rng.NextDouble() * 2f - 1f;
                lp += (w - lp) * 0.35f;                                         // 低通噪声 = 木头拍击，不是尖的“嘶”
                float click = lp * Mathf.Exp(-t / 0.006f) * noiseAmt;
                float v = (body + click) * Mathf.Clamp01(t / 0.001f) * Mathf.Clamp01((len - t) / 0.02f);
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.7f / Mathf.Max(peak, 1e-4f);
            var c = AudioClip.Create(name, n, 1, sr, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
