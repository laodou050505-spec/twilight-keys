using System.Collections.Generic;
using System.Text;
using Twilight.Logic;
using UnityEditor;
using UnityEngine;

namespace Twilight.EditorTools
{
    /// <summary>玩法逻辑回归测试（菜单 Twilight/测试/玩法逻辑），结果打到控制台并返回文本。</summary>
    public static class TKLogicTests
    {
        class Catcher : GameEvents
        {
            public GameResult result; public int releases, misses, holdEnds, holdOk;
            public override void End(GameResult r) => result = r;
            public override void Release(Tile t) => releases++;
            public override void Miss() => misses++;
            public override void HoldEnd(Tile t, bool ok) { holdEnds++; if (ok) holdOk++; }
        }

        [MenuItem("Twilight/测试/玩法逻辑")]
        public static string Run()
        {
            var sb = new StringBuilder();
            int fail = 0;
            void Check(bool ok, string what) { sb.AppendLine((ok ? "✓ " : "✗ ") + what); if (!ok) fail++; }

            // 1. 整首自动演奏（每首都要能通关，长按全部完整）
            foreach (var song in SongLibrary.Load())
            {
                var c = new Catcher();
                var g = new GameCore(c) { Auto = true };
                g.Start(song.id, song.bpm, song.score, song.speeds);
                int steps = 0;
                while (c.result == null && steps < 400000) { g.Update(1f / 72f); steps++; }
                var r = c.result;
                Check(r != null && r.win && r.holdsDone == r.holds,
                    $"{song.title}：自动演奏通关={r?.win}，长按 {r?.holdsDone}/{r?.holds}，{steps / 72f:F0} 秒");
            }
            // 2. 长按 1 秒就松手（宽容模式）：松音、断连击、不失败
            {
                var c = new Catcher();
                var g = new GameCore(c) { Lenient = true, IsHeld = _ => false };
                g.Start("t", 60, "C4:2 D4 E4:2 F4");
                g.Tap(g.Next.lane);
                for (int i = 0; i < 72; i++) g.Update(1f / 72f);
                Check(c.releases == 1 && c.misses == 1 && g.State == GameState.Playing, $"长按提前松手：松音 {c.releases}、断连击 {c.misses}、状态 {g.State}");
            }
            // 3. 长按一直按着：完整
            {
                var c = new Catcher();
                var g = new GameCore(c) { IsHeld = _ => true };
                g.Start("t", 60, "C4:2 D4 E4:2 F4");
                g.Tap(g.Next.lane);
                for (int i = 0; i < 180; i++) g.Update(1f / 72f);
                Check(c.holdOk == 1 && g.ComboValue == 1, $"长按按满：完整 {c.holdOk}，连击 {g.ComboValue}");
            }
            // 4. 追踪短暂丢失（0.1 秒）不算松手
            {
                var c = new Catcher();
                bool held = true;
                var g = new GameCore(c) { IsHeld = _ => held };
                g.Start("t", 60, "C4:2 D4");
                g.Tap(g.Next.lane);
                for (int i = 0; i < 180; i++) { held = !(i > 40 && i < 47); g.Update(1f / 72f); }
                Check(c.holdOk == 1 && c.releases == 0, $"追踪丢失 0.1 秒：仍完整 {c.holdOk}，松音 {c.releases}");
            }
            // 5. 敲击检测：长按期间那根手指没抬起 → 轨道按住；抬起 → 松开
            {
                var d = new TapDetector();
                V3?[][] Tips(float y) => new[] { null, new V3?[] { null, new V3(-0.1f, y, 0f), null, null, null } };
                d.Update(0f, Tips(0.08f)); d.Update(0.05f, Tips(0.05f));
                var ev = d.Update(0.1f, Tips(0.0f));
                bool tapped = ev.Count == 1 && !ev[0].release;
                int lane = tapped ? ev[0].lane : -1;
                bool heldWhileDown = d.IsLaneHeld(lane);
                d.Update(0.2f, Tips(0.012f));
                bool stillHeld = d.IsLaneHeld(lane);
                d.Update(0.3f, Tips(0.04f));
                Check(tapped && heldWhileDown && stillHeld && !d.IsLaneHeld(lane), $"手指按住 / 抬起：敲击 {tapped}（轨道 {lane}），按着 {heldWhileDown}/{stillHeld}，抬起后 {d.IsLaneHeld(lane)}");
            }
            // 8. 快速连打：手指只抬起 1 cm 就再敲，也要每下都算（真机快歌）
            {
                var d = new TapDetector();
                V3?[][] Tips(float y) => new[] { null, new V3?[] { null, new V3(0.05f, y, 0f), null, null, null } };
                int taps = 0; float t = 0f;
                foreach (var y in new[] { 0.06f, 0.03f, 0.006f, 0.014f, 0.021f, 0.005f, 0.014f, 0.022f, 0.006f, 0.015f, 0.022f, 0.006f })
                {
                    foreach (var e in d.Update(t, Tips(y))) if (!e.release) taps++;
                    t += 0.06f;
                }
                Check(taps == 4, $"快速连打（抬 1.5 cm）：认出 {taps} / 4 下");
            }
            // 5b. 第二十三轮：长按中手横着移开（离开轨道）→ 松开；按着的手指追踪闪掉 0.1 秒、别的手指还贴在这条轨道 → 仍按着
            {
                var d = new TapDetector();
                V3?[][] One(float x, float y) => new[] { null, new V3?[] { null, new V3(x, y, 0f), null, null, null } };
                d.Update(0f, One(-0.1f, 0.08f)); d.Update(0.05f, One(-0.1f, 0.05f));
                var ev = d.Update(0.1f, One(-0.1f, 0.0f));
                int lane = ev.Count == 1 ? ev[0].lane : -1;
                d.Update(0.2f, One(-0.1f + 0.1f, 0.004f));            // 指尖贴着桌面横移 10 cm（超过一条轨道）
                bool slidAway = !d.IsLaneHeld(lane);
                var d2 = new TapDetector();
                V3?[][] Two(V3? idx, float midX) => new[] { null, new V3?[] { null, idx, new V3(midX, 0.004f, 0f), null, null } };
                d2.Update(0f, Two(new V3(-0.1f, 0.08f, 0f), -0.09f)); d2.Update(0.05f, Two(new V3(-0.1f, 0.05f, 0f), -0.09f));
                var ev2 = d2.Update(0.1f, Two(new V3(-0.1f, 0.0f, 0f), -0.09f));
                int lane2 = ev2.Count >= 1 ? ev2[0].lane : -1;
                d2.Update(0.15f, Two(null, -0.09f));                  // 食指追踪丢了，中指还贴在同一条轨道
                bool lostButHeld = d2.IsLaneHeld(lane2);
                Check(lane >= 0 && slidAway && lane2 >= 0 && lostButHeld, $"长按：手横着移开 → 松开 {slidAway}；按的手指追踪闪掉、旁边手指还贴着 → 仍按着 {lostButHeld}");
            }
            // 9. 手指搭在桌上不动（追踪抖动 ±5 mm、2 秒）：一次都不能触发；慢慢往上抬也不触发
            {
                var d = new TapDetector();
                var rnd = new System.Random(3);
                V3?[][] Tips(float y) => new[] { null, new V3?[] { null, new V3(0.05f, y, 0f), new V3(0.0f, y, 0f), new V3(-0.05f, y, 0f), null } };
                int taps = 0;
                for (int i = 0; i < 144; i++) foreach (var e in d.Update(i / 72f, Tips(0.004f + ((float)rnd.NextDouble() - 0.5f) * 0.01f))) if (!e.release) taps++;
                for (int i = 0; i < 72; i++) foreach (var e in d.Update(2f + i / 72f, Tips(0.004f + i * 0.0004f))) if (!e.release) taps++;
                Check(taps == 0, $"手指搭着不动 / 慢慢抬起：误触 {taps} 次（应为 0）");
            }
            // 10. 大拇指不参与；两根手指同时落在同一条轨道只算一次
            {
                var d = new TapDetector();
                int taps = 0;
                V3?[][] Tips(float y) => new[] { null, new V3?[] { new V3(0.05f, y, 0.05f), new V3(0.05f, y, 0f), new V3(0.06f, y, 0f), null, null } };
                foreach (var (t, y) in new[] { (0f, 0.06f), (0.05f, 0.03f), (0.1f, 0.0f) }) foreach (var e in d.Update(t, Tips(y))) if (!e.release) taps++;
                Check(taps == 1, $"大拇指不算、同轨道两指同时落：{taps} 次（应为 1）");
            }
            // 7. 轨道跟音高：左低右高、四条都用到、亮的键在那条轨道下面
            foreach (var song in SongLibrary.Load())
            {
                var g = new GameCore(new GameEvents());
                g.Start(song.id, song.bpm, song.score, song.speeds);
                bool mono = true, keyOk = true; var used = new bool[4]; int pcBad = 0;
                var byPitch = new Dictionary<int, int>();
                foreach (var e in Chart.Parse(song.score))     // 整首歌（不只是已生成的那一段）
                {
                    int p = e.notes.Length > 0 ? e.notes[0] : 60;
                    g.MapPitch(p, out int lane, out int key, out float kp, out int alt);
                    used[lane] = true;
                    if (byPitch.TryGetValue(key, out int l) && l != lane) mono = false;
                    byPitch[key] = lane;                                                           // 左低右高按“琴键”比（超出两个八度的音折进来以后，按它实际落的键）
                    if (((key - p) % 12 + 12) % 12 != 0) pcBad++;                                   // 键和音同音名（do 就是 do）
                    if ((int)Mathf.Floor(GameCore.KeyX(key) * 4f) != lane && alt < 0 && !(lane == 3 && GameCore.KeyX(key) >= 1f)) keyOk = false;   // 键在这条轨道那一段
                }
                foreach (var t in g.Tiles) { g.MapPitch(t.notes.Length > 0 ? t.notes[0] : 60, out int l2, out int _); if (l2 != t.lane) mono = false; }
                foreach (var a1 in byPitch) foreach (var b1 in byPitch) if (a1.Key < b1.Key && a1.Value > b1.Value) mono = false;
                int nUsed = (used[0] ? 1 : 0) + (used[1] ? 1 : 0) + (used[2] ? 1 : 0) + (used[3] ? 1 : 0);
                Check(mono && keyOk && pcBad == 0 && nUsed >= 2, $"{song.title}：左低右高 {mono}，音块在自己的键上 {keyOk}，同音名 {pcBad == 0}，用到 {nUsed} 条轨道");
            }
            // 6. 宽松模式不判失败：只敲第一块、其余全漏 → 照样弹完，评级 C；全部 PERFECT → S
            {
                var c = new Catcher();
                var g = new GameCore(c) { Lenient = true, IsHeld = _ => false };
                g.Start("t", 120, "C4 D4 E4 F4 G4 A4 B4 C5 D5 E5");
                g.Tap(g.Next.lane);
                int steps = 0;
                while (c.result == null && steps < 72 * 60) { g.Update(1f / 72f); steps++; }
                var r = c.result;
                Check(r != null && r.win && r.grade == "C" && r.hits == 1 && r.missedTiles == 29, $"全漏：完成 {r?.win}，评级 {r?.grade}，中 {r?.hits} 漏 {r?.missedTiles}，准确率 {r?.accuracy:P0}");
                var c2 = new Catcher();
                var g2 = new GameCore(c2) { Lenient = true, Auto = true };
                g2.Start("t", 120, "C4 D4 E4 F4 G4 A4 B4 C5 D5 E5");
                steps = 0;
                while (c2.result == null && steps < 72 * 60) { g2.Update(1f / 72f); steps++; }
                Check(c2.result != null && c2.result.grade == "S", $"全中：评级 {c2.result?.grade}，准确率 {c2.result?.accuracy:P0}");
            }
            sb.Insert(0, fail == 0 ? "全部通过\n" : $"失败 {fail} 项\n");
            Debug.Log(sb.ToString());
            return sb.ToString();
        }
    }
}
