using Twilight.Logic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Twilight
{
    /// <summary>把玩法核心和表现层串起来：音块（TileField）、琴键（PianoKeys）、声音（PianoAudio）、世界铺开（RevealDirector）、光河闪光。
    /// 输入：编辑器里 A S D F = 四条轨道（按住 = 长按）；头显里由 HandTapInput 调 Tap / 提供 IsLaneHeld。
    /// Step(dt) 也可以在非播放状态下调用（编辑器里逐帧推进、截图检查）。</summary>
    public class GameDirector : MonoBehaviour
    {
        public string songId = "ode";
        public bool playOnStart = true;
        public bool autoPlay;
        [Tooltip("MR：点错轨道只断连击")] public bool lenient = true;
        public TileField tiles;
        public PianoKeys keys;
        public PianoAudio pianoAudio;
        public RevealDirector reveal;
        public LightRiver river;
        public Material riverMaterial;
        public HitFX fx;
        public BeatTrack beatTrack;
        public ScoreHUD scoreHud;
        [Tooltip("大师演奏：按大师谱放声音、琴键跟着动")] public TKMasterPlayer master;
        /// <summary>当前这首是不是大师演奏（欣赏 + 跟弹，敲击不出声）。</summary>
        public bool MasterMode { get; private set; }
        /// <summary>大师演奏里跟弹的连续正确次数（反馈用）。</summary>
        public int FollowStreak { get; private set; }

        public GameCore Game { get; private set; }
        public GameResult LastResult { get; private set; }
        /// <summary>敲击次数（手势 + 键盘）：结束画面“敲任意轨道继续”用。</summary>
        public int TapCount { get; private set; }
        /// <summary>头显手势输入挂进来（可选）：轨道是否还按着。</summary>
        public System.Func<int, bool> HandHeld;

        readonly float[] laneFlash = new float[4];
        static readonly Key[] LaneKeys = { Key.A, Key.S, Key.D, Key.F };
        static readonly int IdLaneFlash = Shader.PropertyToID("_LaneFlash");

        class Events : GameEvents
        {
            readonly GameDirector d;
            public Events(GameDirector d) { this.d = d; }
            public override void Play(Tile t, float durSec, float bps) => d.OnPlay(t, durSec, bps);
            public override void Release(Tile t) { if (d.pianoAudio) d.pianoAudio.Release(t); if (d.keys) d.keys.Release(t.key); }
            public override void HoldEnd(Tile t, bool ok)
            {
                if (d.keys) d.keys.Release(t.key);
                if (ok && d.tiles) d.tiles.BurstAt(t.lane, 0.8f);
                if (ok && d.fx) d.fx.HoldDone(t.lane);
            }
            public override void Hit(Tile t, Judge j, int combo)
            {
                if (d.tiles) d.tiles.BurstAt(t.lane, j.pts == 3 ? 1.2f : j.pts == 2 ? 1f : 0.8f);
                d.laneFlash[t.lane] = j.pts == 3 ? 1f : j.pts == 2 ? 0.75f : 0.5f;
                if (d.fx) d.fx.Judge(t.lane, j, combo);
                if (d.reveal) d.reveal.OnHit(d.Game.ProgressValue);
            }
            public override void PressLane(int lane, bool wrong) { if (wrong) { d.laneFlash[lane] = Mathf.Max(d.laneFlash[lane], 0.35f); if (d.fx) d.fx.Wrong(lane); } }
            public override void Missed(Tile t) { if (d.fx) d.fx.Miss(t.lane); }
            public override void Gain(int lane, int amount, bool tick) { if (d.scoreHud) d.scoreHud.Gain(lane, amount, tick); }
            public override void Fail(Tile t) { if (d.pianoAudio && Application.isPlaying) d.pianoAudio.PlayError(); }
            public override void MasterNote(Tile t, float durSec)
            {
                // 大师弹过一块：轻轻的光，世界照样跟着音乐铺开
                d.laneFlash[t.lane] = Mathf.Max(d.laneFlash[t.lane], 0.3f);
                if (d.reveal) d.reveal.OnHit(d.Game.ProgressValue);
            }
            public override void Follow(int lane, bool ok, Judge? j)
            {
                // 跟着大师弹：不出声，只给反馈——对了轨道亮、迸一点光、显示判定字；错了轨道淡淡一红
                if (ok)
                {
                    d.FollowStreak++;
                    d.laneFlash[lane] = 1f;
                    if (d.tiles) d.tiles.BurstAt(lane, 0.9f);
                    if (d.fx && j.HasValue) d.fx.Judge(lane, j.Value, d.FollowStreak);
                }
                else
                {
                    d.FollowStreak = 0;
                    d.laneFlash[lane] = Mathf.Max(d.laneFlash[lane], 0.35f);
                    if (d.fx) d.fx.Miss(lane);
                }
            }
            public override void End(GameResult r) { d.LastResult = r; if (d.reveal) d.reveal.Finale(); Debug.Log($"结束：{(r.win ? "完成" : "失败")} 评级 {r.grade} 准确率 {r.accuracy:P0} 分 {r.score} 最大连击 {r.maxCombo} 漏 {r.missedTiles} 长按 {r.holdsDone}/{r.holds} 敲击 {d.TapCount}"); }
        }

        void Start() { if (playOnStart && Application.isPlaying) StartSong(songId); }

        /// <summary>开一首：master = 大师演奏（音块自己落下，大师按完整编曲演奏；玩家可以跟着敲，不出声、只给对错反馈，不计纪录）。</summary>
        /// <summary>只弹一遍（不循环加速）：新手练习用。</summary>
        public bool OnePass;

        public void StartSong(string id, bool masterMode)
        {
            MasterMode = masterMode;
            StartSong(id);
        }

        public void StartSong(string id)
        {
            var song = SongLibrary.Find(id) ?? SongLibrary.Load()[0];
            songId = song.id;
            Game ??= new GameCore(new Events(this));
            // 第三十七轮：敲击检测对指尖高度做了平滑（时间常数约 25 ms），触发会晚约 20 ms，判定时补回来
            { var hti = FindAnyObjectByType<HandTapInput>(); Game.InputLag = hti && hti.config != null ? hti.config.smoothTau * 0.8f : 0f; }
            Game.Auto = autoPlay; Game.Lenient = lenient; Game.WaitForTap = false; Game.Master = MasterMode;
            FollowStreak = 0;
            Game.IsHeld = IsHeld;
            if (tiles) tiles.Clear();
            if (reveal) reveal.ResetAll();
            if (fx) fx.ResetAll();
            if (scoreHud) scoreHud.HideResult();
            LastResult = null;
            // 大师演奏：只弹一遍、不加速
            Game.Start(song.id, song.bpm, song.score, MasterMode || OnePass ? new[] { 1f } : song.speeds);
            soundNext = 0; soundTrim = 0;
            if (master) { if (MasterMode) master.Begin(song); else master.Stop(); }
        }

        /// <summary>停掉当前这首（重新摆琴 / 开场前）：清音块、松开琴键、收起界面。</summary>
        public void StopSong()
        {
            Game?.Stop();
            if (master) master.Stop();
            if (tiles) tiles.Clear();
            if (keys) keys.ReleaseAll();
            if (fx) fx.ResetAll();
            if (scoreHud) scoreHud.HideResult();
            LastResult = null;
        }

        bool IsHeld(int lane)
        {
            if (HandHeld != null && HandHeld(lane)) return true;
            var kb = Keyboard.current;
            return kb != null && kb[LaneKeys[lane]].isPressed;
        }

        /// <summary>敲击一条轨道（手势输入 / 键盘）。</summary>
        public void Tap(int lane) { TapCount++; Tapped?.Invoke(lane); Game?.Tap(lane); }
        /// <summary>每次敲击（手势 / 键盘），教程用。</summary>
        public event System.Action<int> Tapped;
        /// <summary>让一条轨道亮一下（教程指引用）。</summary>
        public void FlashLane(int lane, float v) { if (lane >= 0 && lane < 4) laneFlash[lane] = Mathf.Max(laneFlash[lane], v); }

        void Update()
        {
            if (Game == null) return;
            var kb = Keyboard.current;
            if (kb != null)
                for (int i = 0; i < 4; i++) if (kb[LaneKeys[i]].wasPressedThisFrame) Tap(i);
            Step(Time.deltaTime);
        }

        // ---- 第十六轮（用户：音乐不要一卡一卡的，要一直响，不然不像一首歌）----
        // 原来每个音只在“敲中的那一刻”发声：敲早 / 敲晚 / 漏敲，音乐就跟着忽快忽慢、断断续续。
        // 现在整首歌按拍子时钟（AudioSettings.dspTime 预约）准时发声，不管敲没敲中都一直响；敲击只管判定、特效和得分。
        // 教程的“停下来等你敲”模式还是敲了才响（不然停着的时候会先响出来）。
        int soundNext, soundTrim;

        void ScheduleSound()
        {
            soundNext -= Game.Trimmed - soundTrim; soundTrim = Game.Trimmed;
            // 教程“等你敲”期间由敲击发声：调度指针跟着进度走，切回正常节奏时不会把前面的音补响一遍
            if (Game.WaitForTap) { soundNext = Mathf.Max(soundNext, Game.Cursor); return; }
            if (MasterMode || !pianoAudio || !Application.isPlaying) return;
            if (Game.State != GameState.Playing) return;
            if (soundNext < 0) soundNext = 0;
            var list = Game.Tiles;
            while (soundNext < list.Count)
            {
                var t = list[soundNext];
                float ahead = (t.beat - Game.Beat) / Game.Bps;
                if (ahead > 0.1f) break;
                PlayTile(t, t.dur / Game.Bps, Game.Bps, Mathf.Max(0f, ahead));
                soundNext++;
            }
        }

        void PlayTile(Tile t, float durSec, float bps, float delay)
        {
            // 旋律全音量、伴奏轻一点；长按块的音一直响到松手 / 结束；音按时值自然衔接（下一个音来之前不断）
            float len = t.hold ? durSec + 0.4f : Mathf.Max(0.9f, durSec * 1.8f);
            if (t.seq == null)
                for (int i = 0; i < t.notes.Length; i++) pianoAudio.Play(t.notes[i], i == 0 ? 1f : 0.7f, len, delay, t);
            else
                foreach (var g in t.seq)
                    for (int i = 0; i < g.notes.Length; i++)
                        pianoAudio.Play(g.notes[i], g.soft ? 0.55f : i == 0 ? 1f : 0.7f, Mathf.Max(0.9f, len - g.at / bps), delay + g.at / bps, t);
        }

        public void Step(float dt)
        {
            if (Game == null) return;
            Game.Auto = autoPlay;
            Game.Update(dt);
            ScheduleSound();
            if (tiles) tiles.Sync(Game, dt);
            if (reveal) reveal.Tick(Game.Beat, Game.Bps, Game.ProgressValue, dt);
            for (int i = 0; i < 4; i++) laneFlash[i] = Mathf.MoveTowards(laneFlash[i], 0f, dt * 3.5f);
            if (riverMaterial) riverMaterial.SetVector(IdLaneFlash, new Vector4(laneFlash[0], laneFlash[1], laneFlash[2], laneFlash[3]));
        }

        void OnPlay(Tile t, float durSec, float bps)
        {
            if (MasterMode) return;                                     // 大师演奏：声音和琴键交给 TKMasterPlayer
            var color = TileField.LaneColors[t.lane];
            if (keys && t.notes.Length > 0)
            {
                keys.NoteOn(t.key, color, t.hold ? durSec : Mathf.Min(durSec * 0.8f, 0.45f), t.hold);   // 第十六轮：按下的就是这个音真正的键（音块正落在它上方）
            }
            if (!pianoAudio || !Application.isPlaying) return;
            if (Game.WaitForTap) PlayTile(t, durSec, bps, 0f);          // 教程等你敲：敲了才响；正常演奏由 ScheduleSound 按拍子发声
        }
    }
}
