using Twilight.Logic;
using UnityEngine;

namespace Twilight
{
    /// <summary>新手教学（第一次玩、或设置里打开“新手教学”时，在开场光河流到琴键之后、第一首之前）：
    /// 1. 认手：把手放到琴键上方——桌上每根指尖下面亮起小光点（TKFingerMarks），看到光点跟着手指走，就知道系统认到了；
    /// 2. 学敲：像敲门一样用指尖干脆地敲一下桌面，四条轨道每条都敲一次（还没敲过的轨道一闪一闪）；
    ///    没认出来的那一下会直接告诉原因：太高（没碰到桌面）、太慢、不在琴键前面、手指没抬起来；
    /// 3. 边玩边学：交给 TKSession 开第一首“练习”——音块走到判定线就停住等你敲（GameCore.WaitForTap），敲中 8 块后恢复正常节奏。
    /// 编辑器没有手：键盘 A S D F 也算敲。</summary>
    public class TKTutorial : MonoBehaviour
    {
        public HandTapInput hands;
        public GameDirector director;
        public TKFingerMarks marks;

        public bool Active { get; private set; }
        /// <summary>还在第一步（提示里说了“暂停 → 齿轮调高度”，暂停钮招手）。</summary>
        public bool AtStart => Active && step == 0;
        public Bi Message { get; private set; } = Bi.None;

        int step; float hoverT, doneAt, missUntil, beganAt; Bi missMsg = Bi.None;
        readonly bool[] lanes = new bool[4];
        static readonly int[] LaneNote = { 60, 64, 67, 72 };

        void OnEnable()
        {
            if (director) director.Tapped += OnTap;
            if (hands) hands.Missed += OnMiss;
        }

        void OnDisable()
        {
            if (director) director.Tapped -= OnTap;
            if (hands) hands.Missed -= OnMiss;
        }

        public void Begin()
        {
            Active = true; step = 0; hoverT = 0f; doneAt = -1f; missUntil = -1f; beganAt = Time.time;
            for (int i = 0; i < 4; i++) lanes[i] = false;
            if (marks) marks.strength = 1f;
            Debug.Log("[TK] 教程开始");
        }

        public void End()
        {
            Active = false; Message = Bi.None;
            if (marks) marks.strength = 0.45f;
        }

        void OnTap(int lane)
        {
            if (!Active) return;
            if ((step == 0 && Time.time - beganAt > 2.5f) || step == 3) { step = 1; return; }     // 已经会敲了：直接进第二步（第一步至少看 2.5 秒）
            if (step != 1 || lane < 0 || lane > 3) return;
            lanes[lane] = true;
            missUntil = -1f;
            director.FlashLane(lane, 1f);
            if (director.pianoAudio) director.pianoAudio.Play(LaneNote[lane], 0.6f, 1.6f);
        }

        void OnMiss(NearMiss m)
        {
            if (!Active || step == 0) return;
            missMsg = m.reason switch
            {
                "high" => new Bi("再往下一点——指尖要碰到桌面", "a bit lower — let your fingertip touch the table"),
                "slow" => new Bi("敲得再干脆一点，像敲门那样", "a little quicker, like knocking on a door"),
                "zone" => new Bi("在琴键正前方的桌面上敲", "tap the table right in front of the keys"),
                _ => new Bi("抬起手指，再敲下一下", "lift your finger before the next tap"),
            };
            missUntil = Time.time + 2.2f;
        }

        /// <summary>每帧推进；返回 true = 教完了（接着开练习曲）。</summary>
        public bool Tick(float dt)
        {
            if (!Active) return false;
            switch (step)
            {
                case 0:
                    // 第二十四轮（用户：教学里没看到“琴太高”的提示）：原来放在单独一步、一敲桌子就跳过了。现在就写在第一步第二行
                    // 第二十七轮（用户：说“齿轮里可以调”时旁边没有暂停键）：教学时暂停钮也亮出来、轻轻招手；提示指向它
                    Message = new Bi("把双手放到琴键上方\n琴的位置不对？双手捏住两边的圆钮，拖到桌面上", "hold your hands over the keys\npiano in the wrong place? pinch the two side knobs and drag it onto your table");
                    if (FingersOver() >= 2) hoverT += dt;
                    if (hoverT > 2.5f) step = 1;
                    break;
                case 1:
                    int left = 0;
                    for (int i = 0; i < 4; i++) if (!lanes[i]) { left++; director.FlashLane(i, 0.25f + 0.25f * Mathf.Sin(Time.time * 5f + i)); }
                    if (Time.time < missUntil) Message = missMsg;
                    else Message = new Bi($"像敲门一样，用指尖干脆地敲一下桌面\n每条亮着的轨道都敲一次（还差 {left} 条）",
                                          $"tap the table crisply with a fingertip, like knocking\ntry every glowing lane ({left} to go)");
                    if (left == 0) { step = 2; doneAt = Time.time; }
                    break;
                case 3:
                    // 第二十三轮（用户：边玩边学开头要告诉玩家琴的高度可以调，不要大段文字）
                    Message = new Bi("琴的位置不对？\n双手捏住两边的圆钮拖动，或在齿轮里对齐桌子", "piano in the wrong place?\npinch the two side knobs, or align it under the gear");
                    if (Time.time - doneAt > 3.2f) step = 1;
                    break;
                case 2:
                    Message = new Bi("很好！先来一首练习：方块会停在琴键上等你", "great! a practice song — the tiles will wait for you");
                    if (Time.time - doneAt > 2.4f) { Active = false; return true; }
                    break;
            }
            return false;
        }

        int FingersOver()
        {
            if (!hands || float.IsNaN(hands.contactY)) return 0;
            int n = 0;
            foreach (var j in new[] { hands.Left, hands.Right })
            {
                if (j == null) continue;
                foreach (var tip in new[] { j.indexTip, j.middleTip, j.ringTip, j.pinkyTip })
                {
                    var p = hands.ToKeys(tip);
                    if (hands.Detector.InZone(p) && p.y < 0.14f) n++;
                }
            }
            return n;
        }
    }
}
