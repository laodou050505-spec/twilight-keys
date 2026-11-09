using System.Collections.Generic;
using Twilight.Logic;
using UnityEngine;

namespace Twilight
{
    /// <summary>跟着音乐铺开世界：
    /// - 每击中一个音，铺开就往前“涌”一步（约 0.4 拍内缓出），两次击中之间不动——完全跟着弹奏的节奏；
    /// - 目标按进度分三轮：第一轮慢（到 22%）、第二轮快（到 60%）、第三轮在进度 88% 左右铺满（副歌 / 高潮）；
    /// - 铺开过了 dissolveFrom（35%）窗户、石墙、百叶窗、花箱就开始一点点化成光点散去（从下往上），铺满时正好散完，最后只剩琴；
    /// - 天色随进度从黄昏走到入夜；每拍一次脉冲（_TK_BeatPulse），光边和光河跟着闪。</summary>
    public class RevealDirector : MonoBehaviour
    {
        public WorldReveal reveal;
        public TwilightSky sky;
        [Tooltip("铺满后要消散的物体（窗户、石墙、百叶窗、花箱）")]
        public Renderer[] dissolve;
        [Tooltip("窗玻璃（透明材质，淡出）")]
        public Material glass;
        [Tooltip("消散材质模板（引用 Twilight/LitDissolve，保证着色器被打进包；只按名字 Shader.Find 的话打包会被裁掉，真机上消散失效、到时候一下子全没了）")] public Material dissolveTemplate;
        public float surgeBeats = 0.4f;
        public float dissolveSeconds = 3.5f;
        [Tooltip("铺开到这个比例就开始消散石墙 / 窗户（之后跟着铺开一点点散掉）")] [Range(0, 1)] public float dissolveFrom = 0.35f;
        /// <summary>当前铺开程度（0..1，环境音等跟着变）。</summary>
        public float Progress => cur;
        /// <summary>窗户 / 石墙消散到哪了（0 完整 … 1 散完；歌名牌跟着散）。</summary>
        public float WindowFade => fade;

        float from, to, surgeT = 1f, cur, fade, glassAlpha = -1;
        bool dissolving, finale;
        [Tooltip("没击中也会跟着歌曲进度慢慢铺开到目标的这个比例（击中再往前涌）")] public float baseline = 0.75f;
        int lastBeat = int.MinValue;
        readonly List<(Renderer r, Material[] orig)> swapped = new();
        static readonly int IdFade = Shader.PropertyToID("_TK_WindowFade"), IdPulse = Shader.PropertyToID("_TK_BeatPulse"), IdPhase = Shader.PropertyToID("_TK_BeatPhase");
        float pulse;

        /// <summary>进度 → 铺开目标（三轮前慢后快，88% 铺满）。</summary>
        public static float Curve(float p)
        {
            if (p < 1f / 3f) return 0.22f * Mathf.Pow(p * 3f, 1.25f);
            if (p < 2f / 3f) return Mathf.Lerp(0.22f, 0.6f, (p - 1f / 3f) * 3f);
            return Mathf.Lerp(0.6f, 1f, Mathf.Clamp01((p - 2f / 3f) / (0.88f - 2f / 3f)));
        }

        public void ResetAll()
        {
            from = to = cur = 0; surgeT = 1; fade = 0; dissolving = false; finale = false; pulse = 0; lastBeat = int.MinValue;
            Apply();
            RestoreMaterials();
        }

        /// <summary>一首结束：不论打得怎样，把世界缓缓铺满、天色走到入夜（谢幕，让玩家多看一会儿）。</summary>
        public void Finale() { finale = true; }

        /// <summary>击中一个音：铺开往前涌一步。</summary>
        public void OnHit(float progress)
        {
            float target = Curve(progress);
            if (target <= to + 1e-4f) return;
            from = cur; to = target; surgeT = 0;
        }

        /// <summary>每帧：beat = 当前拍位，bps = 每秒拍数。</summary>
        public void Tick(float beat, float bps, float progress, float dt)
        {
            if (surgeT < 1f)
            {
                surgeT = Mathf.Min(1f, surgeT + dt * bps / surgeBeats);
                float e = 1f - Mathf.Pow(1f - surgeT, 3f);
                cur = Mathf.Lerp(from, to, e);
            }
            // 没击中也跟着歌曲进度慢慢铺（不会停在一半）；结束时铺满
            float floor = finale ? 1f : Curve(progress) * baseline;
            if (cur < floor && surgeT >= 1f) { cur = Mathf.MoveTowards(cur, floor, dt * (finale ? 0.22f : 0.06f)); from = to = Mathf.Max(to, cur); }
            // 拍点脉冲
            int b = Mathf.FloorToInt(beat);
            if (b != lastBeat) { lastBeat = b; pulse = 1f; }
            pulse = Mathf.MoveTowards(pulse, 0f, dt * bps * 2.2f);
            Shader.SetGlobalFloat(IdPulse, pulse);
            Shader.SetGlobalFloat(IdPhase, beat - Mathf.Floor(beat));
            // 石墙 / 窗户跟着铺开逐渐消散：铺开过了 dissolveFrom 就开始，一点一点化成光点（从下往上），铺满时正好散完
            if (!dissolving && cur >= dissolveFrom && dissolve != null && dissolve.Length > 0) BeginDissolve();
            if (dissolving)
            {
                float want = Mathf.Clamp01((cur - dissolveFrom) / (1f - dissolveFrom));
                fade = Mathf.MoveTowards(fade, Mathf.Max(fade, want), dt / dissolveSeconds);
            }
            if (sky) sky.progress = Mathf.MoveTowards(sky.progress, finale ? 2f : Mathf.Clamp01(progress) * 2f, dt * 0.25f);
            Apply();
        }

        void Apply()
        {
            if (reveal) { reveal.reveal = cur; reveal.Apply(); }
            Shader.SetGlobalFloat(IdFade, fade);
            if (glass)
            {
                if (glassAlpha < 0) glassAlpha = glass.GetColor("_BaseColor").a;
                var c = glass.GetColor("_BaseColor"); c.a = glassAlpha * Mathf.Clamp01(1f - fade * 2f); glass.SetColor("_BaseColor", c);
            }
            if (fade >= 1f && dissolve != null) foreach (var r in dissolve) if (r && r.enabled) r.enabled = false;
        }

        /// <summary>把要消散的物体换成消散材质（拷贝原贴图 / 颜色），记下高度范围（从下往上散）。</summary>
        public void BeginDissolve()   // 公开：编辑器预览消散
        {
            dissolving = true;
            var sh = dissolveTemplate ? dissolveTemplate.shader : Shader.Find("Twilight/LitDissolve");
            if (!sh) { Debug.LogWarning("[TK] 找不到消散着色器，石墙 / 窗户只能直接隐藏"); return; }
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var r in dissolve) if (r) { minY = Mathf.Min(minY, r.bounds.min.y); maxY = Mathf.Max(maxY, r.bounds.max.y); }
            swapped.Clear();
            foreach (var r in dissolve)
            {
                if (!r) continue;
                var orig = r.sharedMaterials;
                var mats = new Material[orig.Length];
                for (int i = 0; i < orig.Length; i++)
                {
                    var o = orig[i];
                    if (o == glass) { mats[i] = o; continue; }
                    var m = new Material(sh) { name = o ? o.name + " (消散)" : "消散" };
                    if (o)
                    {
                        if (o.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", o.GetTexture("_BaseMap")); m.SetTextureScale("_BaseMap", o.GetTextureScale("_BaseMap")); }
                        if (o.HasProperty("_BaseColor")) m.SetColor("_BaseColor", o.GetColor("_BaseColor"));
                        if (o.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", o.GetFloat("_Smoothness"));
                        if (o.HasProperty("_Cull")) m.SetFloat("_Cull", o.GetFloat("_Cull"));
                        if (o.IsKeywordEnabled("_EMISSION")) { m.SetTexture("_EmissionMap", o.GetTexture("_EmissionMap")); m.SetColor("_EmissionColor", o.GetColor("_EmissionColor")); }
                    }
                    m.SetFloat("_MinY", minY); m.SetFloat("_MaxY", maxY);
                    mats[i] = m;
                }
                swapped.Add((r, orig));
                r.sharedMaterials = mats;
            }
        }

        void RestoreMaterials()
        {
            foreach (var (r, orig) in swapped) if (r) { r.sharedMaterials = orig; r.enabled = true; }
            swapped.Clear();
            if (dissolve != null) foreach (var r in dissolve) if (r) r.enabled = true;
            if (glass && glassAlpha >= 0) { var c = glass.GetColor("_BaseColor"); c.a = glassAlpha; glass.SetColor("_BaseColor", c); }
        }

        void OnDisable() { Shader.SetGlobalFloat(IdFade, 0); RestoreMaterials(); }
    }
}
