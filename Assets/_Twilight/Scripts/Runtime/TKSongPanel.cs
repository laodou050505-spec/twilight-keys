using System.Collections.Generic;
using UnityEngine;

namespace Twilight
{
    /// <summary>曲目面板：八音盒左边立着的一块木板（和盒子同一套清漆木 + 金边），盒子打开就跟着展开。
    /// 上面一排四个分类（古典 / 浪漫 / 民歌 / 童谣），中间一页 6 首（曲名 + 难度星），底下翻页和模式切换：
    /// “我来弹”（正常玩：敲音块出声、计分）/“大师演奏”（音块自己落下，大师按完整编曲演奏；可以跟着敲，不出声，只有对错反馈）。
    /// 全部指尖戳（同八音盒按钮：要先退开才能再戳，有冷却，手指靠近时那一行先亮一点）。
    /// 点一首 = 八音盒换到这首（铭牌翻牌、八音盒放开头）；八音盒 ◀ ▶ 换歌时面板也跟着跳到那一页。
    /// 编辑器：数字键 1–6 选当前页的曲子，Tab 换分类，M 切模式。</summary>
    public class TKSongPanel : MonoBehaviour
    {
        public TKMusicBox box;
        public HandTapInput hands;
        public Transform board;
        public Material glowMaterial;
        [Tooltip("刻字材质（不发光的深色字，写在瓷面上）")] public Material letterMaterial;
        [Tooltip("选中 / 悬停的光（Twilight/BoxGlow）")] public Material highlightMaterial;

        const int Rows = 6;
        const float W = 0.23f, H = 0.315f;   // 面板加高
        // 第十四轮：面板改成象牙瓷卡片（和八音盒一套），字一律深褐墨色，不再发光
        static readonly Color Cream = new(0.36f, 0.25f, 0.15f), Gold = new(0.17f, 0.09f, 0.03f), Dim = new(0.36f, 0.26f, 0.17f), Ink = new(0.2f, 0.11f, 0.05f);

        class Target { public Rect r; public bool armed = true; public float cool, hover; public bool free; }   // free：不参与面板平面判定（拖拽把手在面板外面）
        class Line { public TextMesh zh, en; }

        readonly List<Target> targets = new();       // 0..3 分类，4.. 曲目行，然后上一页、下一页、我来弹、大师演奏
        const int RowBase = 4, PrevIx = RowBase + Rows, NextIx = PrevIx + 1, PlayIx = NextIx + 1, MasterIx = PlayIx + 1, BackIx = MasterIx + 1, DragIx = BackIx + 1;
        Line title, back, pageText, modeA, modeB;
        readonly Line[] tabs = new Line[4], rows = new Line[Rows];
        readonly TextMesh[] stars = new TextMesh[Rows];
        Transform selGlow, hoverGlow, tabGlow, modeGlow, dragHandle, dragIcon, backHandle;
        Material selMat, hoverMat, tabMat, modeMat;
        int cat, page;
        string lastSong;
        float show, openedAt = -9f;
        readonly List<string> list = new();
        int langVer = -1;
        // 拖拽
        int dragBy = -1; Vector3 dragStart, dragPanelStart;
        Vector3 dragOffset;
        readonly bool[] pinching = new bool[2];
        float dragHover;

        void Awake()
        {
            var brushGlow = TKFont.Mat(letterMaterial ? letterMaterial : glowMaterial, 0, TKFont.Brush);
            var roundGlow = TKFont.Mat(letterMaterial ? letterMaterial : glowMaterial, 0, TKFont.Round);
            Line L(string n, float size, Vector3 at, TextAnchor a = TextAnchor.MiddleCenter) => new Line
            {
                zh = Txt(n + "Zh", brushGlow, size, TKFont.Brush, at, a),
                en = Txt(n + "En", roundGlow, size * 0.72f, TKFont.Round, at, a),
            };
            // 曲目卡片：横版 0.268 × 0.235（本地单位；盒子放大 1.3 倍后约 35 × 30 cm），盖在八音盒正面上、正对着人
            // 最上一行：左边“返回”、中间“选曲”；下面分类、5 首曲目，最底下“我来弹 / 翻页 / 大师演奏”
            title = L("Title", 0.0022f, new Vector3(0, 0.105f, 0));
            back = L("Back", 0.0018f, new Vector3(-0.1f, 0.105f, 0));
            for (int i = 0; i < 4; i++) { float x = -0.0915f + i * 0.061f; tabs[i] = L("Tab" + i, 0.0019f, new Vector3(x, 0.074f, 0)); targets.Add(new Target { r = R(x, 0.074f, 0.057f, 0.024f) }); }   // 四颗钮都收在瓷面里
            for (int i = 0; i < Rows; i++)
            {
                float y = 0.045f - i * 0.022f;   // 6 行：0.045 … -0.065（分类线 0.059 和底线 -0.081 之间）                 // 5 行：0.032 … -0.056
                rows[i] = L("Row" + i, 0.0018f, new Vector3(-0.112f, y, 0), TextAnchor.MiddleLeft);
                stars[i] = Txt("Stars" + i, roundGlow, 0.0012f, TKFont.Round, new Vector3(0.112f, y, 0), TextAnchor.MiddleRight);
                targets.Add(new Target { r = R(0, y, 0.24f, 0.021f) });
            }
            pageText = L("Page", 0.0016f, new Vector3(0, -0.101f, 0));
            targets.Add(new Target { r = R(-0.03f, -0.101f, 0.03f, 0.024f) });
            targets.Add(new Target { r = R(0.03f, -0.101f, 0.03f, 0.024f) });
            modeA = L("ModeA", 0.0018f, new Vector3(-0.079f, -0.101f, 0));
            modeB = L("ModeB", 0.0018f, new Vector3(0.079f, -0.101f, 0));
            targets.Add(new Target { r = R(-0.079f, -0.101f, 0.07f, 0.024f) });
            targets.Add(new Target { r = R(0.079f, -0.101f, 0.07f, 0.024f) });
            targets.Add(new Target { r = R(-0.106f, 0.105f, 0.036f, 0.032f) });              // 返回（第三十一轮：圆形图标钮）
            targets.Add(new Target { r = R(0.146f, 0.136f, 0.034f, 0.034f), free = true });    // 拖拽把手（在面板右上外角，悬在板外）
            // 拖拽把手（右上角：四向箭头图标，捏住可以拖动整个面板）
            dragHandle = new GameObject("DragHandle").transform;
            dragHandle.SetParent(transform, false);
            dragHandle.localPosition = new Vector3(0.142f, 0.132f, -0.001f);   // 面板右上角外侧（板边 x 0.134、y 0.126）
            BuildDragIcon();
            // 第三十一轮（用户：左上角返回改成明显的图标样式）：和拖拽钮同一套——金边圆钮 + 象牙底 + 粗金色左箭头
            backHandle = new GameObject("BackHandle").transform;
            backHandle.SetParent(transform, false);
            backHandle.localPosition = new Vector3(-0.106f, 0.105f, -0.001f);
            BuildBackIcon();
            selGlow = Glow("SelGlow", out selMat, new Color(1f, 0.72f, 0.38f), 0.5f);
            hoverGlow = Glow("HoverGlow", out hoverMat, new Color(1f, 0.85f, 0.6f), 0.3f);
            tabGlow = Glow("TabGlow", out tabMat, new Color(1f, 0.72f, 0.38f), 0.45f);
            modeGlow = Glow("ModeGlow", out modeMat, new Color(1f, 0.72f, 0.38f), 0.45f);
            gameObject.SetActive(true);
        }

        static Rect R(float cx, float cy, float w, float h) => new(cx - w / 2, cy - h / 2, w, h);

        /// <summary>拖拽把手：面板右上外角一枚圆形深木底托 + 金色四向箭头（十字 + 四个箭头尖），一看就知道能抓住拖动。
        /// 材质直接取面板木板上的金框 / 深木面（不在运行时读资源库）。</summary>
        void BuildDragIcon()
        {
            Material gold = null, wood = null;
            if (board)
                foreach (var r in board.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name == "Frame") gold = r.sharedMaterial;
                    if (r.name == "Face") wood = r.sharedMaterial;
                }
            // 底托：扁圆柱，边上一圈金
            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(rim.GetComponent<Collider>());
            rim.name = "DragRim"; rim.transform.SetParent(dragHandle, false);
            rim.transform.localRotation = Quaternion.Euler(90, 0, 0);
            rim.transform.localPosition = new Vector3(0, 0, 0.003f);
            rim.transform.localScale = new Vector3(IconRim, 0.003f, IconRim);
            if (gold) rim.GetComponent<MeshRenderer>().sharedMaterial = gold;
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(pad.GetComponent<Collider>());
            pad.name = "DragPad"; pad.transform.SetParent(dragHandle, false);
            pad.transform.localRotation = Quaternion.Euler(90, 0, 0);
            pad.transform.localPosition = new Vector3(0, 0, 0.0012f);
            pad.transform.localScale = new Vector3(IconPad, 0.0015f, IconPad);
            if (wood) pad.GetComponent<MeshRenderer>().sharedMaterial = wood;
            // 金色四向箭头：两根细杆十字 + 四个三角箭头（单面网格，面朝 −Z = 朝玩家）
            var v = new List<Vector3>(); var t = new List<int>();
            void Quad(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 a3) { int k = v.Count; v.Add(a0); v.Add(a1); v.Add(a2); v.Add(a3); t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 }); }
            void Tri(Vector2 a0, Vector2 a1, Vector2 a2) { int k = v.Count; v.Add(a0); v.Add(a1); v.Add(a2); t.AddRange(new[] { k, k + 2, k + 1 }); }
            const float L = 0.0085f, w = 0.0012f, hw = 0.0032f, hl = 0.0038f;   // 第三十一轮：图标缩到约 2/3（原来 L 0.0125）
            Quad(new(-L + hl, -w), new(L - hl, -w), new(L - hl, w), new(-L + hl, w));
            Quad(new(-w, -L + hl), new(w, -L + hl), new(w, L - hl), new(-w, L - hl));
            Tri(new(L, 0), new(L - hl, hw), new(L - hl, -hw));
            Tri(new(-L, 0), new(-L + hl, -hw), new(-L + hl, hw));
            Tri(new(0, L), new(-hw, L - hl), new(hw, L - hl));
            Tri(new(0, -L), new(hw, -L + hl), new(-hw, -L + hl));
            var mesh = new Mesh { name = "DragArrows" };
            var vs = new List<Vector3>(); foreach (var p in v) vs.Add(new Vector3(p.x, p.y, -0.0006f));
            mesh.SetVertices(vs); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var icon = new GameObject("DragIcon");
            icon.transform.SetParent(dragHandle, false);
            icon.AddComponent<MeshFilter>().sharedMesh = mesh;
            var ir = icon.AddComponent<MeshRenderer>(); ir.sharedMaterial = gold; ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dragIcon = icon.transform;
        }

        // 第三十一轮：右上角拖拽钮缩到约 2/3（直径 4 cm → 2.7 cm，本地单位）；返回钮同样大小
        const float IconRim = 0.027f, IconPad = 0.0235f;

        /// <summary>返回钮：和拖拽钮同一套（金边扁圆 + 象牙底），中间一个粗金色左箭头（箭杆 + 大三角箭头），一看就知道是“返回”。</summary>
        void BuildBackIcon()
        {
            Material gold = null, wood = null;
            if (board)
                foreach (var r in board.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name == "Frame") gold = r.sharedMaterial;
                    if (r.name == "Face") wood = r.sharedMaterial;
                }
            foreach (var (n, z, d, h, m) in new[] { ("BackRim", 0.003f, IconRim, 0.003f, gold), ("BackPad", 0.0012f, IconPad, 0.0015f, wood) })
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(c.GetComponent<Collider>());
                c.name = n; c.transform.SetParent(backHandle, false);
                c.transform.localRotation = Quaternion.Euler(90, 0, 0);
                c.transform.localPosition = new Vector3(0, 0, z);
                c.transform.localScale = new Vector3(d, h, d);
                if (m) c.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            // 粗左箭头（单面网格，面朝 −Z = 朝玩家）：箭杆 + 箭头三角，整体略往右挪一点让视觉居中
            var v = new List<Vector3>(); var t = new List<int>();
            void Quad(Vector2 a0, Vector2 a1, Vector2 a2, Vector2 a3) { int k = v.Count; foreach (var p in new[] { a0, a1, a2, a3 }) v.Add(new Vector3(p.x, p.y, -0.0006f)); t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 }); }
            void Tri(Vector2 a0, Vector2 a1, Vector2 a2) { int k = v.Count; foreach (var p in new[] { a0, a1, a2 }) v.Add(new Vector3(p.x, p.y, -0.0006f)); t.AddRange(new[] { k, k + 2, k + 1 }); }
            const float tipX = -0.0068f, headL = 0.0058f, headW = 0.0056f, tailX = 0.0068f, shaft = 0.0017f;
            Tri(new(tipX, 0), new(tipX + headL, -headW), new(tipX + headL, headW));   // 逆时针（和拖拽钮的三角同向），不然被背面剔除
            Quad(new(tipX + headL - 0.0004f, -shaft), new(tailX, -shaft), new(tailX, shaft), new(tipX + headL - 0.0004f, shaft));
            var mesh = new Mesh { name = "BackArrow" };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var icon = new GameObject("BackIcon");
            icon.transform.SetParent(backHandle, false);
            icon.AddComponent<MeshFilter>().sharedMesh = mesh;
            var ir = icon.AddComponent<MeshRenderer>(); ir.sharedMaterial = gold; ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        TextMesh Txt(string n, Material m, float size, Font f, Vector3 at, TextAnchor a)
        {
            var t = TKFont.Text(n, transform, m, size, f, a);
            t.transform.localPosition = at + new Vector3(0, 0, -0.0015f);
            return t;
        }

        Transform Glow(string n, out Material m, Color c, float strength)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = n; q.transform.SetParent(transform, false);
            m = new Material(highlightMaterial);
            m.SetColor("_Color", c); m.SetFloat("_Strength", strength); m.SetFloat("_Falloff", 1.4f); m.SetFloat("_Ring", 0f);
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.transform.localPosition = new Vector3(0, 0, -0.0008f);
            q.transform.localRotation = Quaternion.Euler(0, 180, 0);       // Quad 正面朝 -Z（面向玩家）
            return q.transform;
        }

        void Rebuild()
        {
            list.Clear();
            string c = SongLibrary.Categories[cat];
            foreach (var id in SongLibrary.Ordered())
            {
                var s = SongLibrary.Find(id);
                string sc = string.IsNullOrEmpty(s.category) ? "kids" : s.category;
                if (sc == c && box && box.playlist != null && System.Array.IndexOf(box.playlist, id) >= 0) list.Add(id);
            }
            int pages = Mathf.Max(1, (list.Count + Rows - 1) / Rows);
            page = Mathf.Clamp(page, 0, pages - 1);
        }

        void Follow(string id)
        {
            var s = SongLibrary.Find(id);
            if (s == null) return;
            int c = System.Array.IndexOf(SongLibrary.Categories, string.IsNullOrEmpty(s.category) ? "kids" : s.category);
            cat = c < 0 ? 3 : c;
            Rebuild();
            int i = list.IndexOf(id);
            if (i >= 0) page = i / Rows;
        }

        [Header("抽屉（盒子 Body 空间）")]
        [Tooltip("收起时（缩在盖子上方）")] public Vector3 closedPos = new(0f, 0.13f, -0.06f);
        [Tooltip("打开时：正中、比视线中心略高")] public Vector3 openPos = new(0f, 0.215f, -0.1f);   // 第十四轮（用户）：不放左边，放正中、略高于中心
        [Tooltip("打开后倾斜角度（负 = 面朝下，让坐着的人从上往下看）")] public float openTilt = -8f;
        [Tooltip("打开后转向玩家（度）")] public float openYaw = 26f;
        [Tooltip("打开后的大小")] public float openScale = 0.8f;

        void Update()
        {
            if (!box) return;
            // 不会一上来就出现：点铭牌（写着歌名的那块）才拉出来；选了歌 / 按开始 / 进设置自动收回
            bool want = box.Visible && box.IsOpen && box.PanelOpen && !box.InSettings && !box.Closing;
            if (want && show < 0.5f) openedAt = Time.time;              // 刚打开：记录时刻
            show = Mathf.MoveTowards(show, want ? 1f : 0f, Time.deltaTime * (want ? 2.4f : 3.5f));
            float e = show * show * (3 - 2 * show);
            // 从盖子上方展开：面板从收起位置放大、浮到盒盖上方
            var root = transform.parent;
            var target = openPos + dragOffset;
            root.localPosition = Vector3.Lerp(closedPos, target, e);
            // 打开后整块面板正对玩家的眼睛（不再是固定角度斜着）；面板正面朝 −Z，所以 forward = 眼睛 → 面板
            var faceRot = root.parent.rotation * Quaternion.Euler(openTilt, openYaw, 0f);
            if (box.head) { var d = root.position - box.head.position; if (d.sqrMagnitude > 1e-4f) faceRot = Quaternion.LookRotation(d, Vector3.up); }
            root.rotation = Quaternion.Slerp(root.parent.rotation, faceRot, e);
            root.localScale = Vector3.one * Mathf.Lerp(0.18f, openScale, e);
            transform.localScale = Vector3.one;
            // 拖拽把手：捏住右上角的 + 号拖动面板
            if (show > 0.9f) UpdateDrag();
            bool vis = show > 0.01f;
            if (board && board.gameObject.activeSelf != vis) board.gameObject.SetActive(vis);
            foreach (Transform c in transform) if (c.gameObject.activeSelf != vis) c.gameObject.SetActive(vis);
            if (!vis) return;
            TKFont.Sync();
            // 面板完全展开后 0.6 秒，上面的按钮才开始接受戳（从铭牌收回手指需要时间，不然铭牌→卡片"穿透"误触）
            bool ready = show > 0.95f && Time.time - openedAt > 0.6f;

            if (box.SongId != lastSong) { lastSong = box.SongId; Follow(lastSong); }
            if (langVer != TKLang.Version) { langVer = TKLang.Version; }
            if (list.Count == 0) Rebuild();

            if (ready) Pokes(Time.deltaTime);
            Keys();
            Draw(e);
        }

        void Draw(float e)
        {
            Set(title, "选曲", "Songs"); Alpha(title, Gold, e);
            Set(back, "", ""); Alpha(back, Cream, 0f);   // 第三十一轮：返回改成图标钮（BackHandle），不再写字
            if (backHandle) { float bh = targets[BackIx].hover; backHandle.localScale = Vector3.one * (1f + 0.12f * bh); backHandle.localPosition = new Vector3(-0.106f, 0.105f, -0.001f - 0.004f * bh); }
            // 拖拽把手：手靠近时变亮
            if (dragHandle) dragHandle.localScale = Vector3.one * (1f + 0.15f * dragHover + (dragBy >= 0 ? 0.1f : 0f));
            for (int i = 0; i < 4; i++)
            {
                var b = SongLibrary.CategoryName(SongLibrary.Categories[i]);
                Set(tabs[i], b.zh, b.en); Alpha(tabs[i], i == cat ? Gold : Dim, e);
            }
            int sel = -1;
            for (int i = 0; i < Rows; i++)
            {
                int k = page * Rows + i;
                if (k < list.Count)
                {
                    var s = SongLibrary.Find(list[k]);
                    Set(rows[i], s.title, SongLibrary.EnglishTitle(s.id));
                    int dl = Mathf.Clamp(s.difficulty, 1, 3);
                    stars[i].text = TKLang.En ? (dl == 1 ? "Easy" : dl == 2 ? "Medium" : "Hard") : (dl == 1 ? "简单" : dl == 2 ? "中等" : "较难");
                    bool on = s.id == box.SongId;
                    if (on) sel = i;
                    Alpha(rows[i], on ? Gold : Ink, e);          // 选中：深铜底上的亮金字
                    TKFont.Alpha(stars[i], on ? Gold : Dim, e * 0.8f);
                }
                else { Set(rows[i], "", ""); stars[i].text = ""; }
            }
            int pages = Mathf.Max(1, (list.Count + Rows - 1) / Rows);
            Set(pageText, pages > 1 ? $"‹   {page + 1} / {pages}   ›" : "", pages > 1 ? $"‹   {page + 1} / {pages}   ›" : "");
            Alpha(pageText, Dim, e * 0.85f);
            Set(modeA, "我来弹", "I play"); Set(modeB, "大师演奏", "Master plays");
            Alpha(modeA, !box.MasterMode ? Gold : Dim, e);
            Alpha(modeB, box.MasterMode ? Gold : Dim, e);

            Place(selGlow, default, false, 1.04f);
            Pills(sel, e);
            if (selMat) { selMat.SetFloat("_Ring", 1f); selMat.SetFloat("_Falloff", 1.9f); }   // 选中：一圈金线描边（不是一整块亮）
            if (tabMat) tabMat.SetFloat("_Ring", 1f); if (modeMat) modeMat.SetFloat("_Ring", 1f);   // 分类 / 模式同样用描边，风格统一
            Place(tabGlow, default, false, 1.15f);
            Place(modeGlow, default, false, 1.15f);
            int hv = -1; float best = 0.05f;
            for (int i = 0; i < targets.Count; i++) if (targets[i].hover > best) { best = targets[i].hover; hv = i; }
            Place(hoverGlow, hv >= 0 ? targets[hv].r : default, hv >= 0, 1.1f);
            hoverMat.SetFloat("_Strength", 0.12f * best);   // 瓷面上悬停只微微亮一点
            selMat.SetFloat("_Strength", 0.5f * e); tabMat.SetFloat("_Strength", 0.42f * e); modeMat.SetFloat("_Strength", 0.42f * e);
        }

    // ---- 第十四轮：瓷钮（用户：选中“古典”只在下面画一道横线太低级）----
    // 分类、模式、选中的曲目都是真正的小瓷钮：象牙瓷面 + 一道细金线（和八音盒上的按钮同一套）；
    // 选中的那个换成香槟金釉，并且往下按进去 0.7 mm，像被按住的按钮。没选中的分类 / 模式也有钮，一眼看出“这些都能按”。
    readonly Transform[] tabPill = new Transform[4];
    Transform playPill, masterPill, rowPill;
    Material pillOff, pillOn, pillRim;

    void PillMats()
    {
        if (pillOff) return;
        Material ivory = null, gold = null;
        if (board) foreach (var mr in board.GetComponentsInChildren<MeshRenderer>(true)) { if (mr.name == "Face") ivory = mr.sharedMaterial; if (mr.name == "Frame") gold = mr.sharedMaterial; }
        pillOff = ivory ? new Material(ivory) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (pillOff.HasProperty("_Color")) pillOff.SetColor("_Color", new Color(1.03f, 1.0f, 0.95f));
            if (pillOff.HasProperty("_Gloss")) pillOff.SetFloat("_Gloss", 0.12f);   // 没选中的钮几乎不反光（不然某一颗会被高光整片打白）
        pillOn = new Material(pillOff);
        if (pillOn.HasProperty("_Color")) pillOn.SetColor("_Color", new Color(1.0f, 0.86f, 0.6f));   // 香槟金釉
        if (pillOn.HasProperty("_Gloss")) pillOn.SetFloat("_Gloss", 0.7f);
        pillRim = gold ? gold : pillOn;
    }

    /// <summary>一颗圆头长条瓷钮：w × h（两头半圆），厚 2 mm；外圈一道 1.1 mm 宽的金线。正面朝 −Z。</summary>
    Transform Pill(string n, float w, float h)
    {
        PillMats();
        var root = new GameObject(n).transform;
        root.SetParent(transform, false);
        var body = new GameObject("Body");
        body.transform.SetParent(root, false);
        body.AddComponent<MeshFilter>().sharedMesh = Stadium(w, h, 0.0021f, 0f);
        var br = body.AddComponent<MeshRenderer>(); br.sharedMaterial = pillOff; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var rim = new GameObject("Rim");
        rim.transform.SetParent(root, false);
        rim.AddComponent<MeshFilter>().sharedMesh = Stadium(w + 0.0024f, h + 0.0024f, 0.0016f, 0.0011f);
        var rr = rim.AddComponent<MeshRenderer>(); rr.sharedMaterial = pillRim; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return root;
    }

    /// <summary>圆头长条：hole = 0 时是实心（正面 + 侧壁），hole &gt; 0 时只做一圈宽 hole 的环（金线）。正面在 z = −depth。</summary>
    static Mesh Stadium(float w, float h, float depth, float hole)
    {
        var outer = new List<Vector2>(); var inner = new List<Vector2>();
        float r = h / 2; const int seg = 14;
        for (int side = 0; side < 2; side++)
            for (int k = 0; k <= seg; k++)
            {
                float a = (side == 0 ? -90f : 90f) + 180f * k / seg;
                var c = new Vector2(side == 0 ? w / 2 - r : -w / 2 + r, 0);
                var d = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                outer.Add(c + d * r);
                inner.Add(c + d * Mathf.Max(0.0005f, r - hole));
            }
        int n = outer.Count;
        var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
        if (hole <= 0f)
        {
            v.Add(new Vector3(0, 0, -depth)); uv.Add(new Vector2(0.5f, 0.5f));
            foreach (var p in outer) { v.Add(new Vector3(p.x, p.y, -depth)); uv.Add(new Vector2(p.x / w + 0.5f, p.y / h + 0.5f)); }
            for (int i = 0; i < n; i++) t.AddRange(new[] { 0, 1 + (i + 1) % n, 1 + i });
        }
        else
        {
            for (int i = 0; i < n; i++) { v.Add(new Vector3(outer[i].x, outer[i].y, -depth)); v.Add(new Vector3(inner[i].x, inner[i].y, -depth)); uv.Add(Vector2.zero); uv.Add(Vector2.one); }
            for (int i = 0; i < n; i++) { int a = i * 2, b = ((i + 1) % n) * 2; t.AddRange(new[] { a, b + 1, a + 1, a, b, b + 1 }); }
        }
        int s0 = v.Count;
        for (int i = 0; i < n; i++) { v.Add(new Vector3(outer[i].x, outer[i].y, -depth)); v.Add(new Vector3(outer[i].x, outer[i].y, 0)); uv.Add(Vector2.zero); uv.Add(Vector2.one); }
        for (int i = 0; i < n; i++) { int a = s0 + i * 2, b = s0 + ((i + 1) % n) * 2; t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 }); }
        var m = new Mesh { name = "Pill" };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    void PillAt(Transform p, Rect r, bool on, bool vis)
    {
        p.gameObject.SetActive(vis);
        if (!vis) return;
        p.localPosition = new Vector3(r.center.x, r.center.y, on ? 0.0016f : 0.0009f);   // 钮面在字后面；选中的再按进去 0.7 mm
        p.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial = on ? pillOn : pillOff;
    }

    void Pills(int sel, float e)
    {
        bool vis = e > 0.05f;
        for (int i = 0; i < 4; i++)
        {
            var r = targets[i].r;
            if (!tabPill[i]) tabPill[i] = Pill("TabPill" + i, r.width * 0.92f, r.height * 0.95f);
            PillAt(tabPill[i], r, i == cat, vis);
        }
        var ra = targets[PlayIx].r; var rb = targets[MasterIx].r;
        if (!playPill) playPill = Pill("PlayPill", ra.width * 0.95f, ra.height * 0.95f);
        if (!masterPill) masterPill = Pill("MasterPill", rb.width * 0.95f, rb.height * 0.95f);
        PillAt(playPill, ra, !box.MasterMode, vis);
        PillAt(masterPill, rb, box.MasterMode, vis);
        var rs = targets[RowBase].r;
        if (!rowPill) rowPill = Pill("RowPill", rs.width * 1.02f, rs.height * 1.0f);
        if (sel >= 0) PillAt(rowPill, targets[RowBase + sel].r, true, vis); else rowPill.gameObject.SetActive(false);
    }
        Transform selPlate, tabLine, modeLine;
        Material plateMat;

        void Underline(ref Transform t, Rect r, float frac, float e)
        {
            if (!t)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(c.GetComponent<Collider>());
                t = c.transform; t.name = "Underline"; t.SetParent(transform, false);
                var mr = c.GetComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (board) foreach (var m in board.GetComponentsInChildren<MeshRenderer>(true)) if (m.name == "Frame") mr.sharedMaterial = m.sharedMaterial;
            }
            t.gameObject.SetActive(e > 0.05f);
            t.localPosition = new Vector3(r.center.x, r.yMin + 0.0025f, -0.0008f);
            t.localScale = new Vector3(r.width * frac, 0.0016f, 0.0012f);
        }
        /// <summary>选中行的金牌：一块圆角感的扁金片（用面板金框的材质）+ 两头各一颗小金珠，衬在字后面。</summary>
        void PlacePlate(Rect r, bool on, float e)
        {
            if (!selPlate)
            {
                Material gold = null;
                if (board) foreach (var mr in board.GetComponentsInChildren<MeshRenderer>(true)) if (mr.name == "Frame") gold = mr.sharedMaterial;
                selPlate = new GameObject("SelPlate").transform; selPlate.SetParent(transform, false);
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(bar.GetComponent<Collider>());
                bar.name = "Bar"; bar.transform.SetParent(selPlate, false); bar.transform.localScale = new Vector3(1f, 1f, 0.0015f);
                foreach (var sx in new[] { -0.5f, 0.5f })
                {
                    var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(cap.GetComponent<Collider>());
                    cap.name = "Cap"; cap.transform.SetParent(selPlate, false);
                    cap.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    cap.transform.localPosition = new Vector3(sx, 0, 0); cap.transform.localScale = new Vector3(0.001f, 0.0008f, 1f);
                }
                Material face = null;
                if (board) foreach (var mr in board.GetComponentsInChildren<MeshRenderer>(true)) if (mr.name == "Face") face = mr.sharedMaterial;
                // 选中：比丝绒亮一档的紫蓝牌子（同一种哑光），字是亮香槟金
                if (face) { plateMat = new Material(face); plateMat.SetColor("_Color", new Color(0.42f, 0.38f, 0.78f)); }
                foreach (var mr in selPlate.GetComponentsInChildren<MeshRenderer>()) { if (plateMat) mr.sharedMaterial = plateMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            }
            selPlate.gameObject.SetActive(on && e > 0.05f);
            if (!on) return;
            float w = r.width * 0.98f, h = r.height * 0.92f;
            selPlate.localPosition = new Vector3(r.center.x, r.center.y, 0.0008f);
            selPlate.GetChild(0).localScale = new Vector3(w - h, h, 0.0015f);
            for (int k = 1; k < selPlate.childCount; k++)
            {
                var c = selPlate.GetChild(k);
                c.localPosition = new Vector3((k == 1 ? -1 : 1) * (w - h) / 2, 0, 0);
                c.localScale = new Vector3(h, 0.00075f, h);
            }
        }

        static void Place(Transform g, Rect r, bool on, float pad)
        {
            g.gameObject.SetActive(on);
            if (!on) return;
            g.localPosition = new Vector3(r.center.x, r.center.y, -0.0008f);
            g.localScale = new Vector3(r.width * pad, r.height * pad * 1.6f, 1f);
        }

        static void Set(Line l, string zh, string en) { if (l.zh.text != zh) l.zh.text = zh; if (l.en.text != en) l.en.text = en; }
        static void Alpha(Line l, Color c, float a) { TKFont.Alpha(TKLang.En ? l.en : l.zh, c, a); TKFont.Alpha(TKLang.En ? l.zh : l.en, c, 0f); }

        /// <summary>指尖戳：指尖到面板前 8 mm 以内、落在格子里（外扩 6 mm / 4 mm）；戳过要先退开（离面板 2.5 cm 或移出格子）才能再戳，0.35 秒冷却。</summary>
        void Pokes(float dt)
        {
            var tips = new List<Vector3>(2);
            if (hands) { if (hands.Left != null) tips.Add(hands.Left.indexTip); if (hands.Right != null) tips.Add(hands.Right.indexTip); }
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t.free) continue;
                t.cool -= dt;
                bool inside = false, far = true, near = false;
                foreach (var w in tips)
                {
                    var p = transform.InverseTransformPoint(w);
                    float along = -p.z;
                    bool over = p.x > t.r.xMin - 0.006f && p.x < t.r.xMax + 0.006f && p.y > t.r.yMin - 0.004f && p.y < t.r.yMax + 0.004f;
                    if (over && along < 0.008f && along > -0.03f) inside = true;
                    if (over && along < 0.05f && along > -0.03f) near = true;
                    if (over && along < 0.025f) far = false;
                }
                t.hover = Mathf.MoveTowards(t.hover, near ? 1f : 0f, dt * 6f);
                if (far) t.armed = true;
                if (inside && t.armed && t.cool <= 0f) { t.armed = false; t.cool = 0.35f; Press(i); }
            }
        }

        /// <summary>按下一个格子：0..3 分类，RowBase.. 曲目行，PrevIx / NextIx 翻页，PlayIx 我来弹，MasterIx 大师演奏。</summary>
        public void Press(int i)
        {
            if (i < RowBase) { cat = i; page = 0; Rebuild(); }
            else if (i < PrevIx) { int k = page * Rows + i - RowBase; if (k < list.Count) box.SelectSong(list[k]); }   // 选好歌：铭牌翻牌、但面板不收（玩家可以继续翻、换分类）
            else if (i == PrevIx) { page = Mathf.Max(0, page - 1); }
            else if (i == NextIx) { page = Mathf.Min(Mathf.Max(0, list.Count - 1) / Rows, page + 1); }
            else if (i == PlayIx) box.MasterMode = false;
            else if (i == MasterIx) box.MasterMode = true;
            else if (i == BackIx) box.PanelOpen = false;                // 返回：收起选曲卡，回到八音盒
            box.Tick(i >= RowBase && i < PrevIx);
        }

        /// <summary>拖拽把手：捏住（拇指碰食指）右上角的把手，面板跟着手移动；松手后记住偏移。</summary>
        void UpdateDrag()
        {
            if (!hands || !dragHandle) return;
            var handleW = dragHandle.position;
            float handleR = 0.05f * (transform.parent ? transform.parent.lossyScale.x : 1f);   // 半径跟随整体缩放
            float nearest = 9f;
            if (hands.Left != null) nearest = Mathf.Min(nearest, Vector3.Distance((hands.Left.thumbTip + hands.Left.indexTip) * 0.5f, handleW));
            if (hands.Right != null) nearest = Mathf.Min(nearest, Vector3.Distance((hands.Right.thumbTip + hands.Right.indexTip) * 0.5f, handleW));
            dragHover = Mathf.MoveTowards(dragHover, nearest < handleR * 1.6f ? 1f : 0f, Time.deltaTime * 6f);
            for (int h = 0; h < 2; h++)
            {
                var j = h == 0 ? hands.Left : hands.Right;
                if (j == null) { if (dragBy == h) { dragBy = -1; } pinching[h] = false; continue; }
                float d = Vector3.Distance(j.thumbTip, j.indexTip);
                bool was = pinching[h];
                pinching[h] = pinching[h] ? d < 0.04f : d < 0.022f;
                var pp = (j.thumbTip + j.indexTip) * 0.5f;
                if (dragBy == h)
                {
                    if (!pinching[h]) { dragBy = -1; continue; }
                    var root = transform.parent;
                    var body = root.parent;
                    var delta = pp - dragStart;
                    var localDelta = body.InverseTransformVector(delta);
                    dragOffset = dragPanelStart + new Vector3(localDelta.x, localDelta.y, localDelta.z) - openPos;
                }
                else if (dragBy < 0 && pinching[h] && !was && Vector3.Distance(pp, handleW) < handleR)
                {
                    dragBy = h; dragStart = pp;
                    dragPanelStart = openPos + dragOffset;
                }
            }
        }

        void Keys()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            for (int i = 0; i < Rows; i++) if (kb[UnityEngine.InputSystem.Key.Digit1 + i].wasPressedThisFrame) Press(RowBase + i);
            if (kb.tabKey.wasPressedThisFrame) Press((cat + 1) % 4);
            if (kb.mKey.wasPressedThisFrame) Press(box.MasterMode ? PlayIx : MasterIx);
            if (kb.pageDownKey.wasPressedThisFrame) Press(NextIx);
            if (kb.pageUpKey.wasPressedThisFrame) Press(PrevIx);
        }
    }
}
