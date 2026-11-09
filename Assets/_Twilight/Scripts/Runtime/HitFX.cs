using System;
using Twilight.Logic;
using UnityEngine;

namespace Twilight
{
    /// <summary>击中反馈（打击感）：
    /// - 判定字：每条轨道一个，在判定线上方弹出（PERFECT 金 / GREAT 浅蓝白 / GOOD 淡紫 / MISS 灰红），先放大再回弹、上浮淡出，始终朝向玩家；
    /// - 火花：PERFECT 18 粒、GREAT 10 粒、GOOD 4 粒，从击中点往上溅；长按期间判定线上持续冒火星，按满再溅一次；
    /// - 热度：连击越高光河越亮（全局 _TK_Heat），失误后很快退回；
    /// - 里程碑：连击 10 / 25 / 50 / 75 / 100 …… 中间弹出大字，光河从琴键往窗口涌一道光（_TK_Surge），响一声铃，并通知外部（天灯）。</summary>
    public class HitFX : MonoBehaviour
    {
        public GameDirector director;
        public LightRiver river;
        public BeatTrack beatTrack;
        public Material textMaterial, sparkMaterial, flareMaterial, beamMaterial, petalMaterial;
        public float hitS = 0.055f;
        public static readonly int[] Milestones = { 10, 25, 50, 75, 100, 150, 200, 300, 400, 500 };
        /// <summary>连击到里程碑时触发（参数 = 连击数）。</summary>
        public event Action<int> Milestone;

        class Label { public TextMesh tm, en; public Transform t; public float age = 9f, dur = 0.5f, size, rise; public Vector3 pos; public Color col; }
        readonly Label[] lanes = new Label[4];
        ParticleSystem sparks, petals, burst;
        /// <summary>星芒 / 光柱：每个一块自己的方片网格（颜色写在顶点色里）。</summary>
        class Glint { public Transform t; public Mesh mesh; public float age = 9f, dur, size, roll; public Color col; public Vector3 pos; public bool beam; }
        readonly Glint[] flares = new Glint[8], beams = new Glint[8];
        int flareNext, beamNext;
        readonly Color[] quadCols = new Color[4];
        Material textMat, enMat;
        Transform head;
        float heat, surge, holdSpark;
        static readonly int IdHeat = Shader.PropertyToID("_TK_Heat"), IdSurge = Shader.PropertyToID("_TK_Surge");
        static readonly Color CPerfect = new(1f, 0.88f, 0.6f), CGreat = new(0.96f, 0.94f, 0.9f), CGood = new(0.86f, 0.8f, 0.94f, 0.85f), CMiss = new(0.8f, 0.55f, 0.5f, 0.7f);
        /// <summary>判定字：毛笔写的一个字 + 下面小号英文斜体。</summary>
        static readonly string[] JudgeZh = { "错过", "不错", "很好", "完美" }, JudgeEn = { "MISS", "GOOD", "GREAT", "PERFECT" };

        void Start()
        {
            textMat = TKFont.Mat(textMaterial, 0, TKFont.Round);       // 暖光字（Twilight/GlowText）：英文大字
            enMat = TKFont.Mat(textMaterial, 0, TKFont.Kai);           // 下面一行小号中文
            for (int i = 0; i < 4; i++) lanes[i] = MakeLabel("Judge" + i);
            sparks = MakeSparks();
            petals = MakePetals();
            burst = MakeBurst();
            for (int i = 0; i < flares.Length; i++) flares[i] = MakeGlint("Flare" + i, flareMaterial, false);
            for (int i = 0; i < beams.Length; i++) beams[i] = MakeGlint("Beam" + i, beamMaterial, true);
        }

        Glint MakeGlint(string name, Material mat, bool beam)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var m = new Mesh { name = name };
            float y0 = beam ? 0f : -0.5f, y1 = beam ? 1f : 0.5f;
            m.SetVertices(new[] { new Vector3(-0.5f, y0, 0), new Vector3(0.5f, y0, 0), new Vector3(0.5f, y1, 0), new Vector3(-0.5f, y1, 0) });
            m.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetColors(new[] { Color.clear, Color.clear, Color.clear, Color.clear });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return new Glint { t = go.transform, mesh = m, beam = beam };
        }

        ParticleSystem MakePetals()
        {
            var go = new GameObject("Petals"); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.011f, 0.02f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.004f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 160;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.13f; sh.radiusThickness = 0.4f; sh.rotation = new Vector3(90f, 0, 0);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
            vel.radial = new ParticleSystem.MinMaxCurve(0.02f, 0.07f);
            vel.y = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.12f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.6f, 1, 1f));
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = petalMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!petalMaterial) r.enabled = false;
            return ps;
        }

        static readonly Color[] PetalColors = { new(1f, 0.72f, 0.8f), new(1f, 0.94f, 0.9f), new(1f, 0.8f, 0.62f), new(0.98f, 0.6f, 0.72f), new(0.85f, 0.8f, 1f) };

        /// <summary>花瓣旋涡：在 center 周围一圈往上螺旋飘起（连击里程碑 / 倍率升级）。</summary>
        public void PetalSwirl(Vector3 center, int n = 60)
        {
            if (!petals || !petalMaterial) return;
            petals.transform.SetPositionAndRotation(center, Quaternion.identity);
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int i = 0; i < n; i++) { ep.startColor = PetalColors[UnityEngine.Random.Range(0, PetalColors.Length)]; petals.Emit(ep, 1); }
        }

        static readonly Color[] Autumn = { new(0.86f, 0.27f, 0.2f), new(1f, 0.55f, 0.22f), new(1f, 0.8f, 0.32f), new(0.5f, 0.64f, 0.27f), new(1f, 0.7f, 0.78f), new(0.95f, 0.42f, 0.3f) };

        /// <summary>花瓣叶子炸开（击中 / 连击里程碑 / 倍率升级 / 评级）：从 pos 往四周（偏上）炸开，打着转慢慢飘落。</summary>
        public void PetalBurst(Vector3 pos, int n, float speed = 1f)
        {
            if (!burst || !petalMaterial || n <= 0) return;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false, position = pos };
            for (int i = 0; i < n; i++)
            {
                var dir = UnityEngine.Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.8f + 0.25f;
                ep.velocity = dir.normalized * UnityEngine.Random.Range(0.18f, 0.5f) * speed;
                ep.startColor = Autumn[UnityEngine.Random.Range(0, Autumn.Length)];
                ep.startSize = UnityEngine.Random.Range(0.007f, 0.013f) * Mathf.Lerp(0.7f, 1f, speed);
                ep.rotation = UnityEngine.Random.Range(0f, 360f);
                burst.Emit(ep, 1);
            }
        }

        ParticleSystem MakeBurst()
        {
            var go = new GameObject("PetalBurst"); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.gravityModifier = 0.035f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 3.2f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.08f; noise.frequency = 1.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = petalMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        void Flare(Vector3 pos, Color c, float size)
        {
            var g = flares[flareNext]; flareNext = (flareNext + 1) % flares.Length;
            g.pos = pos; g.col = Color.Lerp(c, Color.white, 0.35f); g.size = size; g.age = 0f; g.dur = 0.3f; g.roll = UnityEngine.Random.Range(-12f, 12f);
            g.t.gameObject.SetActive(true);
        }

        void Beam(Vector3 pos, Color c, float height)
        {
            var g = beams[beamNext]; beamNext = (beamNext + 1) % beams.Length;
            g.pos = pos; g.col = c; g.size = height; g.age = 0f; g.dur = 0.36f;
            g.t.gameObject.SetActive(true);
        }

        void UpdateGlint(Glint g, float dt)
        {
            if (g.age >= g.dur) return;
            g.age += dt;
            float k = Mathf.Clamp01(g.age / g.dur);
            if (k >= 1f) { g.t.gameObject.SetActive(false); return; }
            float a;
            if (g.beam)
            {
                float grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(g.age / 0.08f), 3f);
                a = 1f - k * k;
                g.t.position = g.pos;
                var d = head ? head.position - g.pos : Vector3.back; d.y = 0;
                if (d.sqrMagnitude > 1e-6f) g.t.rotation = Quaternion.LookRotation(-d, Vector3.up);
                g.t.localScale = new Vector3(0.034f * (1f - 0.5f * k), g.size * grow, 1f);
            }
            else
            {
                float pop = g.age < 0.06f ? Mathf.Lerp(0.3f, 1.15f, g.age / 0.06f) : Mathf.Lerp(1.15f, 0.7f, (g.age - 0.06f) / (g.dur - 0.06f));
                a = 1f - Mathf.Pow(k, 1.5f);
                g.t.position = g.pos;
                if (head) g.t.rotation = Quaternion.LookRotation(g.pos - head.position, Vector3.up) * Quaternion.Euler(0, 0, g.roll);
                g.t.localScale = Vector3.one * g.size * pop;
            }
            var c = g.col; c.a = a;
            for (int i = 0; i < 4; i++) quadCols[i] = c;
            g.mesh.SetColors(quadCols);
        }

        Label MakeLabel(string name)
        {
            var tm = TKFont.Text(name, transform, textMat, 0.005f, TKFont.Round);
            var en = TKFont.Text(name + "Zh", tm.transform, enMat, 0.0022f, TKFont.Kai);
            tm.gameObject.SetActive(false);
            return new Label { tm = tm, en = en, t = tm.transform };
        }

        ParticleSystem MakeSparks()
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.22f, 0.65f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.005f, 0.012f);
            main.gravityModifier = 0.11f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 900;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 38f; sh.radius = 0.012f;
            sh.rotation = new Vector3(-90f, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                       new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.25f));
            var damp = ps.limitVelocityOverLifetime; damp.enabled = true; damp.drag = 2.2f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = sparkMaterial; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        public void ResetAll()
        {
            heat = 0; surge = 0;
            foreach (var l in lanes) if (l != null) { l.age = 9f; l.t.gameObject.SetActive(false); }
            foreach (var g in flares) if (g != null) { g.age = 9f; g.t.gameObject.SetActive(false); }
            foreach (var g in beams) if (g != null) { g.age = 9f; g.t.gameObject.SetActive(false); }
            if (petals) petals.Clear();
            if (burst) burst.Clear();
            if (sparks) sparks.Clear();
        }

        /// <summary>击中一块：判定字 + 火花；combo 到里程碑时放大字。</summary>
        public void Judge(int lane, Judge j, int combo)
        {
            var c = j.pts == 3 ? CPerfect : j.pts == 2 ? CGreat : CGood;
            Show(lanes[0], j.pts, c, LabelPos(lane), j.pts == 3 ? 0.0046f : 0.0038f, 0.6f);
            var lc = TileField.LaneColors[lane];
            Emit(lane, j.pts == 3 ? 18 : j.pts == 2 ? 10 : 4, lc);
            if (river)
            {
                var hp = river.Point(hitS, lane, 0.012f);
                Flare(hp, lc, j.pts == 3 ? 0.13f : j.pts == 2 ? 0.09f : 0.055f);
                if (j.pts >= 2) Beam(river.Point(hitS, lane, 0.004f), lc, j.pts == 3 ? 0.34f : 0.22f);
                PetalBurst(river.Point(hitS, lane, 0.02f), j.pts == 3 ? 6 : j.pts == 2 ? 3 : 1, 0.6f);
            }
            if (Array.IndexOf(Milestones, combo) >= 0 || (combo > 500 && combo % 100 == 0))
            {
                surge = 1f;
                if (beatTrack) beatTrack.Chime();
                if (river) PetalSwirl(river.Point(hitS + 0.02f, 1.5f, 0.01f), combo >= 50 ? 90 : 60);
                Milestone?.Invoke(combo);
            }
        }

        public void Miss(int lane) => Show(lanes[0], 0, CMiss, LabelPos(lane), 0.0032f, 0.5f);

        /// <summary>长按按满：再溅一次。</summary>
        public void HoldDone(int lane)
        {
            Emit(lane, 14, TileField.LaneColors[lane]);
            if (river) Flare(river.Point(hitS, lane, 0.012f), TileField.LaneColors[lane], 0.1f);
        }

        /// <summary>判定字：琴正上方居中（英文 7 个字母放不进一条轨道），略往击中那条轨道偏一点。</summary>
        Vector3 LabelPos(int lane) => river ? river.Point(hitS + 0.13f, Mathf.Lerp(1.5f, lane, 0.25f), 0.08f) : transform.position;

        void Show(Label l, int pts, Color c, Vector3 pos, float size, float dur)
        {
            if (l == null) return;
            l.tm.text = JudgeEn[pts]; l.en.text = TKLang.En ? "" : JudgeZh[pts];     // 英文界面不再叠中文小字 l.col = c; l.pos = pos; l.size = size; l.dur = dur; l.age = 0f; l.rise = 0.022f;
            l.t.gameObject.SetActive(true);
            Animate(l, 0f);
        }

        void Emit(int lane, int n, Color laneColor)
        {
            if (river) EmitAt(river.Point(hitS, lane, 0.006f), n, laneColor);
        }

        /// <summary>在任意位置溅火花（分数、评级也用）；burst = 向四周炸开（而不是往上溅），值是速度倍数。</summary>
        public void EmitAt(Vector3 pos, int n, Color color, float burst = 0f)
        {
            if (!sparks) return;
            sparks.transform.SetPositionAndRotation(pos, Quaternion.identity);
            var ep = new ParticleSystem.EmitParams { startColor = Color.Lerp(color, Color.white, 0.45f), applyShapeToPosition = burst <= 0f };
            if (burst > 0f)
            {
                ep.position = pos;
                for (int i = 0; i < n; i++) { ep.velocity = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(0.2f, 0.55f) * burst; sparks.Emit(ep, 1); }
                return;
            }
            sparks.Emit(ep, n);
        }

        void Animate(Label l, float dt)
        {
            l.age += dt;
            float k = l.age / l.dur;
            if (k >= 1f) { l.t.gameObject.SetActive(false); return; }
            // 弹出：0.07 秒放大到 1.3 倍，再回弹到 1
            float pop = l.age < 0.07f ? Mathf.Lerp(0.6f, 1.3f, l.age / 0.07f) : Mathf.Lerp(1.3f, 1f, Mathf.Clamp01((l.age - 0.07f) / 0.12f));
            l.tm.characterSize = l.size * pop;
            float a = k < 0.55f ? 1f : 1f - (k - 0.55f) / 0.45f;
            var c = l.col; c.a *= a; l.tm.color = c;
            // 毛笔字像墨在水里化开：一出来略大略淡，很快收紧；英文小字稍晚一点浮出来
            l.en.characterSize = l.size * 0.48f;
            l.en.transform.localPosition = new Vector3(0, -l.size * 7.4f, 0);
            var ce = l.col; ce.a *= a * Mathf.Clamp01((l.age - 0.05f) / 0.12f) * 0.85f; l.en.color = ce;
            var p = l.pos + Vector3.up * (l.rise * (1f - (1f - k) * (1f - k)));
            l.t.position = p;
            if (head) l.t.rotation = Quaternion.LookRotation(p - head.position, Vector3.up);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!head && Camera.main) head = Camera.main.transform;
            TKFont.Sync();
            foreach (var f in flares) if (f != null) UpdateGlint(f, dt);
            foreach (var b in beams) if (b != null) UpdateGlint(b, dt);
            foreach (var l in lanes) if (l != null && l.age < l.dur) Animate(l, dt);
            var g = director ? director.Game : null;
            // 热度：连击 40 满；涨得慢、掉得快
            float target = g != null && g.State == GameState.Playing ? Mathf.Clamp01(g.ComboValue / 40f) : 0f;
            heat = Mathf.MoveTowards(heat, target, dt * (target > heat ? 0.6f : 3f));
            surge = Mathf.MoveTowards(surge, 0f, dt / 1.1f);
            Shader.SetGlobalFloat(IdHeat, heat);
            Shader.SetGlobalFloat(IdSurge, surge);
            // 长按期间：判定线上持续冒火星
            var ht = g?.HoldTile;
            if (ht != null && g.State == GameState.Playing)
            {
                holdSpark -= dt;
                if (holdSpark <= 0f) { holdSpark = 0.035f; Emit(ht.lane, 1, TileField.LaneColors[ht.lane]); }
            }
        }
    }
}
