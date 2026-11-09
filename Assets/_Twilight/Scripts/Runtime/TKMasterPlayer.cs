using System.Collections.Generic;
using Twilight.Logic;
using UnityEngine;

namespace Twilight
{
    /// <summary>大师演奏：按曲目的大师谱（SongLibrary.Song.master，双手、和弦、分解和弦的完整编曲）用钢琴采样放出来，
    /// 拍位跟 GameCore.Beat 走（和落下的音块严丝合缝）；按 dspTime 提前约 0.12 秒排程（不受帧率抖动影响）。
    /// 每个音都让琴键按下 / 亮起（两个八度装不下的按八度折进来），旋律音亮得更明显。
    /// 玩家可以跟着敲：敲的声音不出（不影响音乐），只在轨道上给“对 / 错”的光效反馈（GameDirector 处理）。
    /// 没有大师谱的曲子：用玩家谱 score 本身（旋律 + 伴奏）放。</summary>
    public class TKMasterPlayer : MonoBehaviour
    {
        public GameDirector director;
        public PianoAudio audio;
        public PianoKeys keys;
        [Range(0, 1)] public float volume = 0.9f;
        public float lookahead = 0.12f;

        struct N { public float beat, len; public int midi; public float vel; public bool melody; }
        readonly List<N> notes = new();
        int next;
        bool active;
        float lastBeat;

        /// <summary>开一首（GameDirector.StartSong 在大师模式下调）。</summary>
        public void Begin(SongLibrary.Song song)
        {
            notes.Clear(); next = 0; active = true; lastBeat = -1e9f;
            var src = !string.IsNullOrEmpty(song.master) ? song.master : song.score;
            List<NoteEvent> ev;
            try { ev = Chart.Parse(src); } catch (System.Exception e) { Debug.LogWarning($"[TK] 大师谱读不了 {song.id}：{e.Message}"); active = false; return; }
            float at = 0f;
            foreach (var e in ev)
            {
                if (e.seq != null && e.seq.Length > 0)
                {
                    for (int g = 0; g < e.seq.Length; g++)
                    {
                        var grp = e.seq[g];
                        float end = g + 1 < e.seq.Length ? e.seq[g + 1].at : e.dur;
                        Add(at + grp.at, Mathf.Max(0.25f, e.dur - grp.at), grp.notes, !grp.soft && g == 0, grp.soft);
                        _ = end;
                    }
                }
                else Add(at, e.dur, e.notes, true, false);
                at += e.dur;
            }
            notes.Sort((a, b) => a.beat.CompareTo(b.beat));
            Debug.Log($"[TK] 大师演奏 {song.id}：{notes.Count} 个音（{(string.IsNullOrEmpty(song.master) ? "用玩家谱" : "大师谱")}）");
        }

        void Add(float beat, float len, int[] ns, bool hasMelody, bool soft)
        {
            if (ns == null) return;
            for (int i = 0; i < ns.Length; i++)
            {
                bool mel = hasMelody && i == 0;
                notes.Add(new N { beat = beat, len = len, midi = ns[i], vel = (mel ? 0.95f : 0.62f) * (soft ? 0.75f : 1f), melody = mel });
            }
        }

        public void Stop() { active = false; notes.Clear(); next = 0; }

        void Update()
        {
            var g = director ? director.Game : null;
            if (!active || g == null || !g.Master) return;
            if (g.State != GameState.Playing) return;
            float bps = Mathf.Max(0.05f, g.Bps), beat = g.Beat;
            // 暂停后继续 / 往回跳：重新找起点
            if (beat < lastBeat - 0.5f) { next = 0; while (next < notes.Count && notes[next].beat < beat) next++; }
            lastBeat = beat;
            float horizon = beat + lookahead * bps;
            while (next < notes.Count && notes[next].beat <= horizon)
            {
                var n = notes[next++];
                float delay = Mathf.Max(0f, (n.beat - beat) / bps);
                float sec = n.len / bps;
                if (audio) audio.Play(n.midi, n.vel * volume, Mathf.Max(0.5f, sec * 1.3f + 0.2f), delay, this);
                StartCoroutine(Key(n, delay, sec));
            }
        }

        System.Collections.IEnumerator Key(N n, float delay, float sec)
        {
            if (delay > 0) yield return new WaitForSeconds(delay);
            if (!keys) yield break;
            var color = n.melody ? new Color(1f, 0.86f, 0.55f) : new Color(0.75f, 0.82f, 1f);
            if (n.melody) keys.NoteOn(n.midi, color, Mathf.Clamp(sec * 0.85f, 0.08f, 0.6f));
            else keys.Glow(n.midi, color, 0.45f);
        }
    }
}
