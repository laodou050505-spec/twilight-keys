using System.Collections.Generic;
using UnityEngine;

namespace Twilight
{
    /// <summary>八音盒音色（程序合成，不用采样、没有版权问题）：钢片梳齿被钉拨一下——
    /// 基音为主，悬臂梁的第二、第三振型（约 6.27 倍、17.55 倍）短促地闪一下，开头一点点“嗒”的拨片声，指数衰减，高音衰减更快。
    /// 每个音第一次用到时生成并缓存。</summary>
    public static class TKMusicBoxSynth
    {
        static readonly Dictionary<int, AudioClip> cache = new();

        public static AudioClip Clip(int midi)
        {
            midi = Mathf.Clamp(midi, 48, 108);
            if (cache.TryGetValue(midi, out var c) && c) return c;
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            float f = 440f * Mathf.Pow(2f, (midi - 69) / 12f);
            float decay = 1.7f * Mathf.Pow(440f / f, 0.35f);              // 秒：低音长、高音短
            float len = Mathf.Min(2.6f, decay * 3.2f);
            int n = Mathf.CeilToInt(len * sr);
            var data = new float[n];
            var rng = new System.Random(midi);
            float noise = 0f, peak = 0f;
            // 一点点失谐（两片梳齿不会完全一样）：主音 + 很弱的失谐副本，听起来更“金属”
            float detune = 1.0016f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float att = Mathf.Clamp01(t / 0.0015f);
                float v = Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t / decay)
                        + 0.18f * Mathf.Sin(2 * Mathf.PI * f * detune * t + 0.7f) * Mathf.Exp(-t / (decay * 0.8f))
                        + 0.22f * Mathf.Sin(2 * Mathf.PI * f * 6.27f * t) * Mathf.Exp(-t / 0.09f)
                        + 0.06f * Mathf.Sin(2 * Mathf.PI * f * 17.55f * t) * Mathf.Exp(-t / 0.025f);
                // 拨片声：几毫秒的高通噪声
                float w = (float)rng.NextDouble() * 2 - 1;
                noise = w - noise * 0.6f;
                v += 0.12f * noise * Mathf.Exp(-t / 0.003f);
                v *= att;
                data[i] = v;
                peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            float g = 0.85f / Mathf.Max(1e-4f, peak);
            for (int i = 0; i < n; i++) data[i] *= g;
            c = AudioClip.Create("musicbox_" + midi, n, 1, sr, false);
            c.SetData(data, 0);
            cache[midi] = c;
            return c;
        }
    }
}
