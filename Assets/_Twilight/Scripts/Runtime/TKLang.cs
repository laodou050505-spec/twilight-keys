using UnityEngine;

namespace Twilight
{
    /// <summary>界面语言（设置里切换，存在 PlayerPrefs）：中文 / English。第一次启动跟系统语言。
    /// 界面一次只显示一种语言（之前中英两行并排，太挤）；标题“黄昏琴村 / Twilight Keys”是招牌，始终两种都写。</summary>
    public static class TKLang
    {
        const string Key = "tk_lang";
        static int cur = -1;

        public static bool En
        {
            get
            {
                if (cur < 0)
                {
                    var sl = Application.systemLanguage;
                    bool zh = sl == SystemLanguage.Chinese || sl == SystemLanguage.ChineseSimplified || sl == SystemLanguage.ChineseTraditional;
                    cur = PlayerPrefs.HasKey(Key) ? PlayerPrefs.GetInt(Key) : (zh ? 0 : 1);
                }
                return cur == 1;
            }
            set
            {
                int v = value ? 1 : 0;
                if (v == cur) return;
                cur = v; PlayerPrefs.SetInt(Key, v); PlayerPrefs.Save(); Version++;
            }
        }

        /// <summary>每切换一次加 1（界面据此刷新）。</summary>
        public static int Version { get; private set; }

        public static string Pick(string zh, string en) => En ? en : zh;

        /// <summary>新手教学：没教过、或在设置里打开了，下一次开始前先教。</summary>
        /// <summary>琴的高度微调（米，正 = 抬高；设置里每次 ±2 cm，范围 ±20 cm）。第二十二轮：让玩家按自己的身材调。</summary>
        public static float PianoLift
        {
            get => PlayerPrefs.GetFloat("tk_piano_lift", 0f);
            set { PlayerPrefs.SetFloat("tk_piano_lift", Mathf.Clamp(value, -0.2f, 0.2f)); PlayerPrefs.Save(); Version++; }
        }

        /// <summary>第三十轮（用户：新手教学只出现一次，之后别再提示）：演奏里的教学提示（“敲停在琴键上的那一块就开始”“长条按住别抬”）
        /// 只在第一首（教学练习曲，或关了教学时的第一首）里说，弹完一首就记下，之后都不说。</summary>
        public static bool Coached
        {
            get => PlayerPrefs.GetInt("tk_coached", PlayerPrefs.GetInt("tk_tutorial_v2", 1) == 0 ? 1 : 0) == 1;   // 老存档：教学已经关了 = 学过了
            set { PlayerPrefs.SetInt("tk_coached", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        /// <summary>“大师演奏”的说明只说一次。</summary>
        public static bool MasterHinted
        {
            get => PlayerPrefs.GetInt("tk_master_hinted", 0) == 1;
            set { PlayerPrefs.SetInt("tk_master_hinted", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public static bool Tutorial
        {
            // 第二十五轮（用户：第一次玩默认开新手教学）：换一个新键名，已装过的机器（之前学完被自动关掉的）也重新从“开”开始
            get => PlayerPrefs.GetInt("tk_tutorial_v2", 1) == 1;
            set { PlayerPrefs.SetInt("tk_tutorial_v2", value ? 1 : 0); PlayerPrefs.Save(); }
        }
    }
}
