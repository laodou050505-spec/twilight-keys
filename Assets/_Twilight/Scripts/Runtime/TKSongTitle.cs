using System.Collections.Generic;
using UnityEngine;
using Twilight.Logic;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace Twilight
{
    /// <summary>窗框顶上的歌名牌 + 窗边歌单（第二十五 / 二十九轮）。
    /// 歌名牌：横梁正面的象牙瓷牌（细金边）写当前这首；一首歌开始时展开，换歌 / 换语言先收再展。
    /// 歌单：看着歌名牌捏一下（拇指尖碰食指尖），一张张歌名卡从歌名牌里飞出来，排在窗户右边一列，之后一直留着；
    /// 看着哪张卡捏一下就换成这首（马上从头弹）；最上 / 最下两张小卡翻页；再看着歌名牌捏一下收起歌单。
    /// 新手教学没做完时捏歌名牌只提示“先跟着教学弹完这首”。视线：PICO 眼动（拿不到时用头的朝向）。</summary>
    public class TKSongTitle : MonoBehaviour
    {
        public GameDirector director;
        public TKSession session;
        public HandTapInput hands;
        public Transform head;
        public Material plateMaterial, rimMaterial, letterMaterial;
        [Tooltip("消散版材质模板（Twilight/MusicBoxFade；场景里要有引用，打包才带着色器）")] public Material fadeTemplate;
        [Tooltip("窗户散到这个程度，歌名牌开始散")] [Range(0, 1)] public float fadeAfter = 0.85f;
        public float fadeSeconds = 1.6f;
        [Tooltip("歌名牌中心（窗户本地坐标：横梁正面中间）")] public Vector3 at = new(0f, 0.518f, -0.112f);
        [Tooltip("歌单第一张卡（窗户本地坐标：右扇百叶窗外侧）")] public Vector3 shelfAt = new(1.7f, 0.5f, -0.12f);
        public float cardW = 0.56f, cardH = 0.112f, cardGap = 0.132f;
        const int PerPage = 8;

        Transform root, plate, rim;
        TextMesh text, sub;
        Material ink;
        string shownText = "", shownSub = "";
        float open;
        int langVer = -1;

        // 歌单
        class Card { public Transform t, plate, rim; public TextMesh text; public string id; public int kind; public float show, hover, delay; public Vector3 slot; }   // kind: 0 歌、1 上一页、2 下一页
        readonly List<Card> cards = new();
        bool shelfOpen;
        int page = -1;
        float nudgeUntil = -1f;
        Mesh cardMesh, cardRimMesh, pageMesh, pageRimMesh;
        Material goldPlate, plateFade, rimFade;
        float titleFade;                         // 第三十轮：窗框散完后歌名牌也散掉（0 完整 … 1 散完）

        static bool ShelfKnown { get => PlayerPrefs.GetInt("tk_shelf_known", 0) == 1; set { PlayerPrefs.SetInt("tk_shelf_known", value ? 1 : 0); PlayerPrefs.Save(); } }

        void Start()
        {
            ink = TKFont.Mat(letterMaterial, 0, TKFont.Kai);
            if (ink.HasProperty("_HaloColor")) ink.SetColor("_HaloColor", new Color(0, 0, 0, 0));
            root = new GameObject("SongTitle").transform;
            root.SetParent(transform, false);
            root.localPosition = at;
            text = TKFont.Text("Name", root, ink, 0.0098f, TKFont.Kai);
            text.transform.localPosition = new Vector3(0, 0, -0.004f);
            sub = TKFont.Text("Sub", root, ink, 0.0052f, TKFont.Kai);
            sub.transform.localPosition = new Vector3(0, -0.062f, -0.004f);
            plate = Part(root, "Plate", plateMaterial, 0.0035f);
            rim = Part(root, "Rim", rimMaterial ? rimMaterial : plateMaterial, 0.0055f);
            root.localScale = new Vector3(0.001f, 1f, 1f);
            cardMesh = ScoreHUD.Pill(cardW, cardH, 0.008f); cardRimMesh = ScoreHUD.Pill(cardW + 0.008f, cardH + 0.008f, 0.005f);
            pageMesh = ScoreHUD.Pill(cardW * 0.55f, cardH * 0.72f, 0.008f); pageRimMesh = ScoreHUD.Pill(cardW * 0.55f + 0.008f, cardH * 0.72f + 0.008f, 0.005f);
            goldPlate = rimMaterial ? new Material(rimMaterial) : plateMaterial;
        }

        /// <summary>歌名牌换成消散版材质（光照一样，不跳色）并推进 _Fade；没在散就换回原材质。</summary>
        void ApplyFade()
        {
            if (!plate || !rim) return;
            var pr = plate.GetComponent<MeshRenderer>(); var rr = rim.GetComponent<MeshRenderer>();
            var origRim = rimMaterial ? rimMaterial : plateMaterial;
            if (titleFade <= 0f || !fadeTemplate)
            {
                if (pr.sharedMaterial != plateMaterial) pr.sharedMaterial = plateMaterial;
                if (rr.sharedMaterial != origRim) rr.sharedMaterial = origRim;
                return;
            }
            if (!plateFade) { plateFade = new Material(fadeTemplate) { name = "SongTitle 瓷牌（消散）" }; plateFade.CopyPropertiesFromMaterial(plateMaterial); }
            if (!rimFade) { rimFade = new Material(fadeTemplate) { name = "SongTitle 金边（消散）" }; rimFade.CopyPropertiesFromMaterial(origRim); }
            var b = rr.bounds; b.Encapsulate(pr.bounds);
            foreach (var m in new[] { plateFade, rimFade }) { m.SetFloat("_Fade", titleFade); m.SetFloat("_MinY", b.min.y); m.SetFloat("_MaxY", b.max.y); }
            pr.sharedMaterial = plateFade; rr.sharedMaterial = rimFade;
        }

        static Transform Part(Transform parent, string n, Material m, float z)
        {
            var g = new GameObject(n);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = new Vector3(0, 0, z);
            g.AddComponent<MeshFilter>();
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return g.transform;
        }

        static string Name(string id)
        {
            var s = SongLibrary.Find(id);
            return TKLang.En ? SongLibrary.EnglishTitle(id) : (s != null ? s.title : id);
        }

        string SubLine()
        {
            if (Time.time < nudgeUntil) return TKLang.Pick("先跟着教学弹完这首", "finish the tutorial song first");
            if (!ShelfKnown) return TKLang.Pick("看着歌名捏一下 · 打开歌单", "look here and pinch · song list");
            return "";
        }

        void Update()
        {
            if (!root || !director) return;
            var g = director.Game;
            bool playing = g != null && g.State != GameState.Idle;
            string want = playing ? Name(director.songId ?? "") : "";
            string wantSub = playing ? SubLine() : "";
            bool changed = want != shownText || langVer != TKLang.Version;
            bool subChanged = wantSub != shownSub;
            bool show = playing && !string.IsNullOrEmpty(want) && !changed;
            // 第三十轮（用户：前面窗框消散后，前面的歌名也要散掉）：窗户散得差不多了，歌名牌从下往上化成雾散去；下一首窗户回来，它再弹出来
            float wf = director.reveal ? director.reveal.WindowFade : 0f;
            if (!playing || wf <= 0.0001f) { if (titleFade > 0f) { titleFade = 0f; open = 0f; } }
            else if (wf >= fadeAfter) titleFade = Mathf.MoveTowards(titleFade, 1f, Time.deltaTime / Mathf.Max(0.1f, fadeSeconds));
            open = Mathf.MoveTowards(open, show ? 1f : 0f, Time.deltaTime * (show ? 1.6f : 3f));
            if ((changed && open <= 0.001f) || (!changed && subChanged)) Relayout(want, wantSub);
            float e = open * open * (3f - 2f * open);
            root.gameObject.SetActive(open > 0.001f && titleFade < 0.999f);
            ApplyFade();
            root.localScale = new Vector3(Mathf.Max(0.001f, e), Mathf.Lerp(0.7f, 1f, e), 1f);
            var titleHover = Hover(root.position, root.rotation, plateHalf, 0.03f);
            var inkC = new Color(0.2f, 0.11f, 0.05f);
            float inkA = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.7f, titleFade));   // 字在牌子中间：牌子散到一半时字也淡没
            TKFont.Alpha(text, inkC, Mathf.Clamp01(e * 1.6f - 0.5f) * inkA);
            TKFont.Alpha(sub, new Color(0.38f, 0.26f, 0.16f), Mathf.Clamp01(e * 1.6f - 0.5f) * inkA);
            root.localPosition = at + new Vector3(0, 0, -0.012f * titleHover);   // 看着它时微微往前凸，提示“能捏”

            // 捏合：看着歌名牌 → 开 / 关歌单；看着某张卡 → 换歌 / 翻页
            bool pinched = PinchStarted();
            if (pinched && show && titleFade < 0.3f && titleHover > 0.5f)
            {
                if (session && !session.CanPickSong && session.InTutorial) nudgeUntil = Time.time + 2.6f;
                else { shelfOpen = !shelfOpen; ShelfKnown = true; if (shelfOpen) { page = -1; Rebuild(); } }
                pinched = false;
            }
            UpdateShelf(show && shelfOpen && wf < 0.5f, pinched);   // 右边百叶窗散到一半，歌单先收起（下一首窗户回来再摆出来）
            TKFont.Sync();
        }

        Vector2 plateHalf = new(0.15f, 0.05f);

        void Relayout(string want, string wantSub)
        {
            shownText = want; shownSub = wantSub; langVer = TKLang.Version;
            text.characterSize = 0.0098f; text.text = want;
            sub.text = wantSub;
            var b = text.GetComponent<MeshRenderer>().localBounds;
            if (b.size.x + 0.09f > 1.3f) { text.characterSize = 0.0098f * 1.18f / Mathf.Max(0.01f, b.size.x); b = text.GetComponent<MeshRenderer>().localBounds; }
            var sb = sub.GetComponent<MeshRenderer>().localBounds;
            bool hasSub = !string.IsNullOrEmpty(wantSub);
            float w = Mathf.Clamp(Mathf.Max(b.size.x, hasSub ? sb.size.x : 0f) + 0.09f, 0.24f, 1.3f);
            float h = Mathf.Max(0.1f, b.size.y + 0.045f) + (hasSub ? 0.05f : 0f);
            float cy = hasSub ? -0.025f : b.center.y;
            text.transform.localPosition = new Vector3(0, hasSub ? 0.004f : 0f, -0.004f);
            plate.GetComponent<MeshFilter>().sharedMesh = ScoreHUD.Pill(w, h, 0.006f);
            rim.GetComponent<MeshFilter>().sharedMesh = ScoreHUD.Pill(w + 0.006f, h + 0.006f, 0.004f);
            plate.localPosition = new Vector3(b.center.x, cy, 0.0035f); rim.localPosition = new Vector3(b.center.x, cy, 0.0055f);
            plateHalf = new Vector2(w / 2f, h / 2f);
        }

        // ---------- 歌单 ----------
        void Rebuild()
        {
            var list = session ? session.Playlist : null;
            if (list == null || list.Length == 0) return;
            int pages = (list.Length + PerPage - 1) / PerPage;
            if (page < 0) { int cur = System.Array.IndexOf(list, director.songId); page = Mathf.Max(0, cur) / PerPage; }
            page = (page % pages + pages) % pages;
            foreach (var c in cards) if (c.t) Destroy(c.t.gameObject);
            cards.Clear();
            var items = new List<(int kind, string id)>();
            if (pages > 1) items.Add((1, ""));
            for (int i = page * PerPage; i < Mathf.Min(list.Length, (page + 1) * PerPage); i++) items.Add((0, list[i]));
            if (pages > 1) items.Add((2, ""));
            for (int i = 0; i < items.Count; i++)
            {
                var c = new Card { id = items[i].id, kind = items[i].kind, delay = i * 0.07f };
                c.t = new GameObject("Card").transform; c.t.SetParent(transform, false);
                bool small = c.kind != 0;
                c.plate = Part(c.t, "Plate", plateMaterial, 0.004f); c.plate.GetComponent<MeshFilter>().sharedMesh = small ? pageMesh : cardMesh;
                c.rim = Part(c.t, "Rim", rimMaterial ? rimMaterial : plateMaterial, 0.0065f); c.rim.GetComponent<MeshFilter>().sharedMesh = small ? pageRimMesh : cardRimMesh;
                c.text = TKFont.Text("Name", c.t, ink, small ? 0.0064f : 0.0084f, TKFont.Kai);
                c.text.transform.localPosition = new Vector3(0, 0, -0.005f);
                c.text.text = c.kind == 1 ? TKLang.Pick("上一页", "previous") : c.kind == 2 ? TKLang.Pick("下一页", "next") : Name(c.id);
                var tb = c.text.GetComponent<MeshRenderer>().localBounds;
                if (tb.size.x > cardW - 0.11f) c.text.characterSize *= (cardW - 0.11f) / tb.size.x;
                c.slot = shelfAt + Vector3.down * (cardGap * i);
                cards.Add(c);
            }
            pageLang = TKLang.Version;
        }
        int pageLang = -1;

        void UpdateShelf(bool want, bool pinched)
        {
            if (want && (cards.Count == 0 || pageLang != TKLang.Version)) Rebuild();
            var face = head ? Quaternion.LookRotation(Flat(transform.TransformPoint(shelfAt) - head.position), Vector3.up) : transform.rotation;
            var from = transform.TransformPoint(at);
            Card hit = null; float best = 0.5f;
            foreach (var c in cards)
            {
                c.show = Mathf.MoveTowards(c.show, want ? 1f : 0f, Time.deltaTime * (want ? 2.2f : 4f));
                float k = Mathf.Clamp01((c.show - (want ? c.delay : 0f)) / Mathf.Max(0.01f, 1f - c.delay));
                float e = k * k * (3f - 2f * k);
                bool vis = e > 0.001f;
                if (c.t.gameObject.activeSelf != vis) c.t.gameObject.SetActive(vis);
                if (!vis) continue;
                var slotW = transform.TransformPoint(c.slot);
                var pos = Vector3.Lerp(from, slotW, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.08f;   // 从歌名牌里飞出来，带一点弧线
                bool small = c.kind != 0;
                var half = small ? new Vector2(cardW * 0.275f, cardH * 0.36f) : new Vector2(cardW / 2f, cardH / 2f);
                float hv = e > 0.95f ? Hover(pos, face, half, 0.015f) : 0f;
                c.hover = Mathf.MoveTowards(c.hover, hv, Time.deltaTime * 8f);
                if (hv > best) { best = hv; hit = c; }
                c.t.SetPositionAndRotation(pos - face * Vector3.forward * (0.015f * c.hover), face);
                c.t.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, e) * (1f + 0.06f * c.hover);
                bool cur = c.kind == 0 && c.id == director.songId;
                c.plate.GetComponent<MeshRenderer>().sharedMaterial = cur ? goldPlate : plateMaterial;   // 正在弹的那首：香槟金牌
                TKFont.Alpha(c.text, new Color(0.2f, 0.11f, 0.05f), Mathf.Clamp01(e * 1.5f - 0.4f));
            }
            if (want && pinched && hit != null)
            {
                if (hit.kind == 1) { page--; Rebuild(); foreach (var c in cards) c.show = 1f; }
                else if (hit.kind == 2) { page++; Rebuild(); foreach (var c in cards) c.show = 1f; }
                else if (hit.id != director.songId && session && session.CanPickSong) session.PickSong(hit.id);
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v : Vector3.forward; }

        /// <summary>视线落在一块牌子上（牌子中心 pos、朝向 rot、半宽高 half，四周放宽 pad）：0 没看着 … 1 看着。</summary>
        float Hover(Vector3 pos, Quaternion rot, Vector2 half, float pad)
        {
            var ray = Gaze();
            var n = rot * Vector3.forward;
            float denom = Vector3.Dot(ray.direction, n);
            if (Mathf.Abs(denom) < 1e-4f) return 0f;
            float t = Vector3.Dot(pos - ray.origin, n) / denom;
            if (t < 0.2f || t > 8f) return 0f;
            var lp = Quaternion.Inverse(rot) * (ray.origin + ray.direction * t - pos);
            return Mathf.Abs(lp.x) < half.x * transform.lossyScale.x + pad && Mathf.Abs(lp.y) < half.y + pad ? 1f : 0f;
        }

        // ---------- 视线（PICO 眼动；拿不到就用头的朝向）----------
        /// <summary>编辑器测试：指定视线（不为空时用它）。</summary>
        [System.NonSerialized] public Ray? simGaze;
        Vector3 gazeDir;
        Ray Gaze()
        {
            if (simGaze.HasValue) return simGaze.Value;
            var h = head ? head : Camera.main ? Camera.main.transform : transform;
            var target = new Ray(h.position, h.forward);
#if ENABLE_PICO_XR_SDK && UNITY_ANDROID && !UNITY_EDITOR
            if (ReadEye(h, out var e)) target = e;
#endif
            gazeDir = gazeDir == Vector3.zero ? target.direction : Vector3.Slerp(gazeDir, target.direction, 1f - Mathf.Exp(-Time.deltaTime / 0.04f)).normalized;
            return new Ray(target.origin, gazeDir);
        }

#if ENABLE_PICO_XR_SDK
        EyeTrackingData eye = new EyeTrackingData { eyeDatas = new PerEyeData[(int)PerEyeUsage.EyeCount] };
        bool eyeStarted; float eyeRetry, eyeBad;
        int gazeFrame = -1; Ray gazeCache; bool gazeOk;
        bool ReadEye(Transform h, out Ray ray)
        {
            if (gazeFrame == Time.frameCount) { ray = gazeCache; return gazeOk; }
            gazeFrame = Time.frameCount; ray = default; gazeOk = false;
            if (!eyeStarted)
            {
                eyeRetry -= Time.unscaledDeltaTime;
                if (eyeRetry > 0f) return false;
                eyeRetry = 2f;
                var start = new EyeTrackingStartInfo { needCalibration = 0, mode = EyeTrackingMode.PXR_ETM_BOTH };
                eyeStarted = PXR_MotionTracking.StartEyeTracking(ref start) == 0;
                if (!eyeStarted) return false;
            }
            var info = new EyeTrackingDataGetInfo { displayTime = 0, flags = EyeTrackingDataGetFlags.PXR_EYE_POSITION | EyeTrackingDataGetFlags.PXR_EYE_ORIENTATION };
            if (PXR_MotionTracking.GetEyeTrackingData(ref info, ref eye) != 0 || eye.eyeDatas == null) { Bad(); return false; }
            var c = eye.eyeDatas[(int)PerEyeUsage.Combined];
            if (c.isPoseValid == 0) { Bad(); return false; }
            var origin = h.parent;
            var o = new Vector3(c.pose.position.x, c.pose.position.y, -c.pose.position.z);
            var q = new Quaternion(c.pose.orientation.x, c.pose.orientation.y, -c.pose.orientation.z, -c.pose.orientation.w);
            var dir = origin ? origin.TransformDirection(q * Vector3.forward) : q * Vector3.forward;
            ray = new Ray(origin ? origin.TransformPoint(o) : o, dir.normalized);
            eyeBad = 0f; gazeCache = ray; gazeOk = true;
            return true;
        }
        void Bad() { if ((eyeBad += Time.unscaledDeltaTime) > 3f) { var stop = new EyeTrackingStopInfo(); PXR_MotionTracking.StopEyeTracking(ref stop); eyeStarted = false; eyeBad = 0f; } }
        void OnApplicationPause(bool p) { if (!p) { eyeStarted = false; eyeRetry = 0f; } }
#endif

        // ---------- 捏合（拇指尖碰食指尖）----------
        readonly bool[] pinching = new bool[2];
        /// <summary>编辑器测试：下一帧当作捏了一下。</summary>
        [System.NonSerialized] public bool simPinch;
        bool PinchStarted()
        {
            bool started = simPinch; simPinch = false;
            if (!hands) return started;
            for (int i = 0; i < 2; i++)
            {
                var j = i == 0 ? hands.Left : hands.Right;
                if (j == null) { pinching[i] = false; continue; }
                float d = Vector3.Distance(j.thumbTip, j.indexTip);
                bool was = pinching[i];
                pinching[i] = was ? d < 0.032f : d < 0.018f;
                if (pinching[i] && !was) started = true;
            }
            return started;
        }
    }
}
