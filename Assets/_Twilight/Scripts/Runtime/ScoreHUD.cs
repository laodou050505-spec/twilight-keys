using Twilight.Logic;
using UnityEngine;

namespace Twilight
{
    /// <summary>界面（方向 B：用场景里的东西“写”出来，和手绘卡通风格一致）：
    /// - 琴左边：萤火虫排成的得分。每次击中从轨道飞出一个暖光的“+300”，飞进萤火虫团里，数字重新排成新的分数（萤火虫飞过去重组），整团亮一下；
    /// - 琴右边：花瓣拼成的连击数，外面一圈花瓣慢慢绕着转；断连击时花瓣散开飘落，再重新聚起来；倍率是下面一行暖光小字 ×2；
    /// - 提示（开始 / 暂停）：琴远端上方一行暖光字（中文 + 英文），轻轻呼吸；
    /// - 结算：一块手绘叶子木牌从桌上升起，花形徽章里是评级字母；木牌上方萤火虫把分数从 0 一路“写”到最终值；木牌下部写准确率、最大连击；
    /// 所有出现 / 消失都缓入缓出。</summary>
    public class ScoreHUD : MonoBehaviour
    {
        public GameDirector director;
        public LightRiver river;
        public HitFX fx;
        public BeatTrack beatTrack;
        public Material glowMaterial, letterMaterial, fireflyMaterial, petalMaterial, boardMaterial;
        public GameObject boardModel;
        [Tooltip("提示牌：象牙瓷面 + 细金边（和八音盒 / 选曲面板同一套）")] public Material plateMaterial, plateRimMaterial;
        public float hitS = 0.055f;
        [Header("摆放（琴的本地坐标：x 右、z 朝窗；y 从桌面算）")]
        public Vector3 scoreAt = new(-0.23f, 0.15f, 0.1f);      // 分数右对齐：右边缘贴着琴的左侧
        public Vector3 comboAt = new(0.34f, 0.15f, 0.1f);
        public Vector3 messageAt = new(0f, 0.17f, 0.42f);
        public Vector3 boardAt = new(0f, 0.01f, 0.5f);      // 底部离开桌面一点（下面的字不被光河挡住）
        public float boardHeight = 0.6f;   // 第十四轮：放大一点，所有字都排进叶子里
        /// <summary>提示（会话流程设置；空 = 不显示）。</summary>
        [System.NonSerialized] public Bi message = Bi.None;
        /// <summary>结算后的提示。</summary>
        [System.NonSerialized] public Bi prompt = Bi.None;

        static readonly Color Warm = new(1f, 0.86f, 0.55f), Cream = new(1f, 0.95f, 0.84f), Firefly = new(1f, 0.88f, 0.45f), FireflyB = new(0.95f, 0.98f, 0.6f);
        static readonly Color PetalA = new(0.95f, 0.42f, 0.55f), PetalB = new(1f, 0.6f, 0.42f);

        class Pop { public TextMesh tm; public Vector3 from; public float age = 9f, dur = 0.45f; public int amount; }
        readonly Pop[] pops = new Pop[10];
        int popNext, inFlight;

        Material glowZh, glowEn, boardZh, boardEn, gradeMat;
        Transform scoreRoot, comboRoot, msgRoot, board;
        DotText scoreDots, comboDots, resultDots;
        TextMesh scoreLabel, comboLabel, multText, msgZh, msgEn;
        TextMesh gradePlus;   // S+ 的“+”：单独一个小字，挂在 S 的右上角
        TextMesh grade, infoZh, infoEn, promptZh, promptEn, stats, scoreText;
        Vector3 infoEnAt, promptEnAt;
        int langVer = -1;
        Spring gradeSpring;
        float shown, target, hudFade, msgFade, boardFade, countT;
        int lastGameScore, lastCombo, lastMult = 1;
        Bi msgShown = Bi.None;
        Transform head;

        GameResult result;
        float resAge = -1f;
        bool gradeLanded;
        public bool ResultShowing => result != null;
        public bool ResultDone => result != null && resAge >= 2.9f;

        void Start()
        {
            glowZh = TKFont.Mat(glowMaterial, 0, TKFont.Kai);
            glowEn = TKFont.Mat(glowMaterial, 0, TKFont.Round);
            boardZh = TKFont.Mat(letterMaterial, 0, TKFont.Kai);
            boardEn = TKFont.Mat(letterMaterial, 0, TKFont.Round);
            foreach (var m in new[] { boardZh, boardEn }) m.SetColor("_HaloColor", new Color(0.18f, 0.1f, 0.05f, 0.75f));
            gradeMat = TKFont.Mat(glowMaterial, 0, TKFont.Round);

            scoreRoot = new GameObject("Score").transform; scoreRoot.SetParent(transform, false);
            scoreDots = new DotText("ScoreFireflies", scoreRoot, fireflyMaterial, 640, false, Firefly, FireflyB, 0.0046f, 0.007f, 3) { height = 0.08f, anchor = 1f, roam = new Vector2(0.13f, 0.07f), idle = 14 };
            scoreLabel = TKFont.Text("Label", scoreRoot, glowZh, 0.0019f, TKFont.Kai, TextAnchor.MiddleRight);
            scoreLabel.transform.localPosition = new Vector3(0, -0.058f, 0);
            scoreLabel.text = "得分  score";

            comboRoot = new GameObject("Combo").transform; comboRoot.SetParent(transform, false);
            comboDots = new DotText("ComboPetals", comboRoot, petalMaterial, 320, true, PetalA, PetalB, 0.0042f, 0.006f, 5) { height = 0.11f, roam = new Vector2(0.1f, 0.075f), idle = 34 };
            comboLabel = Text(comboRoot, "Label", glowZh, 0.0019f, TKFont.Kai, new Vector3(0, -0.105f, 0));
            comboLabel.text = "连击  combo";
            multText = Text(comboRoot, "Mult", glowEn, 0.0034f, TKFont.Round, new Vector3(0, -0.085f, 0));

            msgRoot = new GameObject("Message").transform; msgRoot.SetParent(transform, false);
            // 第二十四轮（用户：教学提示的字看不清）：不再是浮在空中的暖光字（透视里亮的房间一衬就看不清），
            // 改成一块象牙瓷牌（细金边）上的深褐墨字，牌子宽度跟着字的长短变
            var ink = TKFont.Mat(letterMaterial, 0, TKFont.Kai); ink.SetColor("_HaloColor", new Color(0, 0, 0, 0));
            msgZh = Text(msgRoot, "Zh", ink, 0.0032f, TKFont.Kai, new Vector3(0, 0.012f, 0));
            msgEn = Text(msgRoot, "En", ink, 0.0028f, TKFont.Round, new Vector3(0, 0.012f, 0));
            plate = MakePlate("Plate", plateMaterial, 0f); plateRim = MakePlate("PlateRim", plateRimMaterial ? plateRimMaterial : plateMaterial, 0.0012f);

            // 结算木牌
            board = new GameObject("ResultBoard").transform; board.SetParent(transform, false);
            if (boardModel)
            {
                var m = Instantiate(boardModel, board, false); m.name = "Model";
                m.transform.localRotation = Quaternion.Euler(0, 180f, 0) * m.transform.localRotation;   // FBX 正面朝 +Z，转过来
                m.transform.localScale *= boardHeight;
                foreach (var r in m.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = boardMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            }
            float front = -0.06f * boardHeight / 2f - 0.002f;
            grade = Text(board, "Grade", gradeMat, 0.0085f, TKFont.Round, new Vector3(0, boardHeight * 0.575f, front));
            gradePlus = Text(grade.transform, "GradePlus", gradeMat, 0.0085f * 0.6f, TKFont.Round, Vector3.zero);
            // 第十四轮（用户：结算面板的字不要出到面板外面）：分数、歌名、统计排在徽章下面，提示排在徽章上面，
            // 每行都按叶子在那个高度的宽度自动缩小（Fit），萤火虫也不再满天飞
            infoZh = Text(board, "InfoZh", boardZh, 0.0034f, TKFont.Kai, new Vector3(0, boardHeight * 0.262f, front));
            infoEn = Text(board, "InfoEn", boardEn, 0.0034f, TKFont.Round, new Vector3(0, boardHeight * 0.262f, front));
            stats = Text(board, "Stats", boardZh, 0.003f, TKFont.Kai, new Vector3(0, boardHeight * 0.198f, front));
            scoreText = Text(board, "ScoreText", boardZh, 0.0068f, TKFont.Kai, new Vector3(0, boardHeight * 0.352f, front));   // 分数刻在牌上（萤火虫光点叠在亮木头上看不清）
            promptZh = Text(board, "PromptZh", boardZh, 0.003f, TKFont.Kai, new Vector3(0, boardHeight * 0.765f, front));
            promptEn = Text(board, "PromptEn", boardEn, 0.003f, TKFont.Round, new Vector3(0, boardHeight * 0.765f, front));
            infoEnAt = infoEn.transform.localPosition; promptEnAt = promptEn.transform.localPosition;
            resultDots = new DotText("ResultFireflies", board, fireflyMaterial, 640, false, Firefly, FireflyB, 0.0034f, 0.0052f, 9) { height = 0.055f, roam = new Vector2(0.05f, 0.015f), idle = 0 };
            resultDots.root.localPosition = new Vector3(0, boardHeight * 0.352f, front - 0.003f);   // 分数写在徽章下面、叶子最宽处
            board.gameObject.SetActive(false);

            for (int i = 0; i < pops.Length; i++) { pops[i] = new Pop { tm = Text(transform, "Gain" + i, glowEn, 0.0034f, TKFont.Round, Vector3.zero) }; pops[i].tm.gameObject.SetActive(false); }
            gradeSpring.value = 1f;
            scoreDots.SetText("0"); comboDots.SetText("0");
        }

        TextMesh Text(Transform parent, string name, Material m, float size, Font f, Vector3 at)
        {
            var t = TKFont.Text(name, parent, m, size, f);
            t.transform.localPosition = at;
            return t;
        }

        Vector3 Local(Vector3 l) => river.transform.TransformPoint(l + new Vector3(0, river.tableTop, 0));

        /// <summary>朝向玩家（只转身，微微仰起）。</summary>
        void Face(Transform t, Vector3 pos, float pitch = 0.5f)
        {
            t.position = pos;
            var d = head ? pos - head.position : river.transform.forward;
            d.y *= pitch;
            if (d.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        // ---------- 外部调用 ----------
        public void Gain(int lane, int amount, bool tick)
        {
            if (tick || !river) { target += amount; return; }
            var p = pops[popNext]; popNext = (popNext + 1) % pops.Length;
            if (p.age < p.dur) { target += p.amount; inFlight--; }
            p.amount = amount; p.age = 0f;
            p.from = river.Point(hitS + 0.03f, lane, 0.05f);
            p.tm.text = "+" + amount;
            p.tm.gameObject.SetActive(true);
            inFlight++;
        }

        public void ShowResult(GameResult r, string songId)
        {
            result = r; resAge = 0f; gradeLanded = false; countT = 0f;
            grade.text = r.grade == "S+" ? "S" : r.grade ?? "";
            gradePlus.text = r.grade == "S+" ? "+" : "";   // 第四十二轮：S+ 的加号小一号、挂在 S 的右上角
            int acc = Mathf.RoundToInt(r.accuracy * 100);
            var song = SongLibrary.Find(songId);
            if (r.master)
            {
                // 大师演奏：没有分数评级，只说跟弹了多少
                grade.text = ""; gradePlus.text = "";                           // 不评级
                int tot = r.hits + r.misses;
                infoZh.text = TKLang.En ? "" : (song != null ? song.title : "");
                infoEn.text = TKLang.En ? SongLibrary.EnglishTitle(songId) : "";
                stats.text = TKLang.En ? "played by the master" + (tot > 0 ? $"  ·  {r.hits} in step" : "") : "大师演奏" + (tot > 0 ? $"  ·  跟上了 {r.hits} 下" : "");
                FitAll();
                SetScore(tot > 0 ? r.hits.ToString() : "");
                resultDots.SetText("");
                return;
            }
            // 一次只写一种语言（设置里切）；另一个对象留空
            infoZh.text = TKLang.En ? "" : (song != null ? song.title : "");
            infoEn.text = TKLang.En ? SongLibrary.EnglishTitle(songId) : "";
            stats.text = TKLang.En ? $"accuracy {acc}%  ·  best combo {r.maxCombo}" : $"准确率 {acc}%  ·  最大连击 {r.maxCombo}";
            FitAll();
            SetScore("0");
            resultDots.SetText("");
        }

    // ---- 结算木牌：每行字限宽（叶子在各高度的可写宽度，按 boardHeight = 0.5 量的，留了边）----
    float TitleW => boardHeight * 0.4f;
    float StatsW => boardHeight * 0.31f;
    float PromptW => boardHeight * 0.32f;
    float ScoreW => boardHeight * 0.44f;

    /// <summary>字太长就整行等比缩小，保证落在叶子里（不换行、不出边）。</summary>
    static void Fit(TextMesh t, float size, float maxW)
    {
        t.characterSize = size;
        if (string.IsNullOrEmpty(t.text)) return;
        float w = t.GetComponent<MeshRenderer>().localBounds.size.x;
        if (w > maxW && w > 1e-5f) t.characterSize = size * maxW / w;
    }

    void SetScore(string v) { if (scoreText.text == v) return; scoreText.text = v; Fit(scoreText, 0.0068f, ScoreW); }

    void FitAll()
    {
        Fit(infoZh, 0.0034f, TitleW); Fit(infoEn, 0.0034f, TitleW); Fit(stats, 0.003f, StatsW);
    }

    /// <summary>萤火虫写的分数：按最终的字符串定字高（位数多就写小一点），从 0 数到最终值都在叶子里。</summary>
    void ScoreHeight(string final)
    {
        float units = 0f;
        foreach (char c in final) units += (c >= '0' && c <= '9' ? 0.62f : 0.32f) + 0.06f;
        resultDots.height = Mathf.Min(0.056f, ScoreW / Mathf.Max(1f, units));
    }
        public void HideResult()
        {
            result = null; resAge = -1f; prompt = Bi.None;
            shown = target = 0f; lastGameScore = 0; inFlight = 0;
            foreach (var p in pops) if (p != null) { p.age = 9f; p.tm.gameObject.SetActive(false); }
            if (scoreDots != null) { scoreDots.SetText("0"); comboDots.SetText("0"); }
        }

        void Update()
        {
            if (!river || !director || scoreDots == null) return;
            if (!head && Camera.main) head = Camera.main.transform;
            TKFont.Sync();
            if (langVer != TKLang.Version)
            {
                langVer = TKLang.Version;
                scoreLabel.text = TKLang.Pick("得分", "score"); comboLabel.text = TKLang.Pick("连击", "combo");
                msgShown = Bi.None;                // 提示按新语言重写
            }
            float dt = Time.deltaTime;
            gradeSpring.Step(dt, 260f, 13f);
            var g = director.Game;
            bool placed = river.gameObject.activeInHierarchy;

            // ---- 得分 / 连击：摆好琴后一直在（开场时跟光河一起淡入；结算时让位给木牌）----
            bool live = placed && result == null && g != null && g.State != GameState.Idle;
            hudFade = Mathf.MoveTowards(hudFade, live ? 1f - river.hide : 0f, dt * 1.5f);
            float hf = Mathf.SmoothStep(0f, 1f, hudFade);
            scoreRoot.gameObject.SetActive(hf > 0.001f); comboRoot.gameObject.SetActive(hf > 0.001f);
            Face(scoreRoot, Local(scoreAt)); Face(comboRoot, Local(comboAt));
            scoreDots.visible = hf; comboDots.visible = hf;

            if (g != null)
            {
                if (g.ScoreValue < lastGameScore) { shown = target = 0f; inFlight = 0; }
                lastGameScore = g.ScoreValue;
                if (inFlight <= 0 && target < g.ScoreValue) target = g.ScoreValue;
            }
            for (int i = 0; i < pops.Length; i++) UpdatePop(pops[i], dt);
            // 分数：每次落地后跳到新值（萤火虫重组）；长按持续加分时每 0.12 秒重排一次
            if (target > shown) { countT -= dt; if (countT <= 0f) { shown = target; countT = 0.12f; } }
            scoreDots.SetText(Mathf.FloorToInt(shown).ToString("N0"));
            scoreDots.Update(dt);
            TKFont.Alpha(scoreLabel, Warm, hf * 0.55f);

            int c = g != null ? g.ComboValue : 0, mult = g != null ? g.Multiplier : 1;
            if (c == 0 && lastCombo >= 3) { comboDots.Scatter(); }
            if (c > lastCombo)
            {
                bool milestone = System.Array.IndexOf(HitFX.Milestones, c) >= 0;
                comboDots.Kick(milestone ? 1f : 0.45f);
                if (milestone && fx) fx.PetalBurst(comboRoot.position, 40);
            }
            if (c != lastCombo && (c > 0 || comboDots.Text == null)) comboDots.SetText(c.ToString());
            lastCombo = c;
            comboDots.Update(dt);
            TKFont.Alpha(comboLabel, Warm, hf * 0.55f);
            multText.text = mult > 1 ? "×" + mult : "";
            TKFont.Alpha(multText, Warm, hf);
            if (mult > lastMult && fx) { fx.PetalBurst(comboRoot.position, 60); if (beatTrack) beatTrack.Chime(0.4f); }
            lastMult = mult;

            UpdateMessage(dt, placed);
            UpdateBoard(dt);
        }

        void UpdatePop(Pop p, float dt)
        {
            if (p.age >= p.dur) return;
            p.age += dt;
            float k = Mathf.Clamp01(p.age / p.dur);
            var to = scoreRoot.position;
            var ctrl = (p.from + to) * 0.5f + Vector3.up * 0.08f;
            float e = k * k * (3f - 2f * k);
            var pos = Vector3.Lerp(Vector3.Lerp(p.from, ctrl, e), Vector3.Lerp(ctrl, to, e), e);
            p.tm.characterSize = 0.0034f * (k < 0.12f ? Mathf.Lerp(0.7f, 1.25f, k / 0.12f) : Mathf.Lerp(1.25f, 0.5f, (k - 0.12f) / 0.88f));
            TKFont.Alpha(p.tm, p.amount >= 600 ? Warm : Cream, k < 0.6f ? 1f : Mathf.Clamp01(1f - (k - 0.6f) / 0.35f));
            Face(p.tm.transform, pos, 1f);
            if ((Time.frameCount & 1) == 0 && fx) fx.EmitAt(pos, 1, Firefly, 0.05f);
            if (k >= 1f)
            {
                p.tm.gameObject.SetActive(false);
                target += p.amount; inFlight = Mathf.Max(0, inFlight - 1);
                countT = 0f;
                scoreDots.Kick(0.6f + Mathf.Clamp01(p.amount / 1200f) * 0.6f);
            }
        }

    // ---- 提示瓷牌 ----
    Transform plate, plateRim;
    static readonly Color Ink = new(0.2f, 0.11f, 0.05f);

    Transform MakePlate(string n, Material m, float rim)
    {
        var g = new GameObject(n);
        g.transform.SetParent(msgRoot, false);
        g.AddComponent<MeshFilter>();
        var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        g.transform.localPosition = new Vector3(0, 0, rim > 0 ? 0.0052f : 0.0045f);
        return g.transform;
    }

    /// <summary>按当前字的大小重做瓷牌：左右各留 2.2 cm、上下各 1.3 cm，两头半圆；金边比瓷面大一圈。</summary>
    void SizePlate()
    {
        var t = TKLang.En ? msgEn : msgZh;
        var b = t.GetComponent<MeshRenderer>().localBounds;
        float w = Mathf.Max(0.08f, b.size.x + 0.044f), h = Mathf.Max(0.034f, b.size.y + 0.026f);
        var c = new Vector3(b.center.x, b.center.y + t.transform.localPosition.y, 0);
        if (plate) { plate.GetComponent<MeshFilter>().sharedMesh = Pill(w, h, 0.004f); plate.localPosition = new Vector3(c.x, c.y, 0.0045f); }
        if (plateRim) { plateRim.GetComponent<MeshFilter>().sharedMesh = Pill(w + 0.004f, h + 0.004f, 0.003f); plateRim.localPosition = new Vector3(c.x, c.y, 0.0062f); }
    }

    /// <summary>圆头长条（两头半圆）：正面在 z = −depth/2、朝 −Z。</summary>
    public static Mesh Pill(float w, float h, float depth)
    {
        var outline = new System.Collections.Generic.List<Vector2>();
        float r = h / 2; const int seg = 16;
        for (int side = 0; side < 2; side++)
            for (int k = 0; k <= seg; k++)
            {
                float a = (side == 0 ? -90f : 90f) + 180f * k / seg;
                var c = new Vector2(side == 0 ? w / 2 - r : -w / 2 + r, 0);
                outline.Add(c + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * r);
            }
        int n = outline.Count;
        var v = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>(); var t = new System.Collections.Generic.List<int>();
        float z0 = -depth / 2, z1 = depth / 2;
        v.Add(new Vector3(0, 0, z0)); uv.Add(new Vector2(0.5f, 0.5f));
        foreach (var p in outline) { v.Add(new Vector3(p.x, p.y, z0)); uv.Add(new Vector2(p.x / w + 0.5f, p.y / h + 0.5f)); }
        for (int i = 0; i < n; i++) t.AddRange(new[] { 0, 1 + (i + 1) % n, 1 + i });
        int s0 = v.Count;
        for (int i = 0; i < n; i++) { v.Add(new Vector3(outline[i].x, outline[i].y, z0)); v.Add(new Vector3(outline[i].x, outline[i].y, z1)); uv.Add(Vector2.zero); uv.Add(Vector2.one); }
        for (int i = 0; i < n; i++) { int a = s0 + i * 2, b = s0 + ((i + 1) % n) * 2; t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 }); }
        var m = new Mesh { name = "Pill" };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }
        /// <summary>提示：琴远端上方一行暖光字；换字时先淡出再淡入。</summary>
        void UpdateMessage(float dt, bool placed)
        {
            bool want = placed && result == null && !message.Empty;
            bool changing = want && !msgShown.Empty && !message.Same(msgShown);
            msgFade = Mathf.MoveTowards(msgFade, want && !changing ? 1f : 0f, dt * 2.5f);
            if (want && (msgShown.Empty || (changing && msgFade <= 0.001f))) { msgShown = message; msgZh.text = TKLang.En ? "" : message.zh; msgEn.text = TKLang.En ? message.en : ""; msgEn.transform.localPosition = new Vector3(0, 0.012f, 0); SizePlate(); }
            bool vis = msgFade > 0.001f;
            if (msgRoot.gameObject.activeSelf != vis) msgRoot.gameObject.SetActive(vis);
            if (!vis) { if (!want) msgShown = Bi.None; return; }
            float e = Mathf.SmoothStep(0f, 1f, msgFade);
            Face(msgRoot, Local(messageAt + new Vector3(0, 0.01f * (1 - e), 0)));
            TKFont.Alpha(msgZh, Ink, e); TKFont.Alpha(msgEn, Ink, e);
            // 瓷牌像卷轴一样从中间展开（不透明的瓷不能淡入）
            if (plate) plate.localScale = new Vector3(Mathf.Lerp(0.02f, 1f, e), 1f, 1f);
            if (plateRim) plateRim.localScale = new Vector3(Mathf.Lerp(0.02f, 1f, e), 1f, 1f);
        }

        /// <summary>结算：木牌从桌上升起（缩放 + 上浮），萤火虫写分数，徽章里的评级字母弹出来（花瓣炸开 + 铃声），再写准确率 / 连击。</summary>
        void UpdateBoard(float dt)
        {
            boardFade = Mathf.MoveTowards(boardFade, result != null ? 1f : 0f, dt * 2f);
            bool vis = boardFade > 0.001f;
            if (board.gameObject.activeSelf != vis) board.gameObject.SetActive(vis);
            if (!vis) return;
            float e = Mathf.SmoothStep(0f, 1f, boardFade);
            var pos = Local(boardAt);
            board.position = pos;
            var d = head ? pos - head.position : river.transform.forward; d.y = 0;
            if (d.sqrMagnitude > 1e-6f) board.rotation = Quaternion.LookRotation(d, Vector3.up) * Quaternion.Euler(-6f, 0, 0);
            board.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, e) * (result != null && resAge > 0f && resAge < 0.5f ? 1f + 0.04f * Mathf.Sin(resAge * 25f) * (0.5f - resAge) : 1f);
            resultDots.visible = e;
            resultDots.Update(dt);
            if (result == null) { foreach (var t in new[] { grade, gradePlus, infoZh, infoEn, stats, scoreText, promptZh, promptEn }) TKFont.Alpha(t, t.color, 0f); return; }

            resAge += dt;
            // 分数：0.5 ~ 2.0 秒，萤火虫从 0 一路写到最终值（越到后面越慢），嗒嗒声越来越高
            float k = Mathf.Clamp01((resAge - 0.5f) / 1.5f);
            float ek = 1f - Mathf.Pow(1f - k, 3f);
            countT -= dt;
            if (resAge > 0.5f && countT <= 0f)
            {
                countT = k < 1f ? Mathf.Lerp(0.06f, 0.14f, k) : 9f;
                int val = result.master ? result.hits : result.score;          // 大师演奏：写跟上的下数
                SetScore(result.master && val == 0 ? "" : Mathf.FloorToInt(val * ek).ToString("N0"));
                if (beatTrack && k < 1f) beatTrack.Tick(Mathf.Lerp(1f, 1.7f, k), 0.12f);
                if (k >= 1f) resultDots.Kick(1f);
            }
            // 评级：2.0 秒从徽章里弹出来
            float st = resAge - 2.0f;
            if (st >= 0f)
            {
                float s = st < 0.14f ? Mathf.Lerp(0.2f, 1.25f, st / 0.14f) : gradeSpring.value;
                if (st >= 0.14f && !gradeLanded)
                {
                    gradeLanded = true; gradeSpring.value = 1.25f; gradeSpring.Kick(-2f);
                    if (fx) fx.PetalBurst(grade.transform.position, 70);
                    if (beatTrack) beatTrack.Chime(0.6f);
                }
                grade.characterSize = 0.0085f * s;
                TKFont.Alpha(grade, new Color(1f, 0.93f, 0.7f), Mathf.Clamp01(st / 0.08f) * e);
                gradePlus.characterSize = 0.0085f * 0.62f * s;
                gradePlus.transform.localPosition = new Vector3(0.0085f * 3.0f * s, 0.0085f * 2.1f * s, 0f);   // 用户：加号别离 S 太远
                TKFont.Alpha(gradePlus, new Color(1f, 0.93f, 0.7f), Mathf.Clamp01(st / 0.08f) * e);
            }
            else { TKFont.Alpha(grade, Color.white, 0f); TKFont.Alpha(gradePlus, Color.white, 0f); }
            TKFont.Alpha(infoZh, Cream, e * Mathf.Clamp01((resAge - 2.3f) / 0.4f));
            TKFont.Alpha(infoEn, Cream, e * Mathf.Clamp01((resAge - 2.3f) / 0.4f));
            TKFont.Alpha(stats, Warm, e * Mathf.Clamp01((resAge - 2.4f) / 0.4f));
            TKFont.Alpha(scoreText, new Color(1f, 0.9f, 0.62f), e * Mathf.Clamp01((resAge - 0.4f) / 0.3f));
            string pz = TKLang.En ? "" : prompt.zh, pe = TKLang.En ? prompt.en : "";
            if (promptZh.text != pz || promptEn.text != pe) { promptZh.text = pz; promptEn.text = pe; Fit(promptZh, 0.003f, PromptW); Fit(promptEn, 0.003f, PromptW); }
            float pa = prompt.Empty ? 0f : e * (0.65f + 0.3f * Mathf.Sin(Time.time * 2.4f));
            TKFont.Alpha(promptZh, Cream, pa); TKFont.Alpha(promptEn, Cream, pa);
        }
    }
}
