using System;
using System.Collections.Generic;
using UnityEngine;

namespace Twilight
{
    /// <summary>曲库：Resources/songs.json（由原型 songs.js 导出，全部公有领域曲目）。</summary>
    public static class SongLibrary
    {
        [Serializable]
        public class Song
        {
            public string id, title, subtitle, score;
            /// <summary>大师演奏的完整编曲（和 score 同一段音乐、总拍数相等）；空 = 用 score 自己。</summary>
            public string master;
            /// <summary>classical / romantic / folk / kids（曲目面板分页）。</summary>
            public string category;
            /// <summary>英文曲名、作曲家（英文界面 / 曲目面板）。</summary>
            public string en, composer;
            public int difficulty;
            public float bpm;
            public float[] speeds;
        }

        [Serializable] class File { public Song[] songs; }

        static List<Song> cache;

        public static List<Song> Load()
        {
            if (cache != null) return cache;
            var ta = Resources.Load<TextAsset>("songs");
            var f = JsonUtility.FromJson<File>(ta.text);
            cache = new List<Song>(f.songs);
            foreach (var s in cache) if (s.speeds != null && s.speeds.Length == 0) s.speeds = null;
            return cache;
        }

        public static Song Find(string id) => Load().Find(s => s.id == id);

        static readonly Dictionary<string, string> English = new()
        {
            ["twinkle"] = "Twinkle, Twinkle, Little Star", ["tigers"] = "Frère Jacques", ["ode"] = "Ode to Joy", ["jingle"] = "Jingle Bells",
            ["elise"] = "Für Elise", ["canon"] = "Canon in D", ["auldlangsyne"] = "Auld Lang Syne", ["birthday"] = "Happy Birthday to You",
            ["largo"] = "Going Home (Largo)", ["londonbridge"] = "London Bridge", ["marylamb"] = "Mary Had a Little Lamb",
            ["merryxmas"] = "We Wish You a Merry Christmas", ["minuet"] = "Minuet in G", ["newyear"] = "Happy New Year",
            ["sakura"] = "Sakura Sakura", ["silentnight"] = "Silent Night", ["songbie"] = "Farewell",
        };

        /// <summary>英文曲名（界面中英双语用）。</summary>
        public static string EnglishTitle(string id)
        {
            var s = Find(id);
            if (s != null && !string.IsNullOrEmpty(s.en)) return s.en;
            return English.TryGetValue(id ?? "", out var e) ? e : (s?.subtitle ?? "");
        }

        /// <summary>曲目面板的四类（顺序 = 面板上的分页顺序）。</summary>
        public static readonly string[] Categories = { "classical", "romantic", "folk", "kids" };
        public static Bi CategoryName(string c) => c switch
        {
            "classical" => new Bi("古典", "Classical"),
            "romantic" => new Bi("浪漫", "Romantic"),
            "folk" => new Bi("民歌", "Folk"),
            _ => new Bi("童谣", "Kids & Holiday"),
        };

        /// <summary>按类别、难度排好的全部曲目 id（选歌用）。</summary>
        public static List<string> Ordered()
        {
            var l = new List<Song>(Load());
            l.Sort((a, b) =>
            {
                int ca = System.Array.IndexOf(Categories, a.category ?? "kids"), cb = System.Array.IndexOf(Categories, b.category ?? "kids");
                if (ca < 0) ca = 3; if (cb < 0) cb = 3;
                return ca != cb ? ca.CompareTo(cb) : a.difficulty != b.difficulty ? a.difficulty.CompareTo(b.difficulty) : 0;
            });
            return l.ConvertAll(s => s.id);
        }
    }
}
