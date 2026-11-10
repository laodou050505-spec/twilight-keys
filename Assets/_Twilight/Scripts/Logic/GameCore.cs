using System;
using System.Collections.Generic;

namespace Twilight.Logic
{
    public enum TileState { Pending, Holding, Hit, Missed }
    public enum GameState { Idle, Ready, Playing, Paused, Failing, Won, Over }

    /// <summary>一个钢琴块。beat：起点拍位；dur：拍数；hold：长按块（按住到 holdOk 才算完整）。</summary>
    public class Tile
    {
        public float beat, dur;
        public int[] notes;
        public NoteGroup[] seq;
        public int lane, loop, index;
        /// <summary>这个音在琴上真正的键（C4~B5；整首歌整体移八度落进两个八度里）。音块就落在这个键的正上方。</summary>
        public int key;
        /// <summary>画音块的位置（轨道坐标，可带小数：0 = 最左轨道中线 … 3 = 最右轨道中线），正对着 key 那个键。</summary>
        public float keyPos;
        /// <summary>键正好骑在两条轨道分界线上（F4、F5 等）时，相邻那条轨道也算对；否则 -1。</summary>
        public int altLane = -1;
        public bool Accepts(int l) => l == lane || l == altLane;
        public bool loopEnd, hold;
        public TileState state;
        /// <summary>长按：已经按住到第几拍（相对块起点，0..dur）。</summary>
        public float held;
        public bool holdBroken, holdDone;
        public object view;     // 渲染层挂的对象
    }

    public struct Judge
    {
        public string name; public float win; public int pts;
        public static readonly Judge Perfect = new() { name = "PERFECT", win = 0.12f, pts = 3 };
        public static readonly Judge Great = new() { name = "GREAT", win = 0.25f, pts = 2 };
        public static readonly Judge Good = new() { name = "GOOD", win = float.PositiveInfinity, pts = 1 };
        public static readonly Judge Miss = new() { name = "MISS", win = 0, pts = 0 };
        public static Judge Of(float absDt) => absDt < Perfect.win ? Perfect : absDt < Great.win ? Great : Good;
    }

    public class GameResult
    {
        public bool win, auto, master;
        public int score, stars, maxCombo, hits, misses, bonus, holdsDone, holds;
        /// <summary>漏掉的块数、准确率（0~1：每块 PERFECT 3 / GREAT 2 / GOOD 1 / 漏 0，长按没按住扣 1，除以满分）、评级 S/A/B/C。</summary>
        public int missedTiles;
        public float accuracy;
        public string grade;
        public static string GradeOf(float acc) => acc >= 0.92f ? "S" : acc >= 0.8f ? "A" : acc >= 0.6f ? "B" : "C";
        public Dictionary<string, int> counts;
        public string songId;
    }

    /// <summary>游戏回调（全部可选）。音乐完全由玩家敲出来：击中时 Play 发声（旋律 + 同块伴奏）。</summary>
    public class GameEvents
    {
        public virtual void Play(Tile t, float durSec, float bps) { }
        public virtual void Release(Tile t) { }                 // 长按提前松手：让音淡出
        public virtual void Judged(Tile t, Judge j) { }
        public virtual void Score(int s) { }
        public virtual void Combo(int c) { }
        public virtual void Stars(int n, bool won) { }
        public virtual void Progress(float p) { }
        public virtual void Hit(Tile t, Judge j, int combo) { }
        public virtual void HoldEnd(Tile t, bool complete) { }
        public virtual void PressLane(int lane, bool wrong) { }
        public virtual void Miss() { }
        public virtual void Missed(Tile t) { }                  // 宽松模式漏掉一块（不失败）
        /// <summary>得分（已乘倍率）：tick = 长按过程中的持续加分（量小、频繁）。</summary>
        public virtual void Gain(int lane, int amount, bool tick) { }
        public virtual void Fail(Tile t) { }
        public virtual void End(GameResult r) { }
        public virtual void Loop(int n, bool won) { }
        /// <summary>大师演奏：一块到线（大师弹过了）——演奏层按大师谱发声、琴键动、光效走。</summary>
        public virtual void MasterNote(Tile t, float durSec) { }
        /// <summary>大师演奏：玩家跟弹一下（ok = 轨道对、时机对；j 只在 ok 时有效）。不出声，只给反馈。</summary>
        public virtual void Follow(int lane, bool ok, Judge? j) { }
    }

    /// <summary>玩法核心（移植自原型 game.js，加上长按）：谱面生成、判定、计分、三轮加速、失败。
    /// 时间：beat 随 dt × bps 前进（bps = bpm / 60 × 当前倍速）；块在 beat 到达 tile.beat 时到判定线。</summary>
    public class GameCore
    {
        public const int Lanes = 4;
        public const float ViewBeats = 26f;           // 提前生成多远
        public const float Early = 0.65f;             // 最早可点（秒）
        public const float LateMin = 0.5f, LateMax = 0.8f;   // 真机手部追踪有延迟，晚点的余量要大
        public const float WrongWin = 0.22f;          // 离下一块这么近时点错轨道才算错
        /// <summary>长按：拍数 ≥ HoldMinBeats，或拍数 ≥ 1.5 且时长 ≥ 1 秒。按住到 HoldOk 比例算完整，满 HoldFull 额外加分。</summary>
        public const float HoldMinBeats = 2f, HoldOk = 0.7f, HoldFull = 0.9f;
        /// <summary>手部追踪短暂丢失 / 抖动时，这么久内不算松手。</summary>
        public const float HoldGrace = 0.3f;   // 第二十三轮：0.45 → 0.3 秒（拿开手很快就断，追踪闪一下不断）
        static readonly float[] DefaultSpeeds = { 1f, 1.2f, 1.4f };
        static readonly int[] EndlessStars = { 60, 150, 300 };

        public GameState State { get; private set; } = GameState.Idle;
        public readonly List<Tile> Tiles = new();
        /// <summary>开局以来从 Tiles 头上删掉的块数（外面按下标跟踪时用）。</summary>
        public int Trimmed { get; private set; }
        public int Cursor { get; private set; }
        public float Beat { get; private set; }
        public float Bpm { get; private set; }
        public float Mul { get; private set; } = 1f;
        public float Bps => Bpm / 60f * Mul;
        public int ScoreValue { get; private set; }
        public int ComboValue { get; private set; }
        /// <summary>倍率：连击 10 / 30 / 60 起 ×2 / ×3 / ×4。</summary>
        public int Multiplier => ComboValue >= 60 ? 4 : ComboValue >= 30 ? 3 : ComboValue >= 10 ? 2 : 1;
        /// <summary>每块基础分（× 判定 pts）、长按每 1/4 拍持续加分。</summary>
        public const int BasePoints = 100, HoldTickPoints = 20;
        public int MaxCombo { get; private set; }
        public int Hits { get; private set; }
        public int LoopIndex { get; private set; }
        public float ProgressValue { get; private set; }
        public float TotalBeats => endless ? 0 : loopBeats * speeds.Length;
        public Tile FailTile { get; private set; }
        public Tile HoldTile => holding;
        /// <summary>一轮的拍数、每小节起拍（一轮之内）、拍子（3 / 4）。无尽模式：每 4 拍一小节。</summary>
        public float LoopBeats => loopBeats;
        public float[] Bars { get; private set; } = { 0f };
        public int Meter { get; private set; } = 4;

        public bool Auto;               // 自动演奏（不计纪录）
        /// <summary>大师演奏：音块自己落下、声音由演奏层按大师谱放；玩家的敲击不出声，只判对错（跟着弹）。不计纪录。</summary>
        public bool Master;
        /// <summary>大师演奏模式下，敲中 / 敲错的次数（反馈用）。</summary>
        public int FollowHits { get; private set; }
        public int FollowWrong { get; private set; }

        /// <summary>练习（边玩边学）：下一块走到判定线就停住等你敲，敲中再接着走；时间不往前推，不会漏块。</summary>
        public bool WaitForTap;
        /// <summary>现在正停在判定线上等敲击。</summary>
        public bool Waiting { get; private set; }
        public bool Lenient;            // MR：不判失败——点错轨道、漏掉的块都只断连击（漏块照常发声），最后按表现评级
        /// <summary>宽松模式下最多漏几块（超过就失败）；&lt; 0 = 不限（默认：不判失败）。</summary>
        public int MissAllowance = -1;
        /// <summary>这一首漏掉的块数（宽松模式）。</summary>
        public int Missed => missedTiles;
        public float InputLag;          // 输入延迟补偿（秒）
        public float ResumeLead;        // 继续时让下一块至少隔这么多拍
        /// <summary>长按的“是否还按着”由输入层提供：lane → 这条轨道上触发长按的那根手指 / 那个键是否还按着。</summary>
        public Func<int, bool> IsHeld;

        readonly GameEvents ev;
        string songId;
        bool endless, autoUsed;
        float[] speeds = DefaultSpeeds;
        List<NoteEvent> events;
        IEnumerator<NoteEvent> gen;
        float loopBeats, targetMul, genBeat;
        int genLoop, genIndex, lastLane, stars, misses, bonus, holds, holdsDone, missedTiles, earned;
        float? rewindTo;
        float finishTimer = -1f;
        bool finishWin;
        Tile holding;
        float holdLost, holdTicked;
        long seed;
        readonly Dictionary<string, int> counts = new();
        GameState pausedFrom;

        public GameCore(GameEvents events) { ev = events ?? new GameEvents(); }

        public void Start(string id, float bpm, string score, float[] songSpeeds = null, bool isEndless = false)
        {
            Tiles.Clear(); Trimmed = 0;
            songId = id; endless = isEndless; Bpm = bpm;
            speeds = songSpeeds ?? DefaultSpeeds;
            Cursor = 0; Beat = -0.2f; Mul = targetMul = speeds[0];
            LoopIndex = 0; genBeat = 0; genLoop = 0; genIndex = 0; lastLane = -1; rewindTo = null;
            ScoreValue = 0; ComboValue = 0; MaxCombo = 0; stars = 0; Hits = 0; misses = 0; bonus = 0; holds = 0; holdsDone = 0; missedTiles = 0; earned = 0;
            counts.Clear(); counts["PERFECT"] = 0; counts["GREAT"] = 0; counts["GOOD"] = 0;
            autoUsed = Auto || Master; holding = null; FollowHits = 0; FollowWrong = 0; FailTile = null; finishTimer = -1f; ProgressValue = 0;
            seed = Hash(id);
            if (endless) { gen = Chart.Improv((int)(DateTime.Now.Ticks % 2000000000)).GetEnumerator(); }
            else
            {
                events = Chart.Parse(score); loopBeats = 0; foreach (var e in events) loopBeats += e.dur;
                Bars = Chart.Bars(score); Meter = Chart.Meter(Bars, loopBeats);
            }
            if (endless) { loopBeats = 4f; Bars = new[] { 0f }; Meter = 4; }
            BuildPitchMap();
            Generate();
            State = Master ? GameState.Playing : GameState.Ready;     // 大师演奏：不用敲第一块，直接开始
            if (Master) Beat = -2f;                                     // 留两拍：音块从光河远处落下来
            ev.Score(0); ev.Combo(0); ev.Progress(0);
        }

        float Rand() { seed = seed * 16807 % 2147483647; return seed / 2147483647f; }

        bool IsHoldNote(float dur) => !endless && (dur >= HoldMinBeats || (dur >= 1.5f && dur / (Bpm / 60f) >= 1f));

        void Generate()
        {
            while (genBeat < Beat + ViewBeats)
            {
                NoteEvent e; bool loopEnd = false;
                if (endless) { gen.MoveNext(); e = gen.Current; }
                else
                {
                    if (genLoop >= speeds.Length) return;
                    e = events[genIndex];
                    genIndex++;
                    if (genIndex >= events.Count) { loopEnd = true; genIndex = 0; }
                }
                // 轨道跟音高走（和钢琴一样左低右高）：同一个音永远在同一条轨道，亮的键在这条轨道正下方
                MapPitch(e.notes.Length > 0 ? e.notes[0] : 60, out int lane, out int key, out float keyPos, out int alt);
                lastLane = lane;
                var t = new Tile { beat = genBeat, dur = e.dur, notes = e.notes, seq = e.seq, lane = lane, key = key, keyPos = keyPos, altLane = alt, loop = genLoop, loopEnd = loopEnd, index = Tiles.Count, hold = IsHoldNote(e.dur) };
                Tiles.Add(t);
                genBeat += e.dur;
                if (loopEnd) genLoop++;
            }
        }

    // ---- 第十六轮（用户：这个音是 do，要按的键就该在 do 的正上方，不要随便放）----
    // 音块落在这个音真正的琴键正上方：键盘两个八度（C4~B5，14 个白键）横向等分成四条轨道，
    // 键在哪一段，就敲哪条轨道；音块画在那个键的 x 位置上。整首歌整体移若干个八度，让旋律落在键盘中间；
    // 超出两个八度的个别音按八度折进来。
    int shift;

    void BuildPitchMap()
    {
        int lo = int.MaxValue, hi = int.MinValue;
        if (events != null) foreach (var e in events) if (e.notes.Length > 0) { lo = Math.Min(lo, e.notes[0]); hi = Math.Max(hi, e.notes[0]); }
        if (lo > hi) { shift = 0; return; }
        shift = (int)Math.Round((71.5f - (lo + hi) * 0.5f) / 12f) * 12;          // 旋律中心挪到键盘中间（B4 附近）
    }

    public static int Fold(int midi)
    {
        while (midi < 60) midi += 12;
        while (midi > 83) midi -= 12;
        return midi;
    }

    /// <summary>键在键盘上的横向位置（0 = 最左 C4 的左边沿 … 1 = 最右 B5 的右边沿）：白键在自己那格的中间，黑键在两白键的交界。</summary>
    public static float KeyX(int key)
    {
        int o = (key - 60) / 12, pc = (key - 60) % 12;
        float w = pc switch { 0 => 0.5f, 1 => 1f, 2 => 1.5f, 3 => 2f, 4 => 2.5f, 5 => 3.5f, 6 => 4f, 7 => 4.5f, 8 => 5f, 9 => 5.5f, 10 => 6f, _ => 6.5f };
        return (o * 7 + w) / 14f;
    }

    public void MapPitch(int midi, out int lane, out int key) => MapPitch(midi, out lane, out key, out _, out _);

    public void MapPitch(int midi, out int lane, out int key, out float keyPos, out int alt)
    {
        key = Fold(midi + shift);
        float x4 = KeyX(key) * 4f;                       // 0..4
        lane = Math.Min(3, Math.Max(0, (int)Math.Floor(x4)));
        keyPos = x4 - 0.5f;
        alt = -1;
        float frac = x4 - lane;
        if (frac < 0.12f && lane > 0) alt = lane - 1;           // 骑在分界线上：两边都算对
        else if (frac > 0.88f && lane < 3) alt = lane + 1;
    }
        public Tile Next => Cursor < Tiles.Count ? Tiles[Cursor] : null;

        /// <summary>敲击第 lane 条轨道。</summary>
        public void Tap(int lane)
        {
            if (State != GameState.Ready && State != GameState.Playing) return;
            var next = Next;
            if (next == null) return;
            if (Master)
            {
                // 跟着大师弹：只看敲的轨道和最近要到线（或刚过线）的那块对不对，不改变演奏进度
                if (State == GameState.Ready) return;
                Tile best = null; float bestD = float.MaxValue;
                for (int i = Math.Max(0, Cursor - 3); i < Math.Min(Tiles.Count, Cursor + 3); i++)
                {
                    var t = Tiles[i];
                    float d = Math.Abs((Beat - t.beat) / Bps);
                    if (d < bestD) { bestD = d; best = t; }
                }
                bool ok = best != null && best.Accepts(lane) && bestD < 0.22f;
                if (ok) { FollowHits++; ev.Follow(lane, true, Judge.Of(bestD)); }
                else { FollowWrong++; ev.Follow(lane, false, null); }
                return;
            }
            float dt = (Beat - next.beat) / Bps - InputLag;
            if (State == GameState.Ready)
            {
                if (next.Accepts(lane)) { State = GameState.Playing; HitTile(next, Judge.Perfect); }
                else ev.PressLane(lane, true);
                return;
            }
            if (next.Accepts(lane))
            {
                if (dt < -Early) { ev.PressLane(lane, false); return; }
                HitTile(next, Judge.Of(Math.Abs(dt)));
            }
            else if (holding != null && holding.Accepts(lane))
            {
                // 正在长按的轨道上又敲了一下（手指抖动重触发）：忽略
            }
            else if (Lenient && !WaitForTap && TryHitAhead(lane)) { }
            else if (dt >= -WrongWin)
            {
                ev.PressLane(lane, true);
                if (Lenient) BreakCombo(); else FailAt(next, true);
            }
            else ev.PressLane(lane, false);
        }

        /// <summary>第三十八轮（用户：音块多、手换到别的轨道按时没反应）：宽松模式下不死守顺序——
        /// 敲的轨道和下一块对不上时，往后看 4 块，找这条轨道上正好到时间的那块：
        /// 它前面已经过了时间、又和它隔得开（> 0.25 秒）的块记漏（玩家明显跳过了）；和它几乎同时到的块不动，先算敲中的这块。</summary>
        /// <summary>游标跳过已经先敲中的块（TryHitAhead 原地标记的）。</summary>
        void SkipDone() { while (Cursor < Tiles.Count && Tiles[Cursor].state != TileState.Pending) Cursor++; }

        bool TryHitAhead(int lane)
        {
            const float Near = 0.25f;
            for (int k = Cursor + 1; k < Math.Min(Tiles.Count, Cursor + 5); k++)
            {
                var t2 = Tiles[k];
                if (t2.state != TileState.Pending || !t2.Accepts(lane)) continue;
                float dt2 = (Beat - t2.beat) / Bps - InputLag;
                if (dt2 < -Near) return false;                                      // 后面的更晚，不用再找
                if (dt2 > LateWindow(t2.dur / Bps)) continue;
                // 前面的块：要么已过时间且隔得开（记漏），要么和 t2 几乎同时（保留）；有一块两样都不是，就不能跳
                for (int j = Cursor; j < k; j++)
                {
                    var tj = Tiles[j];
                    if (tj.state != TileState.Pending) continue;
                    bool overdue = (Beat - tj.beat) / Bps - InputLag > 0f;
                    bool together = (t2.beat - tj.beat) / Bps <= Near;
                    if (!overdue && !together) return false;
                }
                while (Next != null && Next != t2 && (t2.beat - Next.beat) / Bps > Near) MissTile(Next);
                HitTile(t2, Judge.Of(Math.Abs(dt2)));                               // 几乎同时的两块：原地先算敲中的这块（不调换顺序，预约发声按下标走）
                return true;
            }
            return false;
        }

        void HitTile(Tile t, Judge j)
        {
            bool inOrder = t == Next;
            t.state = t.hold ? TileState.Holding : TileState.Hit;
            if (inOrder) { Cursor++; SkipDone(); }
            ev.PressLane(t.lane, false);
            ev.Play(t, t.dur / Bps, Bps);
            ComboValue++;
            MaxCombo = Math.Max(MaxCombo, ComboValue);
            Hits++;
            counts[j.name]++;
            earned += j.pts;
            int gain = j.pts * BasePoints * Multiplier;
            ScoreValue += gain;
            ev.Gain(t.lane, gain, false);
            ev.Judged(t, j);
            ev.Score(ScoreValue);
            ev.Combo(ComboValue);
            ev.Hit(t, j, ComboValue);
            if (t.hold)
            {
                // 上一个长按还没结算（比如快速连续的长按）：先结算
                if (holding != null) EndHold(holding);
                holding = t; holdLost = 0; t.held = 0; holds++; holdTicked = 0;
            }
            if (endless)
            {
                int s = 0; foreach (var n in EndlessStars) if (Hits >= n) s++;
                if (s > stars) { stars = s; ev.Stars(s, false); ev.Loop(s, false); }
                ProgressValue = Hits % 100 / 100f;
            }
            else
            {
                ProgressValue = Math.Min(1f, (t.beat + t.dur) / TotalBeats);
                if (t.loopEnd && !t.hold) CompleteLoop();
            }
            ev.Progress(ProgressValue);
        }

        void EndHold(Tile t)
        {
            float frac = t.dur > 0 ? t.held / t.dur : 1f;
            bool ok = frac >= HoldOk;
            t.holdDone = ok; t.holdBroken = !ok;
            t.state = TileState.Hit;
            if (ok)
            {
                holdsDone++;
                int extra = (frac >= HoldFull ? 2 : 1) * BasePoints * Multiplier;
                ScoreValue += extra;
                ev.Gain(t.lane, extra, false);
                ev.Score(ScoreValue);
            }
            else
            {
                ev.Release(t);
                earned = Math.Max(0, earned - 1);
                if (Lenient) BreakCombo();
            }
            ev.HoldEnd(t, ok);
            if (holding == t) holding = null;
            if (!endless && t.loopEnd) CompleteLoop();
        }

        /// <summary>额外加分（注视天灯）：只在演奏中有效。</summary>
        public bool AddBonus(int n)
        {
            if (State != GameState.Playing) return false;
            ScoreValue += n; bonus += n; ev.Score(ScoreValue);
            return true;
        }

        void BreakCombo()
        {
            ComboValue = 0; misses++;
            ev.Judged(null, Judge.Miss);
            ev.Combo(0);
            ev.Miss();
        }

        void CompleteLoop()
        {
            LoopIndex++;
            stars = LoopIndex;
            if (LoopIndex >= speeds.Length)
            {
                ev.Stars(stars, true);
                ev.Loop(LoopIndex, true);
                State = GameState.Won;
                finishTimer = 1.6f; finishWin = true;
                return;
            }
            targetMul = speeds[LoopIndex];
            ev.Stars(stars, false);
            ev.Loop(LoopIndex, false);
        }

        /// <summary>宽松模式漏块：标红、跳过，照常发声（旋律不断），断连击。</summary>
        void MissTile(Tile t)
        {
            missedTiles++;
            t.state = TileState.Missed;
            ev.Missed(t);
            Cursor++; SkipDone();
            ev.Play(t, t.dur / Bps, Bps);
            if (t.hold) ev.Release(t);
            BreakCombo();
            ProgressValue = endless ? ProgressValue : Math.Min(1f, (t.beat + t.dur) / TotalBeats);
            ev.Progress(ProgressValue);
            if (!endless && t.loopEnd) CompleteLoop();
        }

        void FailAt(Tile t, bool wrongLane)
        {
            State = GameState.Failing;
            FailTile = t;
            t.state = TileState.Missed;
            if (holding != null) { ev.Release(holding); holding = null; }
            ev.Fail(t);
            rewindTo = wrongLane ? Beat : t.beat - 0.6f;     // 回退一点，让玩家看清漏掉的那一块
            finishTimer = 1.5f; finishWin = false;
        }

        void Finish(bool win)
        {
            if (State != GameState.Failing && State != GameState.Won) return;
            State = GameState.Over;
            int judged = Hits + missedTiles;
            float acc = judged > 0 ? Math.Min(1f, earned / (3f * judged)) : 0f;
            ev.End(new GameResult
            {
                win = win, score = ScoreValue, stars = stars, maxCombo = MaxCombo, hits = Hits, counts = new Dictionary<string, int>(counts),
                misses = misses, bonus = bonus, songId = songId, auto = autoUsed, holds = holds, holdsDone = holdsDone,
                missedTiles = missedTiles, accuracy = acc, grade = GameResult.GradeOf(acc),
            });
        }

        void FinishMaster()
        {
            State = GameState.Over;
            ev.End(new GameResult { win = true, auto = true, songId = songId, hits = FollowHits, misses = FollowWrong, master = true, counts = new Dictionary<string, int>(counts) });
        }

        public bool Pause()
        {
            if (State == GameState.Playing || State == GameState.Ready) { pausedFrom = State; State = GameState.Paused; return true; }
            return false;
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            State = pausedFrom;
            var n = Next;
            if (ResumeLead > 0 && State == GameState.Playing && n != null) Beat = Math.Min(Beat, n.beat - ResumeLead);
        }

        public void Stop() { State = GameState.Idle; Tiles.Clear(); holding = null; }

        static float LateWindow(float durSec) => Math.Min(LateMax, Math.Max(LateMin, durSec * 0.6f));

        /// <summary>判定区（米 / 秒换算由渲染层做）：最早可点（秒）、最晚补点（秒）。</summary>
        public float LateWindowSec => LateWindow(Next != null ? Next.dur / Bps : 0);

        public void Update(float dt)
        {
            if (State == GameState.Idle || State == GameState.Paused || State == GameState.Over)
            {
                if (State == GameState.Over || State == GameState.Paused) return;
                return;
            }
            if (Auto && State == GameState.Ready && Next != null) Tap(Next.lane);
            if (State == GameState.Playing || State == GameState.Won)
            {
                if (endless) targetMul = 1f + Math.Min(1.3f, Hits / 260f);
                Mul += (targetMul - Mul) * Math.Min(1f, dt * 1.5f);
                Beat += dt * Bps;
                var w = Next;
                Waiting = WaitForTap && State == GameState.Playing && w != null && holding == null && Beat >= w.beat;
                if (Waiting) Beat = w.beat;
                Generate();
                if (Master && State == GameState.Playing)
                {
                    // 大师自己弹：块到线就算弹过（声音由演奏层按大师谱放，这里不发声）
                    var n = Next;
                    while (n != null && Beat >= n.beat) { n.state = TileState.Hit; Cursor++; ev.MasterNote(n, n.dur / Bps); ProgressValue = endless ? ProgressValue : Math.Min(1f, (n.beat + n.dur) / TotalBeats); ev.Progress(ProgressValue); n = Next; }
                    if (n == null && !endless && Beat >= loopBeats * speeds.Length - 0.01f) { FinishMaster(); return; }
                }
                else if (Auto && State == GameState.Playing)
                {
                    var n = Next;
                    if (n != null && Beat >= n.beat) Tap(n.lane);
                }
                UpdateHold(dt);
                var next = Next;
                if (State == GameState.Playing && next != null && (Beat - next.beat) / Bps > LateWindow(next.dur / Bps))
                {
                    if (Lenient && (MissAllowance < 0 || missedTiles < MissAllowance)) MissTile(next);
                    else FailAt(next, false);
                }
            }
            else if (State == GameState.Failing && rewindTo.HasValue)
            {
                Beat += (rewindTo.Value - Beat) * Math.Min(1f, dt * 6f);
            }
            if (finishTimer > 0)
            {
                finishTimer -= dt;
                if (finishTimer <= 0) Finish(finishWin);
            }
            // 回收：走过判定线很远的已击中块
            int remove = 0;
            while (remove < Tiles.Count && remove < Cursor && Tiles[remove].state == TileState.Hit && Beat - (Tiles[remove].beat + Tiles[remove].dur) > 3f) remove++;
            if (remove > 0) { Tiles.RemoveRange(0, remove); Cursor -= remove; Trimmed += remove; }
        }

        void UpdateHold(float dt)
        {
            if (holding == null) return;
            var t = holding;
            float into = Beat - t.beat;
            bool held = Auto || (IsHeld?.Invoke(t.lane) ?? false);
            if (held) { holdLost = 0; t.held = Math.Min(t.dur, Math.Max(t.held, into)); }
            else
            {
                holdLost += dt;
                if (holdLost <= HoldGrace) t.held = Math.Min(t.dur, Math.Max(t.held, into));
                else { EndHold(t); return; }
            }
            // 按着的过程中分数持续往上滚
            while (t.held >= holdTicked + 0.25f)
            {
                holdTicked += 0.25f;
                int g = HoldTickPoints * Multiplier;
                ScoreValue += g; ev.Gain(t.lane, g, true); ev.Score(ScoreValue);
            }
            if (into >= t.dur * HoldFull) { t.held = Math.Max(t.held, t.dur * HoldFull); EndHold(t); }
        }

        static long Hash(string s)
        {
            long h = 7;
            foreach (char c in s) h = (h * 31 + c) % 2147483647;
            return h == 0 ? 1 : h;
        }
    }
}
