using System.Collections.Generic;
using Twilight.Logic;
using UnityEngine;

namespace Twilight
{
    /// <summary>封面 + 选歌 + 设置 + 暂停菜单：一只手绘木质八音盒浮在面前。
    /// 出现：从小变大落到面前 → 掀开盒盖（盖内写着“黄昏琴村 / Twilight Keys”）→ 黄铜立柱从盒里伸起来，托着小浮岛（风车、橡树、小屋、湖和小船）慢慢转。
    /// 八音盒：前面木台上有一套机芯——带钉的黄铜滚筒跟着曲子转，钢梳齿被拨到时轻轻颤；音色是程序合成的钢片拨奏声，
    /// 一直循环放当前这首（放完停一小会儿再从头来），不会只放一遍。
    /// 按钮（指尖戳）：铭牌两边 ◀ ▶ 换歌；下排“开始”（暂停时“继续”，设置里“完成”）、中间齿轮“设置”、“退出”（按一次变“再按退出”，3 秒内再按才退出）。
    ///   手指靠近时按钮先微微弹出来；戳到按下去、咔哒一声；戳过一次要先退开才能再戳，另有冷却（防误触、连按）。
    /// 设置：铭牌翻过去显示设置项，◀ ▶ 改值，齿轮换下一项，“完成”回去。设置项：语言（中文 / English）、新手教学（开 / 关）。
    /// 小岛：周围一圈金色箭头环提示“可以转”；手指伸到小岛旁边拨一下就跟着转（带惯性、棘轮轻响），松开后慢慢回到八音盒的转速。不能拿走。
    /// 文字：和盒盖标题同一套字（中文马善政毛笔字、英文 Nunito），一次只显示一种语言。
    /// 跟随（舒适）：不锁在头上，转头超过 25° 或离得太远才慢慢滑到新的正前方；正在拨小岛时不动。
    /// 开始：小岛落回、立柱缩回、合盖、盒子缩小消失（交给光河从天上流下来）。编辑器：← → 选歌，回车开始，S 设置，Esc 退出。</summary>
    public class TKMusicBox : MonoBehaviour
    {
        public Transform head;
        public HandTapInput hands;
        [Header("模型（由搭建脚本接好）")]
        public Transform body, lidPivot, island, spindle, drum, btnPrev, btnNext, btnStart, btnSettings, btnExit;
        public Transform[] teeth;
        public MeshRenderer spinHint;
        [Tooltip("舞台光（盒下、盒里、小岛背后），随开盖亮起")] public MeshRenderer[] glows;
        public Material glowMaterial, letterMaterial, fireflyMaterial;
        [Tooltip("盒盖上方提示牌：象牙瓷面 + 金边")] public Material plateMaterial, rimMaterial;
        [Tooltip("盖内标题牌材质（中英两张贴图，按界面语言换）")] public Material titleMat;
        public Texture2D titleZhTex, titleEnTex;
        [Header("摆放")]
        public float distance = 0.55f, below = 0.25f, recenterAngle = 25f, scale = 1.3f;   // 第三十二轮（用户：八音盒往上挪一点）：眼下 30 → 25 cm
        /// <summary>第三十二轮（用户：琴不能和弹出来的八音盒叠在一起）：盒底最低只能到这个世界高度（琴键顶面 + 间隙，会话摆好琴后每帧给）。</summary>
        [System.NonSerialized] public float minBottomY = float.NegativeInfinity;
        public float lidOpen = 105f;
        // 第三十四轮（用户：盖子打开后还有缝，看着廉价）：拱盖绕盒沿后面一点翻开，下沿弧线两边翘起，和盒沿之间空出两块月牙缝。
        // 参考图里盖子是“从盒子里立起来”的——开盖后半段再让盖子往下沉、往后退一点，下沿落到盒沿后面，缝被盒沿挡住；合上时原样扣回。
        [Tooltip("开盖到底时合页额外移动（Body 本地：y 向下沉、z 向后退）；默认不沉")] public Vector3 lidSinkOpen = Vector3.zero;
        [Tooltip("小岛（Body 空间）：收起 / 升起的高度、前后位置、大小；立柱顶在升起高度下面")]
        public float islandLow = 0.03f, islandHigh = 0.118f, islandZ = 0.012f, islandScale = 0.6f;   // 第十三轮（参考图）：小岛压低一点，盖内的村庄画露在它上方
        [Header("声音")]
        [Range(0, 1)] public float volume = 0.42f;

        public string[] playlist;
        public int songIndex;
        /// <summary>按下“开始 / 继续”（或编辑器回车）。由 TKSession 消费后清掉。</summary>
        public bool StartPressed { get; set; }
        /// <summary>暂停菜单模式：“开始”键写“继续”。</summary>
        public bool ResumeMode { get; set; }
        /// <summary>暂停菜单里按了“主页”（TKSession 读完清掉）。</summary>
        public bool HomePressed { get; set; }
        public bool Visible => root && root.gameObject.activeSelf;
        public bool Closing => phase == Phase.Closing;
        public bool Busy => phase == Phase.Opening || phase == Phase.Closing;
        /// <summary>正在拨小岛（这时别把手势当成摆琴、敲琴）。</summary>
        public bool Handling => spinBy >= 0;
        /// <summary>第三十一轮（用户：退出时容易碰到别的地方）：手正在按八音盒上的钮 / 曲目卡开着 / 刚按完——这时敲到琴键不算“开始”。</summary>
        public bool Reaching => Handling || PanelOpen || settings || TableAlignRequested || Time.time < reachUntil;   // 第三十三轮：设置开着、正在对齐桌子时，手按桌面也不算“开始”
        float reachUntil = -1f;
        // 退出改成“按住不放 1 秒”（原来 3 秒内按两次：手指要先退开再戳，常常按不出去）
        const float ExitHoldSeconds = 1.0f;
        float exitHold; bool exitHolding;
        static readonly Bi SayExitHow = new("按住“退出”不放，就退出游戏", "hold Exit to quit the game");
        static readonly Bi SayExitHold = new("继续按住…… 松手就取消", "keep holding… let go to cancel");

        enum Phase { Hidden, Opening, Open, Closing }
        Phase phase = Phase.Hidden;
        float t;
        Transform root, plaque;
        Vector3 anchorPos; bool anchored;

        /// <summary>一行字：中文 / 英文各一个 TextMesh（字体不同、材质不同），按当前语言只显示一个。</summary>
        class Line
        {
            public TextMesh zh, en;
            string sz, se;
            public void Set(string z, string e) { if (z != sz) { zh.text = sz = z; } if (e != se) { en.text = se = e; } }
            public void Set(Bi b) => Set(b.zh, b.en);
            public void Alpha(Color c, float a)
            {
                TKFont.Alpha(TKLang.En ? en : zh, c, a);
                TKFont.Alpha(TKLang.En ? zh : en, c, 0f);
            }
        }
        TextMesh titleZh, titleEn;
        Line plaqueLine, startLine, exitLine, hintLine;
        static readonly Vector3 HintAt = new(0, 0.378f, 0.02f);   // 盒盖上方（第二十八轮：抬高 4 cm，不压盖内标题）
        DotText flies, bar;
        Material hintMat;
        Quaternion lidClosed, drumBase;
        Vector3 lidRestPos;
        float[] glowBase; Material[] glowMats;
        Quaternion[] toothBase;
        float[] toothShake;
        Transform[] buttons;
        Vector2[] btnHalf;
        readonly Vector3[] btnRest = new Vector3[5];
        readonly float[] btnPress = new float[5], btnCool = new float[5], btnHover = new float[5];
        readonly bool[] btnArmed = { true, true, true, true, true };
        Bi hint = Bi.None; float progress = -1f; int barCount = -1;
        bool startEnabled;
        float exitArmedUntil = -1f;
        const int BarDots = 24;

        // 设置
        bool settings; int settingItem;
        const int SettingCount = 4;   // 语言、新手教学、琴的高度、对齐桌子
        // ---- 第三十三轮（用户：有自己桌子的玩家想对着桌子弹，琴有时是歪的，要摆正在桌面上）----
        /// <summary>设置里按了“对齐桌子”的 ▶：会话开始看两手食指尖是否按在桌边上。</summary>
        public bool TableAlignRequested { get; set; }
        /// <summary>设置里按了“对齐桌子”的 ◀（已对齐时）：取消对齐，琴回到按身体估的位置。</summary>
        public bool TableAlignCancel { get; set; }
        /// <summary>会话告诉八音盒：现在琴是不是对着桌子摆的（铭牌显示“已对齐”）。</summary>
        public bool TableAligned { get; set; }
        /// <summary>对齐过程中的提示（优先于其他提示）和进度（0..1，−1 不显示）。</summary>
        [System.NonSerialized] public Bi AlignHint = Bi.None;
        [System.NonSerialized] public float AlignProgress = -1f;
        /// <summary>对齐状态变了：设置停在这一项时，铭牌翻成新文字。</summary>
        public void RefreshSetting() { if (settings && settingItem == 3) Flip(SettingBi()); }
        // 换歌 / 翻牌
        float flipT = 1f; Bi pending;
        float hopT = 1f;
        // 转小岛
        int spinBy = -1; float spinAngle, spinVel = BaseSpin, spinTick; Vector3 spinLast; float handNear;
        const float BaseSpin = 14f;   // 八音盒自己转的速度（度 / 秒）

        static readonly Color Engrave = new(0.17f, 0.1f, 0.05f);    // 铜牌上的刻字（深褐）
        static readonly Color Gold = new(1f, 0.84f, 0.5f), Cream = new(1f, 0.95f, 0.84f), Ink = new(0.24f, 0.11f, 0.03f),
            Firefly = new(1f, 0.86f, 0.42f), FireflyB = new(1f, 0.97f, 0.62f);

        void Awake()
        {
            root = transform;
            buttons = new[] { btnPrev, btnNext, btnStart, btnSettings, btnExit };
            btnHalf = new[] { new Vector2(0.0085f, 0.0085f), new Vector2(0.0085f, 0.0085f), new Vector2(0.02f, 0.0083f), new Vector2(0.0085f, 0.0085f), new Vector2(0.02f, 0.0083f) };
            for (int i = 0; i < buttons.Length; i++) if (buttons[i]) btnRest[i] = buttons[i].localPosition;
            if (lidPivot) { lidClosed = lidPivot.localRotation; lidRestPos = lidPivot.localPosition; }
            if (drum) drumBase = drum.localRotation;
            if (teeth != null) { toothBase = new Quaternion[teeth.Length]; toothShake = new float[teeth.Length]; for (int i = 0; i < teeth.Length; i++) toothBase[i] = teeth[i].localRotation; }
            if (spinHint) { hintMat = new Material(spinHint.sharedMaterial); spinHint.sharedMaterial = hintMat; }

            // 字：中文一律马善政毛笔字（和标题同一套），英文一律 Nunito
            var brushGlow = TKFont.Mat(glowMaterial, 0, TKFont.Brush);
            var roundGlow = TKFont.Mat(glowMaterial, 0, TKFont.Round);
            var brushInk = TKFont.Mat(letterMaterial, 0, TKFont.Brush);
            var roundInk = TKFont.Mat(letterMaterial, 0, TKFont.Round);

            // 盒盖内面板上的标题（招牌，中英都写；盖子关着时朝下，掀开后正对玩家；放在面板靠上，不被小岛挡住）
            if (lidPivot)
            {
                // 刻在盖内那块拉丝铜牌上（Blender：铭牌 13 × 5 cm，下表面在合页下 0.0213）：深色刻字，贴在铜牌前 0.5 mm，不再发光、不压在木纹上
                var face = Quaternion.LookRotation(Vector3.up, Vector3.back);
                titleZh = Text("TitleZh", lidPivot, brushInk, 0.0029f, TKFont.Brush, new Vector3(0, 0.0054f, -0.1578f), face);
                titleZh.text = "";   // 第十四轮：标题改成牌面贴图（金箔字），不再用浮在上面的文字
                titleEn = Text("TitleEn", lidPivot, roundInk, 0.00115f, TKFont.Round, new Vector3(0, 0.0054f, -0.1454f), face);
                titleEn.text = "";
            }
            // 正面铭牌：歌名 / 设置项（刻上去的深色字；翻牌绕铭牌中线转）
            plaque = new GameObject("Plaque").transform;
            plaque.SetParent(body, false);
            plaque.localPosition = new Vector3(0, 0.068f, -0.0979f);   // 圆鼓瓷盒：铭牌正面 Blender y = −0.09552、高 0.068
            plaqueLine = new Line
            {
                zh = Text("PlaqueZh", plaque, brushInk, 0.00155f, TKFont.Brush, new Vector3(0, 0.0004f, 0), Quaternion.identity),
                en = Text("PlaqueEn", plaque, roundInk, 0.00095f, TKFont.Round, Vector3.zero, Quaternion.identity),
            };
            // 下排两个长圆钮上的字（按钮节点在转了 180° 的模型下面：本地 +Z 朝外，字要转回来）
            var back = Quaternion.Euler(0, 180, 0);
            if (btnStart) startLine = new Line
            {
                zh = Text("StartZh", btnStart, brushInk, 0.00145f, TKFont.Brush, new Vector3(0, 0.0003f, 0.0016f), back),
                en = Text("StartEn", btnStart, roundInk, 0.00105f, TKFont.Round, new Vector3(0, 0, 0.0016f), back),
            };
            if (btnExit) exitLine = new Line
            {
                zh = Text("ExitZh", btnExit, brushInk, 0.00145f, TKFont.Brush, new Vector3(0, 0.0003f, 0.0016f), back),
                en = Text("ExitEn", btnExit, roundInk, 0.00105f, TKFont.Round, new Vector3(0, 0, 0.0016f), back),
            };
            // 盒子前下方的提示（暖光字，往后仰一点正对视线）+ “手掌放平”的萤火虫进度
            // 提示挪到盒盖上方（原来在盒子前下方，字正好压在琴键上）
            var tilt = Quaternion.Euler(8, 0, 0);
            // 第二十八轮：提示改成瓷牌上的深褐字（发光字在透视画面里看不清）
            hintLine = new Line
            {
                zh = Text("HintZh", root, brushInk, 0.0022f, TKFont.Brush, HintAt, tilt),
                en = Text("HintEn", root, roundInk, 0.0019f, TKFont.Round, HintAt, tilt),
            };
            hintPlate = HintPart("HintPlate", plateMaterial, 0.004f, tilt);
            hintRim = HintPart("HintRim", rimMaterial ? rimMaterial : plateMaterial, 0.0058f, tilt);
            bar = new DotText("Progress", root, fireflyMaterial, BarDots + 6, false, Firefly, FireflyB, 0.004f, 0.006f, 17) { roam = new Vector2(0.12f, 0.02f), idle = 0 };
            bar.root.localPosition = HintAt + new Vector3(0, -0.028f, 0);
            // 小岛周围慢慢飘的萤火虫（少一点，别抢戏）
            flies = new DotText("Fireflies", island ? island : root, fireflyMaterial, 22, false, Firefly, FireflyB, 0.0022f, 0.0035f, 23) { roam = new Vector2(0.13f, 0.08f), idle = 18 };
            flies.root.localPosition = new Vector3(0, 0.05f, 0);
            flies.SetPoints(new List<Vector3>());
            root.gameObject.SetActive(false);
        }

        TextMesh Text(string name, Transform parent, Material m, float size, Font f, Vector3 at, Quaternion rot)
        {
            var tm = TKFont.Text(name, parent, m, size, f);
            tm.transform.localPosition = at; tm.transform.localRotation = rot;
            return tm;
        }

        /// <summary>弹出并打开（canStart：琴已经摆好，“开始”可以按）。</summary>
        public void Show(bool canStart)
        {
            startEnabled = canStart;
            if (phase == Phase.Opening || phase == Phase.Open) return;
            root.gameObject.SetActive(true);
            phase = Phase.Opening; t = 0f; anchored = false; StartPressed = false; settings = false; PanelOpen = false;
            for (int i = 0; i < buttons.Length; i++) { btnArmed[i] = false; btnCool[i] = 0.6f; }
            barCount = -1; exitArmedUntil = -1f; exitHold = 0f; exitHolding = false; spinBy = -1; spinVel = BaseSpin; flipT = 1f; hopT = 1f;
            pending = SongBi(); plaqueLine.Set(pending);
            BuildTune(); tuneClock = -1.5f; lastDsp = -1; pendingShake.Clear();          // 八音盒旋律延后到盒子飘到面前、快开盖时再放
        }

        public void SetCanStart(bool v) => startEnabled = v;

        /// <summary>合上并消失。</summary>
        public void Hide()
        {
            if (phase == Phase.Hidden || phase == Phase.Closing) return;
            phase = Phase.Closing; t = 0f; spinBy = -1; settings = false; PanelOpen = false; TableAlignRequested = false;
            flies.Scatter(1.2f); bar.Scatter(0.8f);
        }

        public void SetHint(Bi h, float prog) { hint = h; progress = prog; }

        public string SongId => playlist != null && playlist.Length > 0 ? playlist[((songIndex % playlist.Length) + playlist.Length) % playlist.Length] : "twinkle";

        Bi SongBi()
        {
            var s = SongLibrary.Find(SongId);
            return new Bi(s != null ? s.title : "", SongLibrary.EnglishTitle(SongId));
        }

        Bi SettingBi()
        {
            if (settingItem == 0) return new Bi("语言 · 中文", "Language · English");
            if (settingItem == 3)
                return TableAlignRequested ? new Bi("对齐桌子 · 对齐中", "Align to table · …")
                     : TableAligned ? new Bi("对齐桌子 · 已对齐", "Align to table · done")
                     : new Bi("对齐桌子 · ▶ 开始", "Align to table · ▶");
            if (settingItem == 2)
            {
                int cm = Mathf.RoundToInt(TKLang.PianoLift * 100f);
                string v = cm == 0 ? "标准" : (cm > 0 ? $"高 {cm} cm" : $"低 {-cm} cm");
                string ve = cm == 0 ? "default" : (cm > 0 ? $"+{cm} cm" : $"{cm} cm");
                return new Bi("琴的高度 · " + v, "Piano height · " + ve);
            }
            bool on = TKLang.Tutorial;
            return new Bi("新手教学 · " + (on ? "开" : "关"), "Tutorial · " + (on ? "On" : "Off"));
        }

        void Flip(Bi to) { pending = to; flipT = 0f; }

        /// <summary>跳到某首（曲目面板点选）：和 ◀ ▶ 换歌一样的效果。</summary>
        public void SelectSong(string id)
        {
            if (playlist == null) return;
            int i = System.Array.IndexOf(playlist, id);
            if (i < 0 || i == songIndex) return;
            songIndex = i; Flip(SongBi());
            hopT = 0f; flies.Kick(1.0f);
            BuildTune(); tuneClock = -0.35f; lastDsp = -1; pendingShake.Clear();
        }

        /// <summary>大师演奏模式（曲目面板切换）：“开始”键写“欣赏”。</summary>
        public bool MasterMode { get; set; }
        /// <summary>曲目抽屉开着（点铭牌开 / 关；选了歌、按开始、进设置时自动收起）。</summary>
        public bool PanelOpen { get; set; }
        float panelClosedAt = -9f;

        /// <summary>所有按钮都要等手指先退开才能再按（卡片开合、换模式之后用）。</summary>
        void DisarmAll()
        {
            for (int i = 0; i < btnArmed.Length; i++) btnArmed[i] = false;
            plaqueArmed = false;
            exitArmedUntil = -1f; exitHold = 0f; exitHolding = false;
        }
        public bool PanelToggled { get; set; }
        bool plaqueArmed = true; float plaqueCool, plaqueHover;
    // ---- 第二十八轮（用户：封面上的歌要和教学歌一样；教学没做完点曲目单要提示先跟教学；开头要告诉玩家歌名能点）----
    /// <summary>新手教学还没做完：铭牌停在教学那首，换歌 / 曲目单都不开，只提示先跟着教学弹。</summary>
    public bool Locked { get; set; }
    /// <summary>玩家打开过曲目单（以后不再提示“点歌名能选歌”）。</summary>
    public static bool PanelKnown => PlayerPrefs.GetInt("tk_panel_known", 0) == 1;
    static readonly Bi SayLocked = new("先跟着教学弹完这首\n学完就能自己选歌了", "finish this tutorial song first\nthen pick any song you like");
    float nudgeUntil = -1f;
    void Nudge() { nudgeUntil = Time.time + 2.6f; hintShake = 0.35f; }

    Transform hintPlate, hintRim; string hintSized; float hintShow;
    static readonly Color HintInk = new(0.2f, 0.11f, 0.05f);

    Transform HintPart(string n, Material m, float z, Quaternion rot)
    {
        var g = new GameObject(n);
        g.transform.SetParent(root, false);
        g.transform.localRotation = rot;
        g.transform.localPosition = HintAt + rot * new Vector3(0, 0, z);
        g.AddComponent<MeshFilter>();
        var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return g.transform;
    }

    void SizeHintPlate()
    {
        var t = TKLang.En ? hintLine.en : hintLine.zh;
        var b = t.GetComponent<MeshRenderer>().localBounds;
        float w = Mathf.Max(0.06f, b.size.x + 0.034f), h = Mathf.Max(0.026f, b.size.y + 0.02f);
        var rot = t.transform.localRotation;
        if (hintPlate) { hintPlate.GetComponent<MeshFilter>().sharedMesh = ScoreHUD.Pill(w, h, 0.003f); hintPlate.localPosition = HintAt + rot * new Vector3(b.center.x, b.center.y, 0.004f); }
        if (hintRim) { hintRim.GetComponent<MeshFilter>().sharedMesh = ScoreHUD.Pill(w + 0.003f, h + 0.003f, 0.002f); hintRim.localPosition = HintAt + rot * new Vector3(b.center.x, b.center.y, 0.0058f); }
    }
        /// <summary>铭牌被点（手指戳 / 键盘 Tab / 测试）：开关曲目抽屉。</summary>
        public void TogglePanel()
        {
            if (settings) return;
            if (Locked && !PanelOpen) { Nudge(); return; }                   // 教学还没做完：不开曲目单
            if (!PanelOpen) { PlayerPrefs.SetInt("tk_panel_known", 1); PlayerPrefs.Save(); }
            PanelOpen = !PanelOpen; Tick(true);
        }

        /// <summary>指尖戳铭牌（同按钮规则：进到前面 6 mm、要先退开才能再戳）。</summary>
        void PlaquePoke(float dt)
        {
            if (!plaque) return;
            plaqueCool -= dt;
            var n = -body.forward; var c = plaque.position;
            float hx = 0.055f * scale, hy = 0.008f * scale;
            bool inside = false, far = true, near = false;
            foreach (var (tip, hand) in Tips())
            {
                if (hand == spinBy) continue;
                var d = tip - c; float along = Vector3.Dot(d, n);
                float dx = Mathf.Abs(Vector3.Dot(d, body.right)), dy = Mathf.Abs(Vector3.Dot(d, body.up));
                bool over = dx < hx + 0.008f && dy < hy + 0.008f;
                if (over && along < 0.006f && along > -0.03f) inside = true;
                if (over && along < 0.06f && along > -0.03f) near = true;
                if (!(along > 0.025f || dx > hx + 0.025f || dy > hy + 0.025f)) far = false;
            }
            plaqueHover = Mathf.MoveTowards(plaqueHover, near ? 1f : 0f, dt * 6f);
            if (far) plaqueArmed = true;
            if (inside && plaqueArmed && plaqueCool <= 0f) { plaqueArmed = false; plaqueCool = 0.45f; TogglePanel(); }
        }
        /// <summary>盒子是否完全打开（曲目面板跟着出现）。</summary>
        public bool IsOpen => phase == Phase.Open;
        /// <summary>铭牌的世界位置（曲目面板从这里展开）。</summary>
        public Transform PlaqueTransform => plaque;
        /// <summary>正在设置里（曲目面板收起）。</summary>
        public bool InSettings => settings;
        /// <summary>设置里改了琴的高度（TKSession 看到后马上重新摆琴，然后清掉）。</summary>
        public bool PianoLiftChanged { get; set; }

        /// <summary>换一首：翻铭牌、小岛跳一下、萤火虫散一下、八音盒从头放新歌。</summary>
        void Step(int d)
        {
            if (playlist == null || playlist.Length == 0) return;
            if (Locked) { Nudge(); return; }                                // 教学还没做完：先弹教学这首
            songIndex = (songIndex + d + playlist.Length) % playlist.Length;
            Flip(SongBi());
            hopT = 0f; flies.Kick(1.0f);
            BuildTune(); tuneClock = -0.35f; lastDsp = -1; pendingShake.Clear();
        }

        void Update()
        {
            if (!root.gameObject.activeSelf || flies == null) return;     // 运行中重新编译后运行时对象会丢，直接跳过
            if (!head && Camera.main) head = Camera.main.transform;
            float dt = Time.deltaTime;
            t += dt;
            TKFont.Sync();
            if (!Handling) Follow(dt);
            // 选曲卡盖在正面时，后面的按钮不响应；卡片收起后再等 0.9 秒、并且手指要先退开，按钮才重新生效
            // （11-04 真机“闪退”：点卡片选歌 → 卡片收起 → 手指继续往前推 → 戳到后面的“退出”）
            if (PanelOpen) { panelClosedAt = Time.time; DisarmAll(); }
            if (phase == Phase.Open && !PanelOpen && Time.time - panelClosedAt > 0.9f) { Buttons(dt); PlaquePoke(dt); IslandSpin(dt); }
            else spinBy = -1;
            Animate(dt);
            KeyboardInput();
            Music(dt);
            FadeVoices(dt);
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        static float Back(float x) { x = Mathf.Clamp01(x); const float c = 1.6f; return 1f + (c + 1f) * Mathf.Pow(x - 1f, 3) + c * Mathf.Pow(x - 1f, 2); }

        void Animate(float dt)
        {
            // 时间线（秒）：0..0.6 盒子弹出；0.5..1.4 开盖；1.1..2.6 立柱伸起、小岛升起
            float pop, lid, rise, textA;
            if (phase == Phase.Closing)
            {
                rise = 1f - Ease(t / 0.6f);
                lid = 1f - Ease((t - 0.45f) / 0.5f);
                pop = 1f - Ease((t - 0.9f) / 0.45f);
                textA = 1f - Ease(t / 0.5f);
                if (t > 1.4f) { phase = Phase.Hidden; root.gameObject.SetActive(false); StopVoices(); return; }
            }
            else
            {
                pop = Back(t / 0.6f);
                lid = Ease((t - 1.2f) / 0.9f);                          // 等盒子飘到面前、停稳再开盖
                rise = Ease((t - 1.8f) / 1.5f);
                textA = Ease((t - 1.6f) / 0.8f);
                if (phase == Phase.Opening && t > 3.5f) phase = Phase.Open;
            }
            body.localScale = Vector3.one * scale * Mathf.Max(0.001f, pop);
            body.localPosition = new Vector3(0, Mathf.Sin(Time.time * 1.3f) * 0.003f, 0);
            if (lidPivot)
            {
                lidPivot.localRotation = Quaternion.AngleAxis(lidOpen * lid, Vector3.right) * lidClosed;
                float sink = Ease((lid - 0.55f) / 0.45f);                   // 先转起来，转过一大半再沉下去（避免盖沿蹭到盒沿）
                lidPivot.localPosition = lidRestPos + lidSinkOpen * sink;
            }
            if (glows != null)
                for (int g = 0; g < glows.Length; g++)
                {
                    if (!glows[g]) continue;
                    float k = g == 0 ? pop : g == 1 ? lid : rise;
                    glows[g].enabled = k > 0.01f;
                    if (glowBase == null) { glowBase = new float[glows.Length]; for (int q = 0; q < glows.Length; q++) glowBase[q] = glows[q].sharedMaterial.GetFloat("_Strength"); glowMats = new Material[glows.Length]; for (int q = 0; q < glows.Length; q++) { glowMats[q] = new Material(glows[q].sharedMaterial); glows[q].sharedMaterial = glowMats[q]; } }
                    glowMats[g].SetFloat("_Strength", glowBase[g] * Mathf.Clamp01(k) * (0.9f + 0.1f * Mathf.Sin(Time.time * 1.4f + g)));
                }

            // 小岛：立在立柱上跟着八音盒转；换歌时跳一下
            if (spinBy < 0)
            {
                spinVel = Mathf.Lerp(spinVel, BaseSpin, 1f - Mathf.Exp(-dt * 0.7f));
                spinAngle += spinVel * dt;
            }
            hopT = Mathf.MoveTowards(hopT, 1f, dt / 0.6f);
            float hop = Mathf.Sin(hopT * Mathf.PI) * 0.01f;
            float sc = Mathf.Lerp(0.25f, islandScale, rise);
            float y = Mathf.Lerp(islandLow, islandHigh, rise) + hop;
            if (island)
            {
                island.gameObject.SetActive(rise > 0.01f);
                island.localPosition = new Vector3(0, y, Mathf.Lerp(0f, islandZ, rise));
                island.localRotation = Quaternion.Euler(0, spinAngle, 0);
                island.localScale = Vector3.one * sc;
            }
            if (spindle)
            {
                // 立柱顶托住小岛底尖（岛底尖在草地下 0.1166 × 缩放）；柱底在盒底 0.032，柱长 0.06
                float tip = y - hop - 0.104f * sc;                     // 实测岛底尖在草地下约 0.108 × 缩放：柱顶插进岛底一点，不留缝
                float k = Mathf.Clamp01((tip - 0.032f) / 0.06f);
                spindle.gameObject.SetActive(k > 0.05f);
                spindle.localScale = new Vector3(1f, 1f, Mathf.Max(0.001f, k));        // 模型本地 Z = Blender Z（竖直）
            }
            if (spinHint)
            {
                spinHint.enabled = rise > 0.95f && phase == Phase.Open;
                spinHint.transform.localPosition = new Vector3(0, y - 0.006f, islandZ);
                spinHint.transform.localScale = Vector3.one * 0.105f * sc;        // 环半径 = 小岛半径（0.09）稍外一点
                handNear = Mathf.MoveTowards(handNear, NearIsland() ? 1f : 0f, dt * 4f);
                hintMat.SetFloat("_Glow", Mathf.Lerp(0.45f + 0.15f * Mathf.Sin(Time.time * 2f), 1.2f, Mathf.Max(handNear, spinBy >= 0 ? 1f : 0f)));
                hintMat.SetFloat("_Phase", spinAngle / 360f);
            }
            if (titleZh) { TKFont.Alpha(titleZh, Engrave, textA * 0.72f); TKFont.Alpha(titleEn, Engrave, textA * 0.6f); }
            if (titleMat && titleZhTex && titleEnTex) { var want = TKLang.En ? titleEnTex : titleZhTex; if (titleMat.GetTexture("_MainTex") != want) titleMat.SetTexture("_MainTex", want); }   // 英文模式：标题也是英文   // 刻字：不闪、不呼吸，就静静刻在铜牌上

            // 铭牌翻牌：转到一半（侧面朝人）时换字；语言切了也要刷新
            if (flipT < 1f)
            {
                float before = flipT;
                flipT = Mathf.MoveTowards(flipT, 1f, dt / 0.4f);
                if (before < 0.5f && flipT >= 0.5f) plaqueLine.Set(pending);
            }
            plaque.localRotation = Quaternion.Euler(flipT < 1f ? (flipT < 0.5f ? flipT * 180f : (flipT - 1f) * 180f) : 0f, 0, 0);
            // 铭牌可以点（打开曲目抽屉）：手指靠近时整块微微往外弹，提示“这里能按”
            plaque.localPosition = new Vector3(0, 0.068f, -0.0979f - 0.0015f * plaqueHover);
            plaqueLine.Alpha(Ink, pop > 0.5f ? 1f : 0f);

            float pa = Mathf.Clamp01(pop);
            if (startLine != null)
            {
                startLine.Set(settings ? new Bi("完成", "Done") : ResumeMode ? new Bi("继续", "Resume") : MasterMode ? new Bi("欣赏", "Listen") : new Bi("开始", "Play"));
                float s = settings || startEnabled ? 0.82f + 0.18f * Mathf.Sin(Time.time * 3f) : 0.3f;
                startLine.Alpha(Engrave, Mathf.Max(s, 0.92f) * pa);   // 象牙白瓷钮上写深褐字（参考图）
            }
            if (exitLine != null)
            {
                exitLine.Set(ResumeMode ? new Bi("主页", "Home") : new Bi("退出", "Exit"));
                exitLine.zh.characterSize = 0.00145f;
                exitLine.Alpha(Engrave, (exitHolding ? 0.75f + 0.25f * Mathf.Sin(Time.time * 8f) : 0.92f) * pa);
            }
            flies.visible = Mathf.Max(rise, phase == Phase.Closing ? 1f - t : 0f); flies.Update(dt);

            // 提示 + “手掌放平”进度
            float prog = exitHold > 0.02f ? exitHold / ExitHoldSeconds : !AlignHint.Empty ? AlignProgress : progress;
            int count = prog >= 0f ? Mathf.RoundToInt(Mathf.Clamp01(prog) * BarDots) : 0;
            if (count != barCount && phase != Phase.Closing)
            {
                barCount = count;
                var pts = new List<Vector3>();
                for (int i = 0; i < count; i++) pts.Add(new Vector3((i / (float)(BarDots - 1) - 0.5f) * 0.2f, 0, 0));
                bar.SetPoints(pts);
            }
            bar.visible = textA; bar.Update(dt);
            bool exitTip = !ResumeMode && !settings && buttons.Length > 4 && btnHover[4] > 0.5f;
            var showHint = exitHold > 0.02f ? SayExitHold : !AlignHint.Empty ? AlignHint : exitTip ? SayExitHow
                : settings && settingItem == 3 ? (TableAligned ? new Bi("▶ 重新对齐 · ◀ 取消对齐 · 齿轮换下一项", "▶ align again  ·  ◀ undo  ·  gear: next") : new Bi("按 ▶，再把两手食指按在桌子靠你的边上\n也可以双手捏住琴两边的圆钮直接拖", "press ▶, then rest both index fingers on the table's near edge\nor pinch the two knobs beside the piano and drag"))
                : settings ? new Bi("两边的小钮修改 · 齿轮换下一项", "side buttons change  ·  gear: next item") : Time.time < nudgeUntil ? SayLocked : hint;
            hintLine.Set(showHint);
            string cur = TKLang.En ? showHint.en : showHint.zh;
            if (cur != hintSized) { hintSized = cur; SizeHintPlate(); }
            float ha = textA * (PanelOpen ? 0f : 1f) * (string.IsNullOrEmpty(cur) ? 0f : 1f);   // 选曲面板打开时它在同一块地方，先藏起来
            hintShow = Mathf.MoveTowards(hintShow, ha, dt * 3f);
            hintLine.Alpha(HintInk, Mathf.Clamp01(hintShow * 1.5f - 0.4f));
            float he = hintShow * hintShow * (3f - 2f * hintShow);
            foreach (var pt in new[] { hintPlate, hintRim }) if (pt) { pt.gameObject.SetActive(he > 0.01f); var psc = pt.localScale; psc.x = Mathf.Max(0.01f, he); pt.localScale = psc; }

            // 按钮：手指靠近先弹出一点，按下去再弹回来（本地 +Z 朝外）
            for (int i = 0; i < buttons.Length; i++)
            {
                if (!buttons[i]) continue;
                btnPress[i] = Mathf.MoveTowards(btnPress[i], 0f, dt * 5f);
                float press = Mathf.Sin(btnPress[i] * Mathf.PI * 0.5f);
                buttons[i].localPosition = btnRest[i] + Vector3.forward * (0.0018f * btnHover[i] * (1f - press) - 0.005f * press);
            }
            // 梳齿被拨后轻轻颤一下
            if (teeth != null)
                for (int i = 0; i < teeth.Length; i++)
                {
                    toothShake[i] = Mathf.MoveTowards(toothShake[i], 0f, dt * 4f);
                    float a = toothShake[i] * 6f * Mathf.Sin(Time.time * 70f);
                    var tr = teeth[i];
                    tr.localRotation = Quaternion.Inverse(tr.parent.rotation) * Quaternion.AngleAxis(a, body.right) * tr.parent.rotation * toothBase[i];
                }
        }

        List<(Vector3 tip, int hand)> Tips()
        {
            var l = new List<(Vector3, int)>(2);
            if (hands)
            {
                if (hands.Left != null) l.Add((hands.Left.indexTip, 0));
                if (hands.Right != null) l.Add((hands.Right.indexTip, 1));
            }
            return l;
        }

        /// <summary>指尖戳按钮：指尖进到按钮正面前 6 mm 以内、落在按钮范围（外扩 1 cm）里；
        /// 戳过一次要先退开（离正面 2.5 cm 以上或移出范围）才能再戳，另有 0.45 秒冷却。正在拨小岛的那只手不算。</summary>
        void Buttons(float dt)
        {
            var tips = Tips();
            var n = -body.forward;
            // 第三十一轮（用户：退出时容易碰到别的地方）：下排“开始 / 齿轮 / 退出”挨得近，感应范围又各往外扩了 1 cm，
            // 一根手指能同时“进”两颗钮。现在先算每颗钮离指尖多近，同一时刻只按最近的那一颗。
            int best = -1; float bestScore = 9f; bool exitTouch = false;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (!buttons[i]) continue;
                btnCool[i] -= dt;
                var c = buttons[i].parent.TransformPoint(btnRest[i]);
                float hx = btnHalf[i].x * scale, hy = btnHalf[i].y * scale;
                bool inside = false, far = true, near = false; float score = 9f;
                foreach (var (tip, hand) in tips)
                {
                    if (hand == spinBy) continue;
                    var d = tip - c;
                    float along = Vector3.Dot(d, n);
                    float dx = Mathf.Abs(Vector3.Dot(d, body.right)), dy = Mathf.Abs(Vector3.Dot(d, body.up));
                    bool over = dx < hx + 0.01f && dy < hy + 0.01f;
                    if (over && along < 0.006f && along > -0.03f) { inside = true; score = Mathf.Min(score, dx / (hx + 0.01f) + dy / (hy + 0.01f)); }
                    if (over && along < 0.06f && along > -0.03f) near = true;
                    if (!(along > 0.025f || dx > hx + 0.025f || dy > hy + 0.025f)) far = false;
                    // 退出长按：按进去以后手指可以稍微晃（往外 2 cm 内、范围外扩 1.5 cm 内都算还按着）
                    if (i == 4 && along < 0.02f && along > -0.04f && dx < hx + 0.015f && dy < hy + 0.015f) exitTouch = true;
                }
                btnHover[i] = Mathf.MoveTowards(btnHover[i], near ? 1f : 0f, dt * 6f);
                if (near) reachUntil = Time.time + 0.8f;               // 手在钮附近：之后 0.8 秒内敲到琴键不算“开始”
                if (far) btnArmed[i] = true;
                if (inside && btnArmed[i] && btnCool[i] <= 0f && score < bestScore) { best = i; bestScore = score; }
            }
            // 退出：按住不放 1 秒（暂停菜单里这颗是“主页”，还是一按就回）
            bool exitMode = !ResumeMode && !settings && buttons.Length > 4 && buttons[4];
            if (exitMode && (exitHolding ? exitTouch : best == 4))
            {
                if (!exitHolding) { exitHolding = true; btnArmed[4] = false; Click(0); }
                exitHold += dt;
                btnPress[4] = Mathf.Max(btnPress[4], 0.7f);              // 钮一直按下去
                reachUntil = Time.time + 0.8f;
                if (exitHold >= ExitHoldSeconds) { exitHold = 0f; exitHolding = false; Quit(); }
                return;                                                 // 按着退出时别的钮都不响应
            }
            if (exitHolding) { exitHolding = false; btnCool[4] = 0.45f; }
            exitHold = Mathf.MoveTowards(exitHold, 0f, dt * 3f);
            if (best >= 0 && !(exitMode && best == 4))
            {
                btnArmed[best] = false; btnCool[best] = 0.45f;
                // 按下的那一刻，旁边几颗也先“上锁”，要等手指退开才能按（防止一戳连按两颗）
                for (int k = 0; k < buttons.Length; k++) if (k != best) btnCool[k] = Mathf.Max(btnCool[k], 0.3f);
                Press(best);
            }
        }

        /// <summary>按下一个按钮（0 ◀、1 ▶、2 开始 / 继续 / 完成、3 设置、4 退出）：手指戳、键盘、测试都走这里。</summary>
        public void Press(int i)
        {
            if (i < 0 || i >= buttons.Length) return;
            btnPress[i] = 1f;
            Click(i == 2 || i == 4 ? 0 : 1);
            switch (i)
            {
                case 0:
                case 1:
                    if (settings) ChangeSetting(i == 0 ? -1 : 1);
                    else Step(i == 0 ? -1 : 1);
                    break;
                case 2:
                    PanelOpen = false;
                    if (settings) { settings = false; TableAlignRequested = false; Flip(SongBi()); }
                    else if (startEnabled) StartPressed = true;
                    else Kick();
                    break;
                case 3:
                    PanelOpen = false;
                    if (!settings) { settings = true; settingItem = 0; }
                    else if (++settingItem >= SettingCount) { settings = false; TableAlignRequested = false; Flip(SongBi()); break; }
                    Flip(SettingBi());
                    break;
                case 4:
                    PanelOpen = false;
                    if (ResumeMode) { HomePressed = true; break; }   // 暂停中：这个键是“回主页”（不退出游戏）
                    Quit();                                          // 手指戳走 Buttons() 里的“按住 1 秒”；这里是键盘 Esc / 测试直接退出
                    break;
            }
        }

        void ChangeSetting(int d)
        {
            if (settingItem == 0) TKLang.En = !TKLang.En;
            else if (settingItem == 2) { TKLang.PianoLift += d * 0.02f; PianoLiftChanged = true; }   // ◀ 低 2 cm、▶ 高 2 cm，琴马上跟着动
            else if (settingItem == 3)
            {
                // ▶ 开始对齐（两手食指按桌边）；◀ 对齐中 = 不对了，已对齐 = 取消对齐
                if (d > 0) { TableAlignRequested = true; TableAlignCancel = false; }
                else if (TableAlignRequested) TableAlignRequested = false;
                else if (TableAligned) TableAlignCancel = true;
            }
            else TKLang.Tutorial = !TKLang.Tutorial;
            // 语言一换，铭牌直接换成新语言的同一项
            Flip(SettingBi());
        }

        void Quit()
        {
            Debug.Log("[TK] 退出游戏");
            StopVoices();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>琴还没摆好就按“开始”：提示晃一下。</summary>
        void Kick() { hintShake = 0.35f; }
        float hintShake;

        bool NearIsland()
        {
            if (!island) return false;
            float R = 0.09f * island.lossyScale.x;
            foreach (var (tip, _) in Tips())
            {
                var v = tip - island.position; float h = Vector3.Dot(v, body.up);
                float r = (v - body.up * h).magnitude;
                if (r < R * 1.8f && h > -0.08f && h < 0.14f) return true;
            }
            return false;
        }

        /// <summary>拨小岛：指尖伸到小岛旁边（离转轴 1.5 cm 到 1.3 倍半径、草地上下）就“搭上”，手指绕着转轴挪多少，小岛就转多少；
        /// 松开后带着惯性，慢慢回到八音盒自己的转速。每转过 30° 一声轻轻的棘轮响。</summary>
        void IslandSpin(float dt)
        {
            if (!island) return;
            var up = body.up;
            float R = 0.09f * island.lossyScale.x;
            int touching = -1; Vector3 tv = default;
            foreach (var (tip, hand) in Tips())
            {
                var v = tip - island.position; float h = Vector3.Dot(v, up);
                var hv = v - up * h; float r = hv.magnitude;
                if (r > 0.015f && r < R * 1.3f && h > -0.04f && h < 0.08f && (touching < 0 || hand == spinBy)) { touching = hand; tv = hv; }
            }
            if (touching < 0) { spinBy = -1; return; }
            if (spinBy != touching) { spinBy = touching; spinLast = tv; return; }
            float d = Vector3.SignedAngle(spinLast, tv, up);
            spinLast = tv;
            spinAngle += d;
            spinVel = Mathf.Lerp(spinVel, d / Mathf.Max(1e-3f, dt), 0.35f);
            spinTick += Mathf.Abs(d);
            if (spinTick > 30f) { spinTick = 0f; Note(100, 0.07f); }
        }

        void KeyboardInput()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || phase != Phase.Open) return;
            if (kb.leftArrowKey.wasPressedThisFrame) Press(0);
            if (kb.rightArrowKey.wasPressedThisFrame) Press(1);
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Press(2);
            if (kb.sKey.wasPressedThisFrame) Press(3);
            if (kb.lKey.wasPressedThisFrame) TogglePanel();
            if (kb.escapeKey.wasPressedThisFrame) Press(4);
        }

        /// <summary>有惯性的跟随：转头超过 recenterAngle 或离开太远才重新对准，平滑滑过去。</summary>
        void Follow(float dt)
        {
            if (!head) return;
            var f = head.forward; f.y = 0;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            var want = head.position + f * distance + Vector3.down * below;
            want.y = Mathf.Max(want.y, minBottomY);                  // 琴调高了：八音盒跟着抬，永远悬在琴键上方
            var cur = anchorPos - head.position; cur.y = 0;
            bool far = !anchored || Vector3.Angle(cur, f) > recenterAngle || Mathf.Abs(cur.magnitude - distance) > 0.3f;
            if (far && phase != Phase.Closing)
            {
                anchorPos = want;
                if (!anchored) { root.position = want; anchored = true; var ff = want - head.position; ff.y = 0; root.rotation = Quaternion.LookRotation(ff, Vector3.up); }
            }
            else anchorPos.y = Mathf.Lerp(anchorPos.y, want.y, 1f - Mathf.Exp(-dt * 0.8f));
            float k = 1f - Mathf.Exp(-dt * 2f);
            root.position = Vector3.Lerp(root.position, anchorPos, k);
            var face = anchorPos - head.position; face.y = 0;
            if (face.sqrMagnitude > 1e-6f) root.rotation = Quaternion.Slerp(root.rotation, Quaternion.LookRotation(face, Vector3.up), k);
            if (hintShake > 0f)
            {
                hintShake = Mathf.MoveTowards(hintShake, 0f, dt);
                float s = Mathf.Sin(Time.time * 60f) * 0.004f * hintShake / 0.35f;
                hintLine.zh.transform.localPosition = hintLine.en.transform.localPosition = HintAt + new Vector3(s, 0, 0);
            }
        }

        // ---------- 八音盒：整首旋律循环播放；滚筒转一圈 = 整首；每个音拨一根梳齿 ----------
        readonly List<AudioSource> voices = new();
        readonly List<(float at, int midi, float vel, int tooth)> tune = new();
        float tuneLen, tuneClock; int tuneNext;
        double lastDsp = -1;
        const float Lookahead = 0.12f;
        readonly List<(float at, int tooth)> pendingShake = new();
        readonly List<float> voiceFade = new();       // 每个声部：>0 = 正在淡出（剩余秒数）
        const float LoopGap = 1.4f;

        void BuildTune()
        {
            tune.Clear(); tuneNext = 0; tuneLen = 0f;
            var s = SongLibrary.Find(SongId);
            if (s == null) return;
            List<NoteEvent> ev;
            try { ev = Chart.Parse(s.score); } catch { return; }
            float beat = 60f / Mathf.Max(40f, s.bpm) * 1.08f, at = 0f;     // 比原曲稍慢一点，八音盒的味道
            int top = 0, lo = 999;
            foreach (var e in ev)
            {
                if (e.notes != null) foreach (var m in e.notes) { top = Mathf.Max(top, m); lo = Mathf.Min(lo, m); }
                if (e.seq != null) foreach (var g in e.seq) if (g.notes != null) foreach (var m in g.notes) { top = Mathf.Max(top, m); lo = Mathf.Min(lo, m); }
            }
            if (top == 0) return;
            int shift = 12 * Mathf.RoundToInt((91 - top) / 12f);                 // 最高音挪到 G6 附近（八音盒的音区）
            int lo2 = lo + shift, hi2 = top + shift;
            void Add(float when, int[] notes, bool soft)
            {
                if (notes == null || notes.Length == 0) return;
                int mel = 0; foreach (var m in notes) mel = Mathf.Max(mel, m);
                foreach (var m in notes)
                {
                    int mm = m + shift;
                    while (mm < 60) mm += 12;                                     // 太低的伴奏音翻上来（八音盒没有低音）
                    float vel = (m == mel ? 0.9f : 0.42f) * (soft ? 0.7f : 1f);
                    int tooth = teeth != null && teeth.Length > 0 ? Mathf.Clamp(Mathf.RoundToInt((mm - lo2) / (float)Mathf.Max(1, hi2 - lo2) * (teeth.Length - 1)), 0, teeth.Length - 1) : -1;
                    tune.Add((when, mm, vel, tooth));
                }
            }
            foreach (var e in ev)
            {
                if (e.seq != null && e.seq.Length > 0) foreach (var g in e.seq) Add(at + g.at * beat, g.notes, g.soft);
                else Add(at, e.notes, false);
                at += Mathf.Max(0.25f, e.dur) * beat;
            }
            tune.Sort((a, b) => a.at.CompareTo(b.at));
            tuneLen = at;
        }

        void Music(float dt)
        {
            if (tune.Count == 0 || phase == Phase.Closing) return;
            // 用音频时钟走（不跟着画面帧率抖），并且提前 0.12 秒把下一批音按 dspTime 精确排程：
            // 之前每个音在"刚好过了那一帧"才 Play，帧率一抖节奏就一抽一抽的
            double dsp = AudioSettings.dspTime;
            if (lastDsp > 0) tuneClock += (float)System.Math.Min(0.1, dsp - lastDsp); else tuneClock += dt;
            lastDsp = dsp;
            while (tuneNext < tune.Count && tuneClock + Lookahead >= tune[tuneNext].at)
            {
                var n = tune[tuneNext];
                double when = dsp + System.Math.Max(0.0, n.at - tuneClock);
                Note(n.midi, n.vel, when);
                if (n.tooth >= 0) pendingShake.Add((n.at, n.tooth));
                tuneNext++;
            }
            for (int k = pendingShake.Count - 1; k >= 0; k--)
                if (tuneClock >= pendingShake[k].at) { toothShake[pendingShake[k].tooth] = 1f; pendingShake.RemoveAt(k); }
            // 放完停一小会儿，从头再来
            if (tuneNext >= tune.Count && tuneClock >= tuneLen + LoopGap) { tuneClock -= tuneLen + LoopGap; tuneNext = 0; pendingShake.Clear(); }
            if (drum)
            {
                float a = Mathf.Clamp01(tuneClock / Mathf.Max(0.1f, tuneLen)) * 360f;
                drum.localRotation = Quaternion.Inverse(drum.parent.rotation) * Quaternion.AngleAxis(-a, body.right) * drum.parent.rotation * drumBase;
            }
        }

        AudioSource Voice()
        {
            for (int i = 0; i < voices.Count; i++) if (!voices[i].isPlaying && voiceFade[i] <= 0f) return voices[i];
            if (voices.Count >= 24)
            {
                // 满了：最老的那个快速淡出（不硬掐，硬掐会"咔"一下），新音用另一个空出来的声部
                int oldest = -1;
                for (int i = 0; i < voices.Count; i++) if (voiceFade[i] <= 0f && (oldest < 0 || voices[i].time > voices[oldest].time)) oldest = i;
                if (oldest >= 0) voiceFade[oldest] = 0.03f;
                if (voices.Count >= 32)
                {
                    // 上限 32 个：实在没有空的，就接管淡出剩余时间最短的那个（很少发生）
                    int pick = 0;
                    for (int i = 1; i < voices.Count; i++) if (voiceFade[i] > 0f && (voiceFade[pick] <= 0f || voiceFade[i] < voiceFade[pick])) pick = i;
                    voices[pick].Stop(); voiceFade[pick] = 0f;
                    return voices[pick];
                }
            }
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 0.55f; s.minDistance = 0.5f; s.dopplerLevel = 0f;
            voices.Add(s); voiceFade.Add(0f);
            return s;
        }

        /// <summary>淡出中的声部每帧压低音量，到 0 再停（Update 里调）。</summary>
        void FadeVoices(float dt)
        {
            for (int i = 0; i < voices.Count; i++)
            {
                if (voiceFade[i] <= 0f) continue;
                float left = voiceFade[i] - dt;
                var v = voices[i];
                if (left <= 0f) { v.Stop(); voiceFade[i] = 0f; }
                else { v.volume *= Mathf.Clamp01(left / (left + dt)); voiceFade[i] = left; }
            }
        }

        void Note(int midi, float vel, double when = -1)
        {
            var v = Voice(); v.clip = TKMusicBoxSynth.Clip(midi); v.volume = vel * volume; v.pitch = 1f;
            if (when > 0) v.PlayScheduled(when); else v.Play();
        }

        /// <summary>曲目面板戳了一下：轻轻的拨音（选了曲子更亮一点）。</summary>
        public void Tick(bool select) => Note(select ? 91 : 98, select ? 0.22f : 0.14f);

        /// <summary>按钮咔哒（木头钮：短促的高音拨一下）。</summary>
        void Click(int kind) => Note(kind == 0 ? 96 : 100, 0.28f);

        void StopVoices() { foreach (var v in voices) if (v) v.Stop(); }
    }
}
