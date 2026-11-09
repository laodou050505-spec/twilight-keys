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
            // 第三十六轮（用户：挪桌子时整个场景跟着动，很别扭）：琴台（琴 + 虚拟桌 + 圆钮 + 光河）单独一层 StageRig，
            // 双手拖动 / 对齐桌子 / 调高度都只动这一层；窗户、村子、天空、铺开效果留在 World 上不动。
            rig = new GameObject("StageRig").transform;
            rig.SetParent(world, false);
            stage.SetParent(rig, true);
            var vtable = world.Find("VirtualTable");
            if (vtable) vtable.SetParent(rig, true);
            BuildVeil();
            // 第三十四轮：琴两端的抓手圆钮（双手各捏一颗拖动整架琴）
            if (director.keys && box) grabHandles = TKGrabHandles.Create(rig, director.keys.transform, box.rimMaterial, box.plateMaterial);
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
                placed = true; envAnchored = true;                      // 编辑器：琴就在编辑时的位置，不自动摆
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
            // 第三十三轮：对齐桌子（设置里两指按桌边）、双手捏住琴拖动
            if (box && box.Visible) TableAlign(dt, L, R);
            PianoGrab(dt, L, R);

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
                    if ((box && box.StartPressed) || (placed && Time.time - menuAt > 1.2f && director.TapCount != lastTaps && !(box && box.Reaching) && !grabbing && Time.time > grabBusyUntil))   // 第三十一轮：手在按八音盒的钮时敲到琴键不算开始
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
                        pauseButton.Beckon = false;                         // 第三十五轮：教学第一步改教圆钮，暂停钮不再招手
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
                    if (!(box && box.Handling) && !grabbing && (upL.Update(dt, L) || upR.Update(dt, R)))     // 拨小岛时掌心可能朝上，不算
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
                        if (Time.time - boxAt > 1f && (box.StartPressed || (director.TapCount != lastTaps && !box.Reaching && !grabbing && Time.time > grabBusyUntil))) { songIndex = box.songIndex; box.StartPressed = false; box.Hide(); BeginSong(); }
                        break;
                    }
                    bool early = since > 0.6f && director.TapCount != lastTaps;
                    if (r != null && hud && !hud.ResultShowing && (since > lingerSeconds || early)) { hud.ShowResult(r, director.songId); lastTaps = director.TapCount; break; }
                    if (hud && !hud.ResultShowing) break;
                    bool ready = !hud || hud.ResultDone;
                    if (box && ready && boxAt < 0f) boxAt = Time.time + 4f;
                    if (hud) hud.prompt = r == null || !ready ? Bi.None : win ? SayNext : SayAgain;
                    if (ready && director.TapCount != lastTaps && !grabbing && Time.time > grabBusyUntil) { if (win && !lastPractice) songIndex = (songIndex + 1) % playlist.Length; BeginSong(); }
                    break;
            }
            UpdateBlink(dt);
            if (grabbing && grabOut && !(box && box.Visible)) Say(SayRelocate);
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
        const string PracticeSong = "scarborough";   // 第三十五轮（用户：教学用短的、不是童谣的曲子）：斯卡布罗集市，30 秒、35 块、难度 1

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
            var p = stageTarget - rot * new Vector3(stageLocal.x, 0, stageLocal.z);
            PlaceRig(new Vector3(p.x, tableTop - authoredTableY, p.z), rot);
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
            if (Mathf.Abs(head.position.y - placedEye) > 0.25f)
            {
                // 第三十三轮：手动对齐 / 双手摆好的琴不跟着姿势跑；系统扫描自动对齐的，站起坐下后重新判断要不要用那张桌子
                if (tableLocked && tableManual) { placedEye = head.position.y; return; }
                if (tableLocked) UnlockTable();
                AutoPlace();
            }
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
            // 第十九轮（用户：桌子要摆正在玩家眼前）：没有桌子可对时，琴正对着玩家的视线、左右居中。
            // 第三十三轮（用户：有自己桌子的玩家想对着桌子弹，琴有时是歪的）：有桌子时一律以桌边为准摆正——
            // 朝向垂直于桌边、高度 = 桌面、判定线在桌边往里 11.9 cm（琴前沿在桌边内 5 cm）。桌子来自“设置 → 对齐桌子”（两指按桌边）或系统房间扫描。
            var fwd = head.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            float floorY = head.parent ? head.parent.position.y : 0f;
            float eyeH = head.position.y - floorY;
            bool floorKnown = eyeH > 0.9f && eyeH < 2.2f;                  // 追踪原点不是地面时（eyeH 很小）不按姿势估
            bool standing = floorKnown && eyeH > 1.38f;
            float lift = TKLang.PianoLift;
            var eye = new Vector3(head.position.x, 0, head.position.z);
            // 系统扫描的桌子：视线碰到的那张、高度合理（坐着：眼下 15~75 cm；站着：眼高 × 0.64 附近 −15~+12 cm）→ 自动对齐到它朝着人的那条边
            if (!tableLocked && room && room.Nearest(head.position, fwd, out var tb) && EnterTable(tb, eye, fwd, out float tIn))
            {
                float below = head.position.y - tb.center.y, comfy = floorY + eyeH * 0.64f;
                bool fits = standing ? tb.center.y > comfy - 0.15f && tb.center.y < comfy + 0.12f : below > 0.15f && below < 0.75f;
                if (fits)
                {
                    // 桌面两根轴里和视线最接近的那根 = 琴的朝向；视线进桌的点投到近边上 = 琴的左右位置
                    Vector3 tf = tb.axisA; float half = tb.halfA, bestDot = -2f;
                    foreach (var (ax, h) in new[] { (tb.axisA, tb.halfA), (-tb.axisA, tb.halfA), (tb.axisB, tb.halfB), (-tb.axisB, tb.halfB) })
                        if (Vector3.Dot(ax, fwd) > bestDot) { bestDot = Vector3.Dot(ax, fwd); tf = ax; half = h; }
                    var c = new Vector3(tb.center.x, 0, tb.center.z);
                    var entry = eye + fwd * tIn;
                    var edgePt = entry - tf * (Vector3.Dot(entry - c, tf) + half);
                    LockTable(edgePt, tf, tb.center.y + 0.007f, false, $"系统扫描的桌子（{tb.halfA * 2f:F2}×{tb.halfB * 2f:F2} m，桌边离人 {Vector3.Dot(edgePt - eye, tf) * 100f:F0} cm）");
                }
            }
            Quaternion rot; Vector3 hitAt; float contactY; string heightHow, how;
            if (tableLocked)
            {
                rot = Quaternion.LookRotation(tableFwd, Vector3.up);
                hitAt = tableEdgeMid + tableFwd * TableEdgeInset;
                float fine = lift - liftAtLock;                                   // 对齐之后再在设置里调高度：在桌面基础上微调
                contactY = tableY + fine;
                heightHow = "桌面" + (Mathf.Abs(fine) > 0.001f ? $"，微调 {fine * 100f:+0;-0} cm" : "");
                how = tableHow;
            }
            else
            {
                // 第二十一 / 二十二轮：高度只按身体比例——坐着琴键在眼下 40 cm、前方 42 cm；站着在眼高的 64%；再加上设置里的高度微调
                float depth = standing ? 0.38f : 0.42f;
                contactY = (standing ? floorY + eyeH * 0.64f : head.position.y - 0.40f) + lift;
                heightHow = (standing ? "按站姿（眼高 × 0.64）" : "按坐姿（眼下 40 cm）") + (Mathf.Abs(lift) > 0.001f ? $"，微调 {lift * 100f:+0;-0} cm" : "");
                rot = Quaternion.LookRotation(fwd, Vector3.up);
                hitAt = eye + fwd * depth;
                how = $"跟着视线，判定线离人 {depth * 100f:F0} cm";
            }
            float tableTop = contactY - 0.007f;
            var f2 = rot * Vector3.forward;
            var stageTarget = new Vector3(hitAt.x, 0, hitAt.z) - f2 * director.tiles.hitS;
            var p = stageTarget - rot * new Vector3(stageLocal.x, 0, stageLocal.z);
            PlaceRig(new Vector3(p.x, tableTop - authoredTableY, p.z), rot);
            // 虚拟木桌桌腿始终落地：按“桌面离地多高”竖直拉伸（模型原点在桌面、原高 authoredTableY）
            var vt = rig.Find("VirtualTable");
            if (vt && floorKnown) { var sc = vt.localScale; sc.y = Mathf.Clamp((tableTop - floorY) / authoredTableY, 0.5f, 1.8f); vt.localScale = sc; }
            hands.contactY = contactY;
            hands.Detector.Reset();
            if (director.river) director.river.Rebuild();
            PlaceLog($"[TK] 自动摆琴：桌面 {tableTop:F3}（{heightHow}，眼睛离地 {eyeH:F2} m）、朝向 {rot.eulerAngles.y:F0}°（{how}）；{ScanSummary(fwd)}");
        }

        // ---------- 第三十三轮（用户：玩家可以用两只手捏住桌子来调）：双手捏住琴拖动 ----------
        bool grabbing; readonly bool[] grabPinch = new bool[2]; readonly float[] grabPinchAt = { -9f, -9f };
        Vector3 grabA0, grabB0, grabPos0; float grabYaw0, grabBusyUntil = -1f;
        TKGrabHandles grabHandles;
        const float KnobReach = 0.06f;                                       // 捏合点离圆钮多近算“捏住了钮”
        static readonly Bi SayGrab = new("捏着两颗圆钮拖：一起挪 = 移动，错开 = 转向，抬 / 压 = 调高度\n摆好松手", "drag with both hands: move, twist to turn, raise or lower\nlet go when it sits right");
        static float Yaw(Vector3 a, Vector3 b) { var d = b - a; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        /// <summary>琴摆出来以后（弹奏中除外），两只手同时在琴附近捏住（拇指碰食指）：琴跟着两手走——
        /// 两手中点平移、两手连线转了多少琴就转多少（只绕竖直轴）、两手一起抬高压低就调高度。松手后按新位置“对齐”下来（和设置里对齐桌子同一套）。</summary>
        void PianoGrab(float dt, HandJoints L, HandJoints R)
        {
            var game = director.Game;
            // 第三十五轮（用户：圆钮要一直留在场景里）：琴摆出来以后圆钮一直在，弹奏中也能拖（要双手各捏住一颗，不会误触）
            bool allowed = placed && world.gameObject.activeInHierarchy && state != State.Place;
            Vector3 pa = default, pb = default;
            for (int h = 0; h < 2; h++)
            {
                var j = h == 0 ? L : R;
                if (j == null) { grabPinch[h] = false; continue; }
                float d = Vector3.Distance(j.thumbTip, j.indexTip);
                bool was = grabPinch[h];
                grabPinch[h] = was ? d < 0.032f : d < 0.018f;
                if (grabPinch[h] && !was) grabPinchAt[h] = Time.time;
                var pp = (j.thumbTip + j.indexTip) * 0.5f;
                if (h == 0) pa = pp; else pb = pp;
            }
            bool both = grabPinch[0] && grabPinch[1];
            // 抓手圆钮：能调的时候出现；手（拇指食指中点）靠近哪颗，哪颗放大
            if (grabHandles)
            {
                grabHandles.Shown = allowed; grabHandles.Held = grabbing; grabHandles.Beckon = tutorial && tutorial.AtKnobs;
                for (int k = 0; k < 2; k++)
                {
                    float dmin = 9f;
                    if (L != null) dmin = Mathf.Min(dmin, Vector3.Distance(pa, grabHandles.Pos(k)));
                    if (R != null) dmin = Mathf.Min(dmin, Vector3.Distance(pb, grabHandles.Pos(k)));
                    grabHandles.SetHover(k, dmin < KnobReach * 1.6f ? 1f : 0f);
                }
            }
            if (grabbing)
            {
                if (!both || !allowed)
                {
                    // 松手：按现在的位置记成“对齐的桌子”，之后每次摆琴都在这里
                    grabbing = false; grabBusyUntil = Time.time + 0.8f;
                    var rot0 = Quaternion.Euler(0f, rig.eulerAngles.y, 0f); var f = rot0 * Vector3.forward;
                    var st = rig.position + rot0 * new Vector3(stageLocal.x, 0f, stageLocal.z); st.y = 0f;
                    var hit = st + f * director.tiles.hitS;
                    LockTable(hit - f * TableEdgeInset, f, rig.position.y + authoredTableY + 0.007f, true, "双手捏住摆放");
                    AutoPlace();
                    if (tutorial) tutorial.NotifyGrabbed();
                    if (box) box.AlignHint = Bi.None;
                    return;
                }
                float dyaw = Mathf.DeltaAngle(Yaw(grabA0, grabB0), Yaw(pa, pb));
                var q = Quaternion.Euler(0f, dyaw, 0f);
                var mid0 = (grabA0 + grabB0) * 0.5f; var mid = (pa + pb) * 0.5f;
                var off = grabPos0 - mid0; off.y = 0f;
                var np = new Vector3(mid.x, 0f, mid.z) + q * off;
                rig.position = new Vector3(np.x, grabPos0.y + (mid.y - mid0.y), np.z);
                rig.rotation = Quaternion.Euler(0f, grabYaw0 + dyaw, 0f);
                grabBusyUntil = Time.time + 0.8f;
                if (director.river && (Time.frameCount & 1) == 0) director.river.Rebuild();   // 光河跟着弯（隔帧重建）
                grabOut = director.river && !director.river.WindowReachable(out _);
                if (box && box.Visible) { box.AlignHint = grabOut ? SayRelocate : SayGrab; box.AlignProgress = -1f; }
                return;
            }
            if (!allowed || !both) return;
            // 开始：两手都是刚捏上（0.5 秒内），而且一只手捏在左钮、另一只捏在右钮上（6 cm 内；左右手哪只捏哪颗都行）
            if (Time.time - grabPinchAt[0] > 0.5f || Time.time - grabPinchAt[1] > 0.5f) return;
            if (!grabHandles || !grabHandles.Visible) return;
            Vector3 k0 = grabHandles.Pos(0), k1 = grabHandles.Pos(1);
            bool onKnobs = (Vector3.Distance(pa, k0) < KnobReach && Vector3.Distance(pb, k1) < KnobReach)
                        || (Vector3.Distance(pa, k1) < KnobReach && Vector3.Distance(pb, k0) < KnobReach);
            if (!onKnobs) return;
            grabbing = true; grabA0 = pa; grabB0 = pb; grabPos0 = rig.position; grabYaw0 = rig.eulerAngles.y; grabOut = false;
            if (box && box.TableAlignRequested) box.TableAlignRequested = false;
            Debug.Log("[TK] 双手捏住琴，开始拖动");
        }

        // ---------- 第三十六轮（用户：挪桌子时整个场景跟着动很别扭；琴挪到身后怎么办）：琴台和场景分开 ----------
        Transform rig;                 // StageRig：琴 + 虚拟桌 + 圆钮 + 光河（挂在 World 下）
        bool envAnchored;              // 场景（World）已经在房间里摆好了
        bool grabOut;                  // 拖动中：窗户超出光河能弯过去的范围
        [Tooltip("眨眼遮罩材质（Twilight/Veil；场景里引用住，打包才带着色器）")] public Material veilMaterial;
        Material veilMat; Renderer veilR;
        float blink; int blinkPhase;   // 0 无，1 变暗，2 亮回来
        static readonly Bi SayRelocate = new("松手后，场景会移到琴的正前方", "let go and the scene will move in front of the piano");

        /// <summary>把琴台摆到 pos / rot（世界坐标）。场景还没摆过：整个世界一起摆（第一次放琴）；
        /// 摆过了：只动琴台，场景不动——光河弯过去接上窗户；窗户超出光河能弯的范围（转到侧面、身后、换了桌子），就“眨一下眼”把场景搬到琴的正前方。</summary>
        void PlaceRig(Vector3 pos, Quaternion rot)
        {
            if (!envAnchored)
            {
                world.SetPositionAndRotation(pos, rot);
                rig.localPosition = Vector3.zero; rig.localRotation = Quaternion.identity;
                envAnchored = true;
                return;
            }
            rig.SetPositionAndRotation(pos, rot);
            if (director.river) director.river.Rebuild();
            if (director.river && !director.river.WindowReachable(out float ang))
            {
                PlaceLog($"[TK] 琴转到窗户 {ang:F0}° 外 / 太远：眨眼把场景搬到琴的正前方");
                if (blinkPhase == 0) blinkPhase = 1;
            }
        }

        /// <summary>场景搬到琴的正前方：World 摆成琴台现在的位置朝向，琴台回到 World 本地原点（琴台在世界里一动不动）。只在全黑时调用。</summary>
        void AnchorEnvToRig()
        {
            var rp = rig.position; var rr = Quaternion.Euler(0f, rig.eulerAngles.y, 0f);
            world.SetPositionAndRotation(rp, rr);
            rig.localPosition = Vector3.zero; rig.localRotation = Quaternion.identity;
            if (director.river) director.river.Rebuild();
        }

        void BuildVeil()
        {
            if (!head || !veilMaterial) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "BlinkVeil";
            go.transform.SetParent(head, false);
            go.transform.localPosition = Vector3.zero; go.transform.localScale = Vector3.one * 0.3f;
            veilR = go.GetComponent<MeshRenderer>();
            veilMat = new Material(veilMaterial);
            veilR.sharedMaterial = veilMat;
            veilR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; veilR.receiveShadows = false;
            go.SetActive(false);
        }

        /// <summary>眨眼：0.25 秒变暗 → 全黑时搬场景 → 0.35 秒亮回来。</summary>
        void UpdateBlink(float dt)
        {
            if (blinkPhase == 1)
            {
                blink = Mathf.MoveTowards(blink, 1f, dt / 0.25f);
                if (blink >= 1f) { AnchorEnvToRig(); blinkPhase = 2; }
            }
            else if (blinkPhase == 2)
            {
                blink = Mathf.MoveTowards(blink, 0f, dt / 0.35f);
                if (blink <= 0f) blinkPhase = 0;
            }
            if (veilR)
            {
                bool on = blink > 0.001f;
                if (veilR.gameObject.activeSelf != on) veilR.gameObject.SetActive(on);
                if (on) veilMat.SetColor("_Color", new Color(0f, 0f, 0f, blink * blink * (3f - 2f * blink)));
            }
        }

        // ---------- 第三十三轮：对齐桌子 ----------
        bool tableLocked, tableManual;
        Vector3 tableEdgeMid, tableFwd;
        float tableY, liftAtLock;
        string tableHow = "";
        [Tooltip("判定线在桌边往里多少（琴前沿在桌边内约 5 cm）")] public float TableEdgeInset = 0.119f;
        /// <summary>记下一张桌子（近边上一点、朝向、指尖贴桌面时的高度）：之后每次摆琴都按它摆正。</summary>
        void LockTable(Vector3 edgePt, Vector3 tf, float contact, bool manual, string how)
        {
            tableLocked = true; tableManual = manual;
            tableEdgeMid = new Vector3(edgePt.x, 0, edgePt.z); tf.y = 0; tableFwd = tf.normalized;
            tableY = contact; liftAtLock = TKLang.PianoLift; tableHow = how;
            if (box) { box.TableAligned = true; box.RefreshSetting(); }
        }
        void UnlockTable()
        {
            tableLocked = tableManual = false;
            if (box) { box.TableAligned = false; box.RefreshSetting(); }
        }

        static readonly Bi SayAlign = new("两手食指尖按在桌子靠你的边上\n左右分开一点，按住别动", "rest both index fingertips on the near edge of your table\nhands apart, hold still");
        static readonly Bi SayAligned = new("对齐好了：琴摆正在桌面上", "aligned: the piano sits square on your table");
        float alignHold, alignAt = -1f, alignedMsgUntil = -1f;
        Vector3 alignL, alignR;

        /// <summary>设置里按了“对齐桌子”：等两手食指尖都按在同一高度、左右分开 12~120 cm、1 秒内没挪动超过 1.5 cm
        /// → 两指连线 = 桌边，琴的朝向垂直于它、高度 = 指尖高度，马上重新摆琴。30 秒没对上就算了。</summary>
        void TableAlign(float dt, HandJoints L, HandJoints R)
        {
            if (box.TableAlignCancel) { box.TableAlignCancel = false; UnlockTable(); if (placed) AutoPlace(); }
            if (!box.TableAlignRequested)
            {
                alignHold = 0f; alignAt = -1f;
                box.AlignHint = Time.time < alignedMsgUntil ? SayAligned : Bi.None; box.AlignProgress = -1f;
                return;
            }
            if (alignAt < 0f) alignAt = Time.time;
            if (Time.time - alignAt > 30f) { box.TableAlignRequested = false; box.RefreshSetting(); return; }
            bool ok = false; Vector3 a = default, b = default;
            if (L != null && R != null && head)
            {
                a = L.indexTip; b = R.indexTip;
                var e = b - a; e.y = 0;
                ok = head.position.y - a.y > 0.2f && head.position.y - b.y > 0.2f && Mathf.Abs(a.y - b.y) < 0.03f && e.magnitude > 0.12f && e.magnitude < 1.2f;
            }
            if (!ok) alignHold = 0f;
            else if (alignHold <= 0f || (a - alignL).magnitude > 0.015f || (b - alignR).magnitude > 0.015f) { alignL = a; alignR = b; alignHold = 0.0001f; }
            else alignHold += dt;
            box.AlignHint = SayAlign; box.AlignProgress = alignHold;
            if (alignHold >= 1f)
            {
                var mid = (a + b) * 0.5f;
                var e = b - a; e.y = 0;
                var tf = Vector3.Cross(e, Vector3.up).normalized;           // 垂直于桌边、水平
                var away = mid - head.position; away.y = 0;
                if (Vector3.Dot(tf, away) < 0f) tf = -tf;                         // 朝着离开人的方向
                LockTable(mid, tf, (a.y + b.y) * 0.5f, true, $"手动对齐桌边（两指相距 {e.magnitude * 100f:F0} cm）");
                box.TableAlignRequested = false; box.RefreshSetting();
                alignHold = 0f; alignedMsgUntil = Time.time + 2.5f;
                AutoPlace();
                if (director.beatTrack) director.beatTrack.Chime(0.35f);
            }
        }

        /// <summary>系统扫描到的桌子（离人最近的 4 张）：距离、方位、桌面离眼多低、尺寸——写进摆琴日志，方便查“为什么没用上”。</summary>
        string ScanSummary(Vector3 fwd)
        {
            if (!room) return "没有房间识别组件";
            if (!room.Found) return room.Status;
            var l = new System.Collections.Generic.List<TKRoomTable.Table>(room.tables);
            var hp = new Vector3(head.position.x, 0, head.position.z);
            l.Sort((x, y) => (new Vector3(x.center.x, 0, x.center.z) - hp).sqrMagnitude.CompareTo((new Vector3(y.center.x, 0, y.center.z) - hp).sqrMagnitude));
            var sb = new System.Text.StringBuilder(room.Status + "：");
            for (int i = 0; i < Mathf.Min(4, l.Count); i++)
            {
                var d = new Vector3(l[i].center.x, 0, l[i].center.z) - hp;
                sb.Append($" [{d.magnitude:F2} m，{Vector3.SignedAngle(fwd, d, Vector3.up):F0}°，眼下 {(head.position.y - l[i].center.y) * 100f:F0} cm，{l[i].halfA * 2f:F2}×{l[i].halfB * 2f:F2}]");
            }
            return sb.ToString();
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
