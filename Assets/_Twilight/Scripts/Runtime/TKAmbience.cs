using UnityEngine;

namespace Twilight
{
    /// <summary>村子的环境音（程序合成，没有版权问题；在音频线程里实时生成，不占贴图 / 安装包）：
    /// · 晚风：低通粉噪声，强弱慢慢起伏；
    /// · 湖水：带通噪声的轻拍，偶尔一下“哗”；
    /// · 蟋蟀：几只，各自 4.2–5 kHz 的短促颤音（一串 3–5 下），节奏略有不同，互相错开；
    /// · 青蛙：偶尔两三声低沉的“咕”；
    /// · 远处的猫头鹰：很久一次，“呜——呜呜”。
    /// 跟着世界铺开慢慢变丰富：开局只有很轻的风（还在屋里），窗户消散后整个夜晚都出来了。
    /// 立体声：每种声音固定一个左右位置（窗外右边湖、左边田野），不跟头转（窗外世界本身就是固定的）。</summary>
    [RequireComponent(typeof(AudioSource))]
    public class TKAmbience : MonoBehaviour
    {
        [Range(0, 1)] public float volume = 0.55f;
        [Tooltip("0 = 只在屋里（很轻的风），1 = 整个夜晚")] [Range(0, 1)] public float openness;
        [Tooltip("入夜程度（虫鸣、蛙叫越晚越多）")] [Range(0, 1)] public float night = 1f;

        public RevealDirector reveal;
        public TwilightSky sky;

        int sr = 48000;
        System.Random rng = new(7);
        float vol, open, nightV;                           // 主线程写、音频线程读（float 写读是原子的）
        // 风
        float wb0, wb1, wb2, wlp, wSlow, wTarget, wTimer;
        // 水
        float aLp, aBp1, aBp2, lapEnv, lapTimer, lapGain;
        // 蟋蟀
        struct Cricket { public float freq, phase, pan, timer, env, chirpPhase; public int left; public float rate; }
        Cricket[] crickets;
        // 青蛙、猫头鹰
        float frogTimer, frogEnv, frogPhase, frogFreq, frogPan; int frogLeft;
        float owlTimer, owlT = -1f, owlPhase;

        void Awake()
        {
            sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            crickets = new Cricket[5];
            for (int i = 0; i < crickets.Length; i++)
                crickets[i] = new Cricket { freq = 4200f + 180f * i + (float)rng.NextDouble() * 120f, pan = Mathf.Lerp(-0.8f, 0.8f, i / 4f), timer = (float)rng.NextDouble() * 2f, rate = 11f + i * 0.9f };
            frogTimer = 3f; owlTimer = 25f; lapTimer = 1f; wTarget = 0.6f;
            var src = GetComponent<AudioSource>();
            src.playOnAwake = true; src.loop = true; src.spatialBlend = 0f; src.volume = 1f;
            // 一段静音 clip 让 OnAudioFilterRead 跑起来（真正的声音在过滤器里生成）
            src.clip = AudioClip.Create("ambience", sr, 2, sr, false);
            src.Play();
        }

        void Update()
        {
            float target = openness;
            if (reveal) target = Mathf.Max(target, Mathf.Clamp01(reveal.Progress * 1.4f));
            open = Mathf.MoveTowards(open, target, Time.deltaTime * 0.3f);
            nightV = night;
            vol = volume;
        }

        float Noise() => (float)rng.NextDouble() * 2f - 1f;

        void OnAudioFilterRead(float[] data, int ch)
        {
            if (crickets == null || rng == null) return;          // 运行中重新编译后字段会丢
            float dt = 1f / sr;
            float o = open, v = vol, nt = nightV;
            float windAmt = Mathf.Lerp(0.12f, 0.32f, o), waterAmt = 0.55f * o, bugAmt = 0.22f * o * Mathf.Lerp(0.4f, 1f, nt);
            for (int i = 0; i < data.Length; i += ch)
            {
                float l = 0f, r = 0f;

                // ---- 晚风：粉噪声（Paul Kellet 简化）→ 低通，强弱慢慢起伏 ----
                float w = Noise();
                wb0 = 0.99765f * wb0 + w * 0.099046f; wb1 = 0.963f * wb1 + w * 0.2965164f; wb2 = 0.57f * wb2 + w * 1.0526913f;
                float pink = (wb0 + wb1 + wb2 + w * 0.1848f) * 0.11f;
                wTimer -= dt; if (wTimer <= 0f) { wTimer = 2f + (float)rng.NextDouble() * 4f; wTarget = 0.35f + (float)rng.NextDouble() * 0.65f; }
                wSlow += (wTarget - wSlow) * dt * 0.4f;
                wlp += (pink - wlp) * 0.04f;
                float wind = wlp * wSlow * windAmt * 2.2f;
                l += wind; r += wind * 0.9f;

                // ---- 湖水：带通噪声（约 500 Hz–1.5 kHz）一下一下轻拍，在右边 ----
                if (waterAmt > 0.001f)
                {
                    lapTimer -= dt;
                    if (lapTimer <= 0f) { lapTimer = 0.7f + (float)rng.NextDouble() * 1.6f; lapGain = 0.4f + (float)rng.NextDouble() * 0.6f; }
                    float phaseIn = Mathf.Clamp01(1f - lapTimer / 0.5f);
                    lapEnv += ((lapTimer > 0.35f ? 0.15f : lapGain) - lapEnv) * dt * 6f;
                    float n = Noise();
                    aBp1 += (n - aBp1) * 0.18f; aBp2 += (aBp1 - aBp2) * 0.18f; aLp += (aBp1 - aBp2 - aLp) * 0.5f;
                    float water = aLp * lapEnv * waterAmt * 1.6f;
                    l += water * 0.45f; r += water;
                    _ = phaseIn;
                }

                // ---- 蟋蟀：每只隔一会儿叫一串（3–5 下颤音），每下约 25 ms ----
                if (bugAmt > 0.001f)
                {
                    for (int c = 0; c < crickets.Length; c++)
                    {
                        ref var k = ref crickets[c];
                        k.timer -= dt;
                        if (k.timer <= 0f && k.left == 0) { k.left = 3 + rng.Next(3); k.chirpPhase = 0f; }
                        if (k.left > 0)
                        {
                            k.chirpPhase += dt * k.rate;
                            float frac = k.chirpPhase - Mathf.Floor(k.chirpPhase);
                            k.env = frac < 0.45f ? Mathf.Sin(frac / 0.45f * Mathf.PI) : 0f;
                            if (k.chirpPhase >= k.left) { k.left = 0; k.env = 0f; k.timer = 0.6f + (float)rng.NextDouble() * 1.8f; }
                        }
                        k.phase += k.freq * dt; if (k.phase > 1f) k.phase -= 1f;
                        float s = Mathf.Sin(k.phase * 2f * Mathf.PI) * k.env * bugAmt * 0.35f;
                        l += s * (1f - k.pan) * 0.5f; r += s * (1f + k.pan) * 0.5f;
                    }
                }

                // ---- 青蛙：隔几秒，两三声低沉的“咕”（130–180 Hz，带一点谐波），在右边湖边 ----
                if (o > 0.3f)
                {
                    frogTimer -= dt;
                    if (frogTimer <= 0f && frogLeft == 0) { frogLeft = 2 + rng.Next(2); frogFreq = 130f + (float)rng.NextDouble() * 50f; frogPan = 0.3f + (float)rng.NextDouble() * 0.5f; frogEnv = 0f; }
                    if (frogLeft > 0)
                    {
                        frogEnv += dt;
                        float e = frogEnv < 0.16f ? Mathf.Sin(frogEnv / 0.16f * Mathf.PI) : 0f;
                        if (frogEnv > 0.28f) { frogLeft--; frogEnv = 0f; if (frogLeft == 0) frogTimer = 4f + (float)rng.NextDouble() * 7f; }
                        frogPhase += frogFreq * (1f - frogEnv * 0.6f) * dt;
                        float s = (Mathf.Sin(frogPhase * 2f * Mathf.PI) + 0.4f * Mathf.Sin(frogPhase * 4f * Mathf.PI) + 0.2f * Mathf.Sin(frogPhase * 6f * Mathf.PI)) * e * 0.05f * o * nt;
                        l += s * (1f - frogPan) * 0.5f; r += s * (1f + frogPan) * 0.5f;
                    }
                }

                // ---- 猫头鹰：很久一次，“呜——呜呜”（约 380 Hz，有点下滑），在左后远处 ----
                if (o > 0.6f)
                {
                    owlTimer -= dt;
                    if (owlTimer <= 0f && owlT < 0f) { owlT = 0f; owlTimer = 28f + (float)rng.NextDouble() * 30f; }
                    if (owlT >= 0f)
                    {
                        owlT += dt;
                        float e = 0f, f = 380f;
                        if (owlT < 0.7f) { e = Mathf.Sin(owlT / 0.7f * Mathf.PI); f = 400f - owlT * 40f; }
                        else if (owlT > 0.95f && owlT < 1.25f) { e = Mathf.Sin((owlT - 0.95f) / 0.3f * Mathf.PI) * 0.8f; f = 370f; }
                        else if (owlT > 1.35f && owlT < 1.75f) { e = Mathf.Sin((owlT - 1.35f) / 0.4f * Mathf.PI) * 0.8f; f = 360f; }
                        else if (owlT >= 1.75f) owlT = -1f;
                        owlPhase += f * dt;
                        float s = Mathf.Sin(owlPhase * 2f * Mathf.PI) * e * 0.035f * nt;
                        l += s * 0.8f; r += s * 0.25f;
                    }
                }

                l *= v; r *= v;
                if (ch >= 2) { data[i] = l; data[i + 1] = r; for (int q = 2; q < ch; q++) data[i + q] = 0; }
                else data[i] = (l + r) * 0.5f;
            }
        }
    }
}
