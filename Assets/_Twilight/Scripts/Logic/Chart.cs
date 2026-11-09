using System;
using System.Collections.Generic;
using System.Globalization;

namespace Twilight.Logic
{
    /// <summary>谱面里的一组同时发声的音（复合块里的一组）。at：相对块起点的拍位；soft：没有旋律音（整组轻弹）。</summary>
    public struct NoteGroup
    {
        public int[] notes;
        public float at;
        public bool soft;
    }

    /// <summary>一个钢琴块对应的乐谱事件：notes 是第一组（旋律 + 伴奏），dur 是拍数；seq 非空表示复合块（点一次按拍位依次发声）。</summary>
    public class NoteEvent
    {
        public int[] notes;
        public float dur;
        public NoteGroup[] seq;
    }

    /// <summary>乐谱解析（移植自原型 songs.js）。
    /// 记谱：空格分隔，每个记号一个块。"E4" 1 拍；"E4:2" 2 拍；"E4+C3" 同时发声；"|" 小节线忽略；
    /// "E4+C3,G3@0.5,C5+E3@1:2" 复合块；某组以 "~" 开头表示没有旋律音。</summary>
    public static class Chart
    {
        public const int MidiMin = 21, MidiMax = 108;
        static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        static readonly Dictionary<string, string> FlatToSharp = new()
        {
            ["Db"] = "C#", ["Eb"] = "D#", ["Gb"] = "F#", ["Ab"] = "G#", ["Bb"] = "A#", ["Cb"] = "B", ["Fb"] = "E", ["E#"] = "F", ["B#"] = "C",
        };

        public static int ParseNote(string name)
        {
            name = name.Trim();
            if (name.Length < 2) throw new FormatException("无法解析音名: " + name);
            int i = 1;
            string letter = char.ToUpperInvariant(name[0]).ToString();
            if (i < name.Length && (name[i] == '#' || name[i] == 'b')) { letter += name[i]; i++; }
            int octave = int.Parse(name.Substring(i), CultureInfo.InvariantCulture);
            if (FlatToSharp.TryGetValue(letter, out var fixedName))
            {
                if (letter == "Cb") octave -= 1;
                if (letter == "B#") octave += 1;
                letter = fixedName;
            }
            int idx = Array.IndexOf(Names, letter);
            if (idx < 0) throw new FormatException("无法解析音名: " + name);
            return (octave + 1) * 12 + idx;
        }

        public static bool IsBlack(int midi) => Names[midi % 12].Length == 2;
        public static string MidiToName(int midi) => Names[midi % 12] + (midi / 12 - 1);

        public static List<NoteEvent> Parse(string score)
        {
            var events = new List<NoteEvent>();
            foreach (var raw in score.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw == "|") continue;
                int colon = raw.LastIndexOf(':');
                string body = colon < 0 ? raw : raw.Substring(0, colon);
                float dur = colon < 0 ? 1f : float.Parse(raw.Substring(colon + 1), CultureInfo.InvariantCulture);
                var parts = new List<NoteGroup>();
                foreach (var p in body.Split(','))
                {
                    var g = ParsePart(p);
                    if (g.notes.Length > 0) parts.Add(g);
                }
                if (parts.Count == 0) continue;
                var ev = new NoteEvent { notes = parts[0].notes, dur = dur };
                if (parts.Count > 1 || parts[0].soft) ev.seq = parts.ToArray();
                events.Add(ev);
            }
            return events;
        }

        /// <summary>每个小节从第几拍开始（按 "|" 切；第一小节可以是弱起）。节奏伴奏用它找强拍。</summary>
        public static float[] Bars(string score)
        {
            var bars = new List<float> { 0f };
            float beat = 0f;
            foreach (var raw in score.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw == "|") { if (beat > bars[^1] + 1e-3f) bars.Add(beat); continue; }
                int colon = raw.LastIndexOf(':');
                float dur = colon < 0 ? 1f : float.Parse(raw.Substring(colon + 1), CultureInfo.InvariantCulture);
                string body = colon < 0 ? raw : raw.Substring(0, colon);
                bool any = false;
                foreach (var p in body.Split(',')) if (ParsePart(p).notes.Length > 0) { any = true; break; }
                if (any) beat += dur;
            }
            return bars.ToArray();
        }

        /// <summary>最常见的小节长度（拍）：3 = 三拍子，4 = 四拍子。</summary>
        public static int Meter(float[] bars, float total)
        {
            var count = new Dictionary<int, int>();
            for (int i = 0; i < bars.Length; i++)
            {
                int len = Mathf_Round((i + 1 < bars.Length ? bars[i + 1] : total) - bars[i]);
                if (len <= 0) continue;
                count.TryGetValue(len, out int n); count[len] = n + 1;
            }
            int best = 4, bestN = 0;
            foreach (var kv in count) if (kv.Value > bestN) { best = kv.Key; bestN = kv.Value; }
            return best;
        }

        static int Mathf_Round(float x) => (int)Math.Floor(x + 0.5f);

        static NoteGroup ParsePart(string s)
        {
            bool soft = s.StartsWith("~");
            if (soft) s = s.Substring(1);
            var at = 0f;
            int a = s.IndexOf('@');
            if (a >= 0) { at = float.Parse(s.Substring(a + 1), CultureInfo.InvariantCulture); s = s.Substring(0, a); }
            var notes = new List<int>();
            foreach (var n in s.Split('+'))
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                int m = ParseNote(n);
                if (m >= MidiMin && m <= MidiMax) notes.Add(m);
            }
            return new NoteGroup { notes = notes.ToArray(), at = at, soft = soft };
        }

        /// <summary>无尽即兴：五声音阶随机游走，偶尔配低音（移植自原型 improvGenerator）。</summary>
        public static IEnumerable<NoteEvent> Improv(int seed)
        {
            long s = seed % 2147483647; if (s <= 0) s += 2147483646;
            float Rand() { s = s * 16807 % 2147483647; return s / 2147483647f; }
            int[] penta = { 0, 2, 4, 7, 9 }, roots = { 48, 45, 41, 43 };
            int degree = 7, beat = 0;
            while (true)
            {
                degree += (int)(Rand() * 5) - 2;
                degree = Math.Max(3, Math.Min(14, degree));
                int midi = 60 + degree / 5 * 12 + penta[degree % 5] - 12;
                var notes = new List<int> { midi };
                if (beat % 4 == 0) notes.Add(roots[beat / 4 % roots.Length]);
                float dur = Rand() < 0.2f ? 0.5f : Rand() < 0.12f ? 2f : 1f;
                beat++;
                yield return new NoteEvent { notes = notes.ToArray(), dur = dur };
            }
        }
    }
}
