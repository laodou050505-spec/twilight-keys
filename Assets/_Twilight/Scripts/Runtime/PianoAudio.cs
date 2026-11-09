using System.Collections.Generic;
using UnityEngine;

namespace Twilight
{
    /// <summary>钢琴音源：88 个采样（A0~C8），AudioSource 复音池（默认 32 路）。
    /// Play 按 dspTime 精确排程（复合块的后续音）；时长到了或长按提前松手时淡出；复音满了让最早的音快速淡出。</summary>
    public class PianoAudio : MonoBehaviour
    {
        [Tooltip("下标 = MIDI - 21（A0 … C8）")]
        public AudioClip[] clips = new AudioClip[88];
        public int voices = 32;
        [Range(0f, 1f)] public float volume = 0.85f;

        class Voice { public AudioSource src; public double start, end; public float vel; public int midi; public object owner; public float fade = -1; }
        readonly List<Voice> pool = new();

        void Awake()
        {
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 0f; s.dopplerLevel = 0; s.priority = 64;
                pool.Add(new Voice { src = s });
            }
        }

        public AudioClip Clip(int midi) => midi >= 21 && midi <= 108 ? clips[midi - 21] : null;

        /// <summary>播放一个音：velocity 0..1，duration 秒（之后淡出），delay 秒后发声；owner 用于之后按组停止（长按）。</summary>
        public void Play(int midi, float velocity = 1f, float duration = 3.2f, float delay = 0f, object owner = null)
        {
            var clip = Clip(midi);
            if (!clip || pool.Count == 0) return;
            double now = AudioSettings.dspTime, start = now + Mathf.Max(0f, delay) + 0.005;
            // 空闲的，否则最早开始的
            Voice v = null;
            foreach (var p in pool) if (!p.src.isPlaying && p.fade < 0) { v = p; break; }
            if (v == null) { v = pool[0]; foreach (var p in pool) if (p.start < v.start) v = p; v.src.Stop(); }
            v.src.clip = clip;
            v.src.volume = velocity * volume;
            v.src.PlayScheduled(start);
            v.start = start; v.end = start + Mathf.Min(duration, clip.length); v.vel = velocity; v.midi = midi; v.owner = owner; v.fade = -1;
        }

        /// <summary>让某个 owner 的音（长按块）快速淡出。</summary>
        public void Release(object owner, float time = 0.12f)
        {
            foreach (var v in pool) if (v.owner == owner && v.src.isPlaying) { v.fade = time; v.owner = null; }
        }

        /// <summary>取消还没开始发声的预约音（失败 / 退出）。</summary>
        public void CancelScheduled()
        {
            double now = AudioSettings.dspTime;
            foreach (var v in pool) if (v.start > now) { v.src.Stop(); v.fade = -1; }
        }

        public void StopAll() { foreach (var v in pool) { v.src.Stop(); v.fade = -1; } }

        /// <summary>失败时的低音和弦。</summary>
        public void PlayError() { foreach (var m in new[] { 35, 36, 41 }) Play(m, 1f, 1.6f); }

        void Update()
        {
            double now = AudioSettings.dspTime;
            float dt = Time.unscaledDeltaTime;
            foreach (var v in pool)
            {
                if (!v.src.isPlaying) { v.fade = -1; continue; }
                if (v.fade < 0 && now > v.end - 0.35) v.fade = 0.35f;      // 时长到了：自然收尾
                if (v.fade >= 0)
                {
                    v.src.volume = Mathf.MoveTowards(v.src.volume, 0f, v.vel * volume * dt / Mathf.Max(0.02f, v.fade));
                    if (v.src.volume <= 0.001f) { v.src.Stop(); v.fade = -1; }
                }
            }
        }
    }
}
