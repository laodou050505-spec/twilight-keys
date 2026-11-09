using Twilight.Logic;
using UnityEngine;

namespace Twilight
{
    /// <summary>一局的流程：
    /// 封面：MR 启动后面前弹出一只八音盒，掀开盖（盖内写着黄昏琴村 / Twilight Keys），小浮岛从盒里升起；下方提示“手掌平放在桌面上”和进度；
    /// 放琴：手掌平放在真实桌面上 1 秒 → 整个世界（琴、光河、窗户、虚拟木桌）按手掌位置摆过去，琴键顶面对齐桌面；
    /// 选歌：八音盒上 ◀ ▶ 换歌（铭牌写歌名、八音盒放这首的开头），按“开始”（或直接敲琴键）→ 八音盒合上化开；
    /// 开场：光河从村子的天空流下来，穿过窗户、沿着桌面流到琴键（约 3.6 秒，伴一串上行琶音）；
    /// 开局：音块停在判定线上等着，敲第一块开始；掌心朝上 = 暂停 / 继续；暂停或结束时手掌平放 1 秒 = 重新摆琴；
    /// 结束：结算牌（分数滚动 → 评级）→ 八音盒再弹出来（已选好下一首）→ 按“开始”或敲任意一条轨道。
    /// 文字一律中英双语，摆好琴后都显示在琴上方的牌子上（不跟着头）。编辑器里没有头显：直接用编辑时的位置开场（键盘 A S D F）。</summary>
    public class TKSession : MonoBehaviour
    {
        public Transform world;
        [Tooltip("真实房间里的桌子（PICO 场景识别）")] public TKRoomTable room;
        public Transform stage;
        public Transform head;
        public HandTapInput hands;
        public GameDirector director;
        public TKMusicBox box;
        public TKTutorial tutorial;
        [Tooltip("演奏中的暂停键（琴左上方）")] public TKPauseButton pauseButton;
        [Tooltip("编辑时桌面高度（世界 y）")] public float authoredTableY = 0.74f;
        public float introSeconds = 3.6f;
        [Tooltip("一首结束后先让玩家看这么久的风景（世界铺满、入夜），再升起结算纸；3 秒后敲任意轨道可以提前叫出")]
        public float lingerSeconds = 3f;     // 第二十六轮（用户）：8 → 1.5 → 3 秒；中间敲琴可以提前出结算
        [Tooltip("曲目顺序（空 = 曲库里全部，按类别、难度排）")] public string[] playlist;

        enum State { Place, Menu, Intro, Tutorial, Play, Result }
        State state = State.Place;
        public string StateName => state.ToString();
        int songIndex, lastTaps, arpIndex;
        readonly PalmPlacer placer = new();
        readonly PalmUp upL = new(), upR = new();
        ScoreHUD hud;
        float resultAt, introT;
        Vector3 stageLocal;           // Stage 在 World 下的本地位置（编辑时）
        GameState lastGame;
        static readonly int[] Arp = { 60, 64, 67, 71, 74, 76, 79, 83, 86 };   // 开场上行琶音（Cmaj9）

        static readonly Bi SayShowHands = new("把双手伸到面前", "hold your hands out in front of you");
        static Bi SayPlace(int p) => new($"把手掌平放在桌面上，停一秒", $"rest a palm flat on the table for a moment");
        static readonly Bi SayReady = new("敲一下停在琴键上的那一块，就开始", "tap the tile resting on the keys to begin");
        static readonly Bi SayPaused = new("暂停了。掌心再朝上，接着弹", "paused — turn a palm up to carry on");
        static readonly Bi SayMenu = new("两边小钮换歌 · 拨小岛转着看", "side buttons: change song  ·  spin the island");
        static readonly Bi SayMasterHint = new("大师演奏中 · 可以跟着敲（不会出声）", "the master is playing — tap along if you like (silent)");
        float playAt;
        static readonly Bi SayWait = new("敲亮着的那一条", "tap the glowing lane");
        static readonly Bi SayHold = new("长条：手指按在桌上别抬，等它走完再松开", "long bar: keep your finger down until it ends");
        int holdHints; float holdHintUntil = -1f; Tile holdHinted;

        /// <summary>长条快到判定线（2 拍内）时提醒怎么弹：每次开机前 4 根长条提醒，每次显示到长条走完。</summary>
        bool HoldComing(GameCore g)
        {
            if (Time.time < holdHintUntil) return true;
            if (holdHints >= 4 || g.Master) return false;
            foreach (var t in g.Tiles)
            {
                if (!t.hold || t.state != TileState.Pending || t == holdHinted) continue;
                float ahead = t.beat - g.Beat;
                if (ahead > 0f && ahead < 2f) { holdHinted = t; holdHints++; holdHintUntil = Time.time + (ahead + t.dur) / Mathf.Max(0.1f, g.Bps); return true; }
                if (ahead > 2f) break;
            }
            return false;
        }
        static readonly Bi SayGoOn = new("很好！现在跟着节奏来", "nice! now keep up with the beat");
        int practiceLeft;                          // 练习：还要敲中几块才恢复正常节奏
        float goOnUntil = -1f;
        static readonly Bi SayBoxPaused = new("暂停了：按“继续”，或再把掌心朝上", "paused — press Resume, or turn a palm up again");
        static readonly Bi SayNext = new("敲任意一条轨道，下一首", "tap any lane for the next song");
        static readonly Bi SayAgain = new("敲任意一条轨道，再来一次", "tap any lane to play it again");
        float boxAt = -1f, menuAt;                // 结算后八音盒再弹出的时刻

        void Awake()
        {
            if (playlist == null || playlist.Length == 0) playlist = SongLibrary.Ordered().ToArray();
            // 第二十八轮：还没学过（新手教学开着）→ 八音盒铭牌一开始就是教学那首
            if (TKLang.Tutorial) { int ti = System.Array.IndexOf(playlist, PracticeSong); if (ti >= 0) songIndex = ti; }
        }

        void Start()
        {
            stageLocal = world.InverseTransformPoint(stage.position);
            hud = director.scoreHud;
            if (!Application.isEditor || UnityEngine.XR.XRSettings.isDeviceActive)
            {
                // 不再要“手掌放平”：八音盒一出来就能按“开始”；按的那一刻按八音盒的位置和头的朝向把琴摆好（AutoPlace）
                world.gameObject.SetActive(false); state = State.Menu; menuAt = Time.time; boxAt = -1f; placed = false;
                if (box) { box.playlist = playlist; box.songIndex = songIndex; box.ResumeMode = false; box.Show(true); box.SetCanStart(true); }
            }
            else
            {
                // 编辑器没有头显：直接用编辑时的位置，弹出八音盒（← → 选歌、回车开始）
                hands.contactY = authoredTableY + 0.007f;
                placed = true;                                          // 编辑器：琴就在编辑时的位置，不自动摆
                if (box) { box.playlist = playlist; box.songIndex = songIndex; EnterMenu(); }
                else BeginIntro();
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var L = hands ? hands.Left : null;
            var R = hands ? hands.Right : null;
            var game = director.Game;
            if (pauseButton && state != State.Play && state != State.Tutorial) { pauseButton.Shown = false; pauseButton.Beckon = false; }

            // 第三十二轮（用户：琴不能和弹出来的八音盒叠在一起）：琴摆出来以后，八音盒盒底至少比琴键顶面高 7 cm（琴键顶面 ≈ 指尖接触高度 + 4 cm）
            if (box) box.minBottomY = placed && world.gameObject.activeInHierarchy && hands ? hands.contactY + 0.04f + boxClearance : float.NegativeInfinity;

            switch (state)
            {
                case State.Place:
                    if (TryPlace(dt, L, R)) { if (box) EnterMenu(); else BeginIntro(); }
                    else if (box) box.SetHint(hands && hands.Tracked ? SayPlace(Mathf.RoundToInt(placer.Progress * 100)) : SayShowHands, hands && hands.Tracked ? placer.Progress : -1f);
                    break;
                case State.Menu:
                    CoverHint();
                    TrackTable(L, R);
                    FollowPosture();
                    if ((box && box.StartPressed) || (placed && Time.time - menuAt > 1.2f && director.TapCount != lastTaps && !(box && box.Reaching)))   // 第三十一轮：手在按八音盒的钮时敲到琴键不算开始
                    {
                        // 第二十二轮：按八音盒“开始”一定按现在的姿势重新摆琴（弹完一首换成坐着 / 站着都跟得上）；敲琴开始说明手已经在琴上，不动
                        bool fromBox = box && box.StartPressed;
                        if (box) { songIndex = box.songIndex; box.StartPressed = false; box.Hide(); }
                        if (!placed || fromBox) AutoPlace();
                        BeginIntro();
                    }
                    break;
                case State.Intro:
                    UpdateIntro(dt);
                    break;
                case State.Tutorial:
                    // 第二十七轮：教学时也有暂停钮（提示里说了“按暂停、在齿轮里调高度”）；按了就弹出八音盒（继续 / 设置 / 回主页）
                    if (pauseButton)
                    {
                        pauseButton.Shown = !tutorialPaused;
                        pauseButton.Beckon = tutorial.AtStart;
                        if (pauseButton.Pressed) { pauseButton.Pressed = false; tutorialPaused = true; }
                    }
                    if (tutorialPaused && box)
                    {
                        Say(Bi.None);
                        if (!box.Visible) { box.playlist = playlist; box.songIndex = songIndex; box.ResumeMode = true; box.Show(true); }
                        box.SetHint(Bi.None, -1f); box.Locked = TKLang.Tutorial || InTutorial;
                        FollowPosture();                                   // 齿轮里改了高度：琴马上跟着动
                        if (box.HomePressed) { box.HomePressed = false; tutorialPaused = false; tutorial.End(); EnterMenu(); break; }
                        if (box.StartPressed) { box.StartPressed = false; songIndex = box.songIndex; tutorialPaused = false; box.Hide(); }
                        break;
                    }
                    if (tutorial.Tick(dt)) { Say(Bi.None); BeginSong(true); }
                    else Say(tutorial.Message);
                    break;
                case State.Play:
                    if (game == null) break;
                    // 掌心朝上：暂停 / 继续
                    if (!(box && box.Handling) && (upL.Update(dt, L) || upR.Update(dt, R)))     // 拨小岛时掌心可能朝上，不算
                    {
                        if (game.State == GameState.Paused) game.Resume(); else game.Pause();
                    }
                    // 暂停键（琴左上方的小圆钮）：只在演奏中显示，按了就暂停（八音盒弹出来：继续 / 换歌 / 回主页）
                    if (pauseButton)
                    {
                        pauseButton.Shown = game.State == GameState.Playing || game.State == GameState.Ready;
                        if (pauseButton.Pressed) { pauseButton.Pressed = false; if (game.State == GameState.Playing || game.State == GameState.Ready) game.Pause(); }
                    }
                    if (game.State == GameState.Paused)
                    {
                        if (box)
                        {
                            // 暂停菜单：八音盒弹出来（“开始”变“继续”；换了歌再按就从新歌开始；也能退出）
                            Say(Bi.None);
                            if (!box.Visible) { box.playlist = playlist; box.songIndex = songIndex; box.ResumeMode = true; box.Show(true); }
                            box.SetHint(Bi.None, -1f); box.Locked = TKLang.Tutorial || InTutorial;
                            FollowPosture();                               // 第三十二轮（用户：游戏里切换琴的高度没有变化）：暂停菜单里调了高度，琴马上跟着动
                            // 暂停菜单里“退出”变成“回主页”：停掉这一首，回到八音盒选歌
                            if (box.HomePressed)
                            {
                                box.HomePressed = false;
                                director.StopSong();
                                if (director.river) director.river.hide = 1f;
                                EnterMenu();
                                break;
                            }
                            if (box.StartPressed)
                            {
                                box.StartPressed = false; box.Hide();
                                if (box.songIndex == songIndex) game.Resume();
                                else { songIndex = box.songIndex; BeginSong(); break; }
                            }
                        }
                        else Say(SayPaused);
                    }
                    else if (box && box.Visible && box.ResumeMode && !box.Closing) box.Hide();     // 掌心朝上继续了：收起八音盒
                    else if (game.State == GameState.Ready) Say(TKLang.Coached ? Bi.None : SayReady);     // 教学提示只在第一首里说
                    else if (game.Master) Say(director.FollowStreak >= 3 ? new Bi($"跟上了 ×{director.FollowStreak}", $"in step ×{director.FollowStreak}") : Time.time - playAt < 6f && !masterHintDone ? SayMasterHint : Bi.None);
                    else if (game.WaitForTap && game.Waiting) Say(SayWait);
                    else if (!TKLang.Coached && HoldComing(game)) Say(SayHold);
                    else if (Time.time < goOnUntil) Say(SayGoOn);
                    else Say(Bi.None);
                    // 练习：敲中够数就恢复正常节奏，教程算完成
                    if (game.WaitForTap && game.Hits >= practiceLeft)
                    {
                        game.WaitForTap = false; goOnUntil = Time.time + 3f; TKLang.Tutorial = false; TKLang.Coached = true;
                        Debug.Log("[TK] 练习结束，恢复正常节奏");
                    }
                    if (game.Master && Time.time - playAt >= 6f && !masterHintDone) { masterHintDone = true; TKLang.MasterHinted = true; }
                    if (game.State == GameState.Over && lastGame != GameState.Over) { state = State.Result; resultAt = Time.time; if (!game.Master) TKLang.Coached = true; }
                    lastGame = game.State;
                    break;
                case State.Result:
                    Say(Bi.None);
                    var r = director.LastResult;
                    bool win = r != null && r.win;
                    float since = Time.time - resultAt;
                    if (box && boxAt > 0f && Time.time >= boxAt)
                    {
                        // 分数看过了：收起结算牌，八音盒弹出来（赢了就预选下一首）
                        if (!box.Visible)
                        {
                            if (hud) { hud.HideResult(); hud.prompt = Bi.None; }
                            box.playlist = playlist; box.songIndex = win && !lastPractice ? (songIndex + 1) % playlist.Length : songIndex;   // 练习曲弹完：回到玩家自己选的那首
                            box.ResumeMode = false; box.Show(true);
                        }
                        CoverHint();
                        FollowPosture();
                        if (Time.time - boxAt > 1f && box.StartPressed) AutoPlace();      // 按八音盒“开始”接下一首：按现在的姿势重新摆琴
                        if (Time.time - boxAt > 1f && (box.StartPressed || (director.TapCount != lastTaps && !box.Reaching))) { songIndex = box.songIndex; box.StartPressed = false; box.Hide(); BeginSong(); }
                        break;
                    }
                    bool early = since > 0.6f && director.TapCount != lastTaps;
                    if (r != null && hud && !hud.ResultShowing && (since > lingerSeconds || early)) { hud.ShowResult(r, director.songId); lastTaps = director.TapCount; break; }
                    if (hud && !hud.ResultShowing) break;
                    bool ready = !hud || hud.ResultDone;
                    if (box && ready && boxAt < 0f) boxAt = Time.time + 4f;
                    if (hud) hud.prompt = r == null || !ready ? Bi.None : win ? SayNext : SayAgain;
                    if (ready && director.TapCount != lastTaps) { if (win && !lastPractice) songIndex = (songIndex + 1) % playlist.Length; BeginSong(); }
                    break;
            }
            lastTaps = director.TapCount;
        }

        /// <summary>开场：封面飘走，光河从天上流下来（river.hide 1 → 0），一串上行琶音跟着前沿走，到琴键时开局。</summary>
        void BeginIntro()
        {
            world.gameObject.SetActive(true);
            if (box && box.Visible) box.Hide();
            if (tutorial) tutorial.End();
            director.StopSong();
            Say(Bi.None);
            state = State.Intro; introT = 0f; arpIndex = 0;
            if (director.river) director.river.hide = 1f;
        }

        void UpdateIntro(float dt)
        {
            introT += dt;
            float k = Mathf.Clamp01(introT / introSeconds);
            float e = k * k * (3f - 2f * k);
            if (director.river) director.river.hide = 1f - e;
            // 琶音：前沿每走过一段响一个音（越来越近、越来越亮）
            while (arpIndex < Arp.Length && k >= 0.08f + arpIndex * (0.84f / Arp.Length))
            {
                if (director.pianoAudio && Application.isPlaying) director.pianoAudio.Play(Arp[arpIndex], Mathf.Lerp(0.28f, 0.5f, arpIndex / (float)Arp.Length), 2.6f);
                arpIndex++;
            }
            if (k >= 1f)
            {
                if (director.river) director.river.hide = 0f;
                if (director.beatTrack) director.beatTrack.Chime(0.35f);
                // 第三十轮（用户：新手教学只需要出现一次）：一开始教就记成“学过了”——中途回主页、退出游戏，下次都不再教（设置里可以手动再打开）
                if (tutorial && TKLang.Tutorial) { TKLang.Coached = false; tutorial.Begin(); state = State.Tutorial; TKLang.Tutorial = false; }
                else BeginSong();
            }
        }

        /// <summary>琴已摆好：世界出现（光河先藏着，开始后才从天上流下来），八音盒停在面前选歌。</summary>
        void EnterMenu()
        {
            world.gameObject.SetActive(true);
            director.StopSong();
            if (hud) hud.prompt = Bi.None;
            Say(Bi.None);
            if (director.river) director.river.hide = 1f;
            box.ResumeMode = false; box.Show(true); box.SetCanStart(true);
            state = State.Menu; menuAt = Time.time; boxAt = -1f;
        }

        bool tutorialPaused, lastPractice, masterHintDone;
        /// <summary>正在教学（教学步骤，或练习曲还在“等你敲”的阶段）：这时不许换歌。</summary>
        public bool InTutorial => state == State.Tutorial || (state == State.Play && lastPractice && director.Game != null && director.Game.WaitForTap);

        // ---- 第二十九轮（用户：游戏里看着歌名捏一下，把歌单一个一个拿出来，歌单一直留着）----
        /// <summary>现在能不能从窗边歌单直接换歌：正在弹 / 看结算时可以；八音盒菜单、教学进行中不行。</summary>
        public bool CanPickSong => (state == State.Play || state == State.Result) && !(director.Game != null && director.Game.WaitForTap);
        public string CurrentSongId => playlist != null && playlist.Length > 0 ? playlist[songIndex % playlist.Length] : "";
        public string[] Playlist => playlist;
        /// <summary>从窗边歌单换歌：马上从头弹这首（暂停中、结算中都行；八音盒弹着就收起）。</summary>
        /// <summary>编辑器测试：直接开始弹某首（跳过八音盒）。</summary>
        public void DebugPlay(string id) { int i = System.Array.IndexOf(playlist, id); if (i >= 0) songIndex = i; if (box) box.Hide(); BeginSong(); }

        public void PickSong(string id)
        {
            if (!CanPickSong) return;
            int i = System.Array.IndexOf(playlist, id);
            if (i < 0) return;
            songIndex = i;
            if (box && box.Visible) { box.StartPressed = false; box.Hide(); }
            if (hud) { hud.HideResult(); hud.prompt = Bi.None; }
            Debug.Log("[TK] 窗边歌单换歌：" + id);
            BeginSong();
        }
        static readonly Bi SayCoverTut = new("先跟着教学弹这首\n学完点歌名，就能自己选歌", "start with this tutorial song\nlater, tap the song name to pick any song");
        static readonly Bi SayCoverPanel = new("点歌名，打开曲目单", "tap the song name to see all songs");

        /// <summary>八音盒开始画面的提示：教学没做完 → 锁住换歌、铭牌停在教学那首；学完但还没打开过曲目单 → 提示歌名能点。</summary>
        void CoverHint()
        {
            if (!box) return;
            bool tut = TKLang.Tutorial;
            box.Locked = tut;
            if (tut && box.IsOpen) { box.SelectSong(PracticeSong); int ti = System.Array.IndexOf(playlist, PracticeSong); if (ti >= 0) songIndex = ti; }
            box.SetHint(tut ? SayCoverTut : !TKMusicBox.PanelKnown ? SayCoverPanel : Bi.None, -1f);
        }
        /// <summary>新手练习曲：最短的《两只老虎》，只弹一遍（第二十七轮，用户：有的歌太长，教学用短歌，之后玩家自己选）。</summary>
        const string PracticeSong = "tigers";

        void BeginSong(bool practice = false)
        {
            world.gameObject.SetActive(true);
            if (tutorial) tutorial.End();
            boxAt = -1f;
            state = State.Play;
            lastGame = GameState.Idle;
            if (director.river) director.river.hide = 0f;
            bool master = box && box.MasterMode && !practice;
            playAt = Time.time;
            masterHintDone = TKLang.MasterHinted;
            director.OnePass = practice;
            lastPractice = practice;
            director.StartSong(practice ? PracticeSong : playlist[songIndex % playlist.Length], master);
            if (practice && director.Game != null) { director.Game.WaitForTap = true; practiceLeft = 8; }
        }

        /// <summary>任意一只手放平 1 秒：把世界摆到手掌那里（判定线对着指尖，琴键顶面对齐桌面）。</summary>
        bool TryPlace(float dt, HandJoints L, HandJoints R)
        {
            var j = R ?? L;
            if (j == null || !head) { placer.Reset(); return false; }
            // 手掌要明显低于眼睛（避免举在空中误触发）
            if (head.position.y - j.middleTip.y < 0.3f) { placer.Reset(); return false; }
            if (!placer.Update(dt, j, out var res)) return false;
            float tableTop = res.contactY - 0.007f;               // 指尖关节中心比桌面高约 7 mm
            // 朝向：从头到手掌的水平方向——琴摆在手掌那里、正面正对着人（原来用手指的指向，手一放总有角度，琴就是歪的；
            // 也不直接用头的朝向：摆琴时人会低头扭头看手）
            var fwd = res.center - head.position; fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-4f) fwd = res.fwd;
            fwd.Normalize();
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            // 判定线落在指尖下面
            var stageTarget = new Vector3(res.center.x, 0, res.center.z) - fwd * director.tiles.hitS;
            world.rotation = rot;
            var p = stageTarget - rot * new Vector3(stageLocal.x, 0, stageLocal.z);
            world.position = new Vector3(p.x, tableTop - authoredTableY, p.z);
            hands.contactY = res.contactY;
            hands.Detector.Reset();
            if (director.river) director.river.Rebuild();
            Debug.Log($"[TK] 放琴：桌面 {tableTop:F3}，朝向 {rot.eulerAngles.y:F0}°");
            return true;
        }

        // ---------- 自动摆琴（不用手掌放平） ----------
        bool placed;
        float tableSeen = float.NaN, tableSeenAt = -99f;
        // 第十四轮（用户：注意玩家手的位置、琴出现的地方）：记下双手放低时（准备弹的姿势）指尖在哪，琴就摆到手底下
        readonly Vector3[] handLow = new Vector3[2];
        readonly float[] handLowAt = { -99f, -99f };
        [Tooltip("没看到手搭在桌上时，桌面比眼睛低多少（坐着弹琴的常见高度）")] public float defaultTableBelowEye = 0.42f;

        /// <summary>八音盒打开期间一直看手：手在眼睛下方 30 cm 以下、几根指尖高度差不多（搭在 / 靠近桌面）时，
        /// 记下最低指尖的高度当作桌面（取最近 3 秒里最低的那次，手抬起来不会把桌面抬高）。</summary>
        public void TrackTable(HandJoints L, HandJoints R)
        {
            if (!head) return;
            if (Time.time - tableSeenAt > 3f) tableSeen = float.NaN;
            for (int h = 0; h < 2; h++)
            {
                var hj = h == 0 ? L : R;
                if (hj == null) continue;
                var tip = (hj.indexTip + hj.middleTip + hj.ringTip) / 3f;
                if (head.position.y - tip.y > 0.33f) { handLow[h] = tip; handLowAt[h] = Time.time; }   // 比八音盒按钮低：是放在桌上 / 准备弹的手，不是在戳按钮
            }
            foreach (var j in new[] { L, R })
            {
                if (j == null) continue;
                float lo = Mathf.Min(Mathf.Min(j.indexTip.y, j.middleTip.y), Mathf.Min(j.ringTip.y, j.pinkyTip.y));
                float hi = Mathf.Max(Mathf.Max(j.indexTip.y, j.middleTip.y), Mathf.Max(j.ringTip.y, j.pinkyTip.y));
                if (head.position.y - lo < 0.3f || hi - lo > 0.05f) continue;
                if (float.IsNaN(tableSeen) || lo < tableSeen) { tableSeen = lo; tableSeenAt = Time.time; }
            }
        }

        /// <summary>按“开始”时把琴摆好：琴在八音盒正下方的桌面上、正面朝着人；桌面高度用 TrackTable 看到的（没看到就按坐姿估）。
        /// 之后敲击的最低点会继续自动校准（TapDetector.autoCalibrate），估得不准几下就对上。</summary>
        /// <summary>菜单 / 结算期间：站起来或坐下（眼睛高度变了 25 cm 以上）、或者设置里调了琴的高度 → 马上按现在的样子重新摆琴。</summary>
        void FollowPosture()
        {
            if (!placed || !head || Application.isEditor) return;
            if (box && box.PianoLiftChanged) { box.PianoLiftChanged = false; AutoPlace(); return; }
            if (float.IsNaN(placedEye)) return;
            if (Mathf.Abs(head.position.y - placedEye) > 0.25f) AutoPlace();
        }
        float placedEye = float.NaN;
        [Tooltip("八音盒盒底比琴键顶面至少高多少")] public float boxClearance = 0.07f;

        /// <summary>摆琴记录同时写进头显上的文件（logcat 缓冲很快会被冲掉）：Android/data/包名/files/tk_place.log。</summary>
        static void PlaceLog(string msg)
        {
            Debug.Log(msg);
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Application.persistentDataPath, "tk_place.log"), System.DateTime.Now.ToString("MM-dd HH:mm:ss ") + msg + "\n"); } catch { }
        }

        public void AutoPlace()   // 公开：编辑器测试也调
        {
            placed = true;
            if (head) placedEye = head.position.y;
            if (!head) return;
            // 第十九轮（用户：桌子要摆正在玩家眼前）：琴永远正对着玩家的视线、左右居中，不再跟桌边转、不往手的方向偏。
            // 真桌子（系统房间扫描）只用来定高度和远近：沿视线方向碰到桌子靠人的那条边，琴前沿放在边内 5 cm。
            var fwd = head.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            float depth = 0.38f; int n = 0;
            // 第二十一轮（用户：坐着时琴在膝盖上、站着又偏高）：不再看手的高度——真机日志里“手放在身前”抓到的是去按八音盒的手（站着时琴到了 1.33 m），
            // 坐着时手搭在膝盖上又被当成桌面。高度只按身体比例：坐着琴键在眼下 46 cm（≈ 坐姿手肘），站着在眼高的 64%（≈ 手肘略低）。
            float floorY = head.parent ? head.parent.position.y : 0f;
            float eyeH = head.position.y - floorY;
            bool floorKnown = eyeH > 0.9f && eyeH < 2.2f;                  // 追踪原点不是地面时（eyeH 很小）不按姿势估
            bool standing = floorKnown && eyeH > 1.38f;
            // 第二十二轮（用户：坐着还是在膝盖上）：坐姿抬高、推远——眼下 40 cm、前方 42 cm（书桌前弹琴时手的位置）；再加上设置里的高度微调
            float lift = TKLang.PianoLift;
            float contactY = (standing ? floorY + eyeH * 0.64f : head.position.y - 0.40f) + lift;
            string heightHow = (standing ? "按站姿（眼高 × 0.64）" : "按坐姿（眼下 40 cm）") + (Mathf.Abs(lift) > 0.001f ? $"，微调 {lift * 100f:+0;-0} cm" : "");
            if (!standing) depth = 0.42f;
            float tableTop = contactY - 0.007f;
            string how = $"默认远近 {depth * 100f:F0} cm";
            var eye = new Vector3(head.position.x, 0, head.position.z);
            // 真桌子只有在高度合适时才用（比舒适高度低 15 cm 到高 12 cm 之内）：站在一张普通书桌前，书桌太低就不用，虚拟桌子放到舒适高度
            if (room && room.Nearest(head.position, fwd, out var tb) && tb.center.y > contactY - 0.15f && tb.center.y < contactY + 0.12f && EnterTable(tb, eye, fwd, out float tIn))
            {
                depth = Mathf.Clamp(tIn + 0.05f + 0.069f, 0.28f, 0.6f);
                contactY = tb.center.y; tableTop = contactY - 0.007f; heightHow = "系统扫描的桌面";
                how = $"系统扫描的桌子（{tb.halfA * 2f:F2}×{tb.halfB * 2f:F2} m，视线碰到桌边 {tIn * 100f:F0} cm）";
            }
            var hitAt = eye + fwd * depth;
            var stageTarget = hitAt - fwd * director.tiles.hitS;
            world.rotation = rot;
            var p = stageTarget - rot * new Vector3(stageLocal.x, 0, stageLocal.z);
            world.position = new Vector3(p.x, tableTop - authoredTableY, p.z);
            // 虚拟木桌桌腿始终落地：按“桌面离地多高”竖直拉伸（模型原点在桌面、原高 authoredTableY）
            var vt = world.Find("VirtualTable");
            if (vt && floorKnown) { var sc = vt.localScale; sc.y = Mathf.Clamp((tableTop - floorY) / authoredTableY, 0.5f, 1.8f); vt.localScale = sc; }
            hands.contactY = contactY;
            hands.Detector.Reset();
            if (director.river) director.river.Rebuild();
            PlaceLog($"[TK] 自动摆琴：桌面 {tableTop:F3}（{heightHow}，眼睛离地 {eyeH:F2} m）、朝向 {rot.eulerAngles.y:F0}°，判定线离人 {depth * 100f:F0} cm（{how}；{(room ? room.Status : "没有房间识别组件")}）");
        }

        /// <summary>从 eye 沿 fwd（水平）看过去，第一次碰到桌面矩形的距离（射线和矩形求交，slab 法）。</summary>
        static bool EnterTable(TKRoomTable.Table tb, Vector3 eye, Vector3 fwd, out float tIn)
        {
            var o = eye - new Vector3(tb.center.x, 0, tb.center.z);
            float t0 = 0f, t1 = 10f;
            foreach (var (ax, half) in new[] { (tb.axisA, tb.halfA), (tb.axisB, tb.halfB) })
            {
                float p = Vector3.Dot(o, ax), d = Vector3.Dot(fwd, ax);
                if (Mathf.Abs(d) < 1e-5f) { if (Mathf.Abs(p) > half) { tIn = 0; return false; } continue; }
                float a = (-half - p) / d, b = (half - p) / d;
                if (a > b) (a, b) = (b, a);
                t0 = Mathf.Max(t0, a); t1 = Mathf.Min(t1, b);
            }
            tIn = t0;
            return t0 <= t1 && t0 < 1.2f;
        }

        void Say(Bi s) { if (hud) hud.message = s; }
    }
}
