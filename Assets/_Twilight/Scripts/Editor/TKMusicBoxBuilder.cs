using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Twilight.EditorTools
{
    /// <summary>搭封面八音盒（Art/Models/MusicBox：MusicBox.fbx 由 Tools/blender_musicbox.py 生成，Island.fbx 由 Tools/blender_island.py 生成）。
    /// 层级：MusicBox（TKMusicBox，跟随视线）/ Body（缩放、弹出）/ Model（盒子，转 180° 正面朝玩家）+ LidPivot（合页）+ Island（浮岛 + 缩小的 P2 村子，立在机芯的立柱上）+ SpinHint（“可以转”的箭头环）。
    /// Body 空间 = Blender 坐标 (x, z, y)：正面朝 −Z，合页在 (0, 0.088, 0.097)。</summary>
    public static class TKMusicBoxBuilder
    {
        const string Dir = "Assets/_Twilight/Art/Models/MusicBox";

        // 与 Tools/blender_musicbox.py、blender_island.py 一致
        const float H = 0.088f, HingeZ = 0.09043f;   // 合页 = Blender (0, HINGE_Y, H)
        const float IR = 0.09f, LX = 0.026f, LY = -0.022f, LR = 0.028f, WZ = 0.0015f;

        public static TKMusicBox Build(Transform head, HandTapInput hands, Material glow, Material letter, Material firefly)
        {
            var go = new GameObject("MusicBox");
            var box = go.AddComponent<TKMusicBox>();
            var body = new GameObject("Body").transform;
            body.SetParent(go.transform, false);
            var model = Instance($"{Dir}/MusicBox.fbx", body);
            model.name = "Model";
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            Materials(model);

            // 合页：盒盖挂在 LidPivot 下，开盖 = 绕本地 X 转
            var pivot = new GameObject("LidPivot").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(0, H, HingeZ);
            model.transform.Find("Lid").SetParent(pivot, true);

            var island = new GameObject("Island").transform;
            island.SetParent(body, false);
            var isl = Instance($"{Dir}/Island.fbx", island);
            isl.transform.localRotation = Quaternion.Euler(0, 180, 0);
            Materials(isl);
            Decorate(island);

            var hint = SpinRing(body);
            // 舞台光：盒下桌面一圈暖光（浮在空中也有“地”）、盒里往上打的暖光、小岛背后一圈柔光
            var glows = new List<MeshRenderer>
            {
                // 第十六轮（用户：盖子那里漏出来了、整个盒子有点亮）：去掉盒盖后面那块光环（比盖子大，光从盖沿漏出来），盒里 / 盒下的暖光也调弱
                Glow("GlowFloor", body, new Vector3(0, -0.002f, 0), Quaternion.Euler(90, 0, 0), new Vector2(0.52f, 0.42f), new Color(1f, 0.62f, 0.32f), 0.22f, 2.4f, 0f),
                Glow("GlowInside", body, new Vector3(0, 0.07f, 0.01f), Quaternion.Euler(90, 0, 0), new Vector2(0.2f, 0.15f), new Color(1f, 0.7f, 0.38f), 0.12f, 1.6f, 0f),
            };
            Reflection(go.transform);

            var mt = model.transform;
            box.head = head; box.hands = hands;
            box.body = body; box.lidPivot = pivot; box.island = island;
            box.spindle = mt.Find("Spindle"); box.drum = mt.Find("Drum");
            var teeth = new List<Transform>();
            for (int i = 0; ; i++) { var tt = mt.Find($"Tooth{i:00}"); if (!tt) break; teeth.Add(tt); }
            box.teeth = teeth.ToArray();
            box.btnPrev = mt.Find("BtnLeft"); box.btnNext = mt.Find("BtnRight");
            box.btnStart = mt.Find("BtnStart"); box.btnSettings = mt.Find("BtnSettings"); box.btnExit = mt.Find("BtnExit");
            box.spinHint = hint; box.glows = glows.ToArray();
            box.glowMaterial = glow; box.letterMaterial = letter; box.fireflyMaterial = firefly;
            box.titleMat = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_TitleArt.mat");
            box.plateMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_PanelIvory.mat");   // 盒盖上方提示瓷牌
            box.rimMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_PanelGold.mat");
            box.titleZhTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Tex/title_zh.png");
            box.titleEnTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Tex/title_en.png");
            foreach (var n in new[] { "Spindle", "Drum", "BtnSettings" }) if (!mt.Find(n)) Debug.LogWarning($"[TK] 八音盒模型里找不到 {n}");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
            return box;
        }

        static GameObject Instance(string path, Transform parent)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(TKArt.Model(path), parent);
            PrefabUtility.UnpackPrefabInstance(g, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return g;
        }

        /// <summary>手绘风材质（和琴、桌子同一套手绘木纹 / 草地 / 岩石贴图），按 FBX 材质槽名分配。</summary>
        static void Materials(GameObject g)
        {
            string tex = TKArt.TexDir, bt = $"{Dir}/Tex";
            Tex($"{bt}/box_wood.png", 2048); Tex($"{bt}/box_gold.png", 512); Tex($"{bt}/enamel.png", 2048); Tex($"{bt}/lid_painting.png", 1024); Tex($"{bt}/porcelain.png", 2048); Tex($"{bt}/title_zh.png", 2048); Tex($"{bt}/lid_inner.png", 2048); Tex($"{bt}/title_en.png", 2048);
            // 专用手绘贴图（Tripo 参照概念图生成 → Tools/make_tileable.py）：柔和宽木纹的缎面清漆木、带一点明暗的素金。
            // 不再用从颜色图硬推的法线（头显里高光一闪一闪）；光泽也收一点（高光走样会闪）。一张木纹铺满一整面，不重复。
            // 盒子用专用的棚拍着色器（Twilight/MusicBoxLit）：金、钢直接用 Blender Cycles 渲的材质球（Tools/blender_matcap.py），
            // 木头 = 手绘木纹 × 柔和暖光 + 清漆高光材质球 + 边光。不吃场景灯光——MR 里没有好的环境光，URP Lit 的金和清漆都像塑料。
            var map = new Dictionary<string, Material>
            {
                // 参考瑞士 REUGE 一类的高级八音盒：烟熏胡桃木（缎面，不上镜面漆）+ 很少的拉丝香槟金（细边、小包角）；金不再刺眼
                ["Wood"] = BoxMat("Box_Wood", 0, new Color(1.12f, 1.05f, 1.0f), $"{bt}/box_wood.png", "lacquer", 0.32f, 0.08f),
                ["WoodDark"] = BoxMat("Box_WoodDark", 0, new Color(0.62f, 0.55f, 0.52f), $"{bt}/box_wood.png", "lacquer", 0.28f, 0.06f),
                ["Gold"] = BoxMat("Box_Gold", 1, new Color(0.84f, 0.74f, 0.6f), $"{bt}/box_gold.png", "goldsoft", 0f, 0.0f),   // 第十六轮：金再压暗（用户：金太亮）   // 第十五轮（用户：不要太闪）：缎面金，高光是一片柔光，头显里不跳   // 第十四轮：Cycles 渲的抛光金材质球（有清楚的柔光箱反光和暗部反差，不再是一片橙色）   // 浓金：暗部古铜、亮部饱满金，不发白
                ["Plaque"] = BoxMat("Box_Plaque", 1, new Color(1.0f, 0.97f, 0.92f), $"{bt}/box_gold.png", "goldsatin", 0f, 0.04f),   // 铭牌：拉丝黄铜，字刻成深色
                ["Steel"] = BoxMat("Box_Steel", 1, new Color(1.0f, 0.93f, 0.8f), null, "steel", 0f, 0.05f),   // 钢梳 / 钉子：带一点香槟色，和缎面金一套
                ["Panel"] = BoxMat("Box_Panel", 0, new Color(0.7f, 0.64f, 0.6f), $"{bt}/box_wood.png", "lacquer", 0.18f, 0.04f),   // 盖内：深胡桃木
                ["Velvet"] = BoxMat("Box_Velvet", 0, new Color(0.96f, 0.89f, 0.77f), null, "lacquer", 0.42f, 0.1f),   // 缎面：褶子上有一道道光   // 奶油色缎面（去掉红丝绒）
                ["Ornament"] = Ornament(),
                // 第九轮（参考 Empire 鎏金饰匣）：一圈卷草浮雕金带（法线贴图扰动材质球 + 凹底压成古铜）、盖内一幅黄昏村庄油画
                ["Relief"] = ReliefMat(),
                ["Painting"] = BoxMat("Box_Painting", 4, Color.white, $"{bt}/lid_painting.png", "lacquer", 0f, 0f),
                // 第十四轮：盖内标题牌（Tools/make_title_art.py 烘的象牙瓷底 + 金箔字），和全游戏同一款字
                ["LidInner"] = BoxMat("Box_LidInner", 0, new Color(0.95f, 0.93f, 0.9f), $"{bt}/lid_inner.png", "lacquer", 0.22f, 0.03f),   // 第十七轮：盖内手绘瓷面（金色花边 + 小玫瑰）
                ["TitleArt"] = BoxMat("Box_TitleArt", 4, new Color(0.95f, 0.93f, 0.9f), $"{bt}/title_zh.png", "lacquer", 0f, 0f),   // 第十六轮：中英两张，TKMusicBox 按语言换
                // 第十一轮（用户参考图：法贝热彩蛋 / 利摩日瓷盒）：盒身四面嵌蓝紫珐琅手绘玫瑰面板（清漆光泽）、珍珠串边、红宝石镶嵌
                ["Enamel"] = BoxMat("Box_Enamel", 0, new Color(1.05f, 1.02f, 1.0f), $"{bt}/enamel.png", "lacquer", 0.42f, 0.05f),
                ["Pearl"] = BoxMat("Box_Pearl", 1, new Color(0.84f, 0.79f, 0.72f), null, "pearl", 0f, 0f),   // 暖白珍珠（参考图），不发蓝
                ["Gem"] = BoxMat("Box_Gem", 1, Color.white, null, "gem", 0f, 0f),
                ["Grass"] = IsleMat("Isle_Grass", $"{bt}/isle_grass_glaze.png", new Color(1f, 1f, 0.97f), 0.12f, 0.9f),   // 第十五轮（用户：小岛和八音盒风格统一）：淡青釉
                ["Rock"] = IsleMat("Isle_Rock", $"{bt}/isle_rock_glaze.png", new Color(1f, 0.98f, 0.95f), 0.18f, 0.8f),   // 象牙白釉瓷雕
                ["Lakebed"] = IsleMat("Isle_Lakebed", $"{bt}/isle_grass_glaze.png", new Color(0.8f, 0.86f, 0.84f), 0.15f, 0.5f),
                ["Water"] = WaterMat(),
                // 第十三轮（用户选定圆鼓象牙白瓷盒）：盒身手绘玫瑰瓷（清漆光泽）、盖子与按钮象牙白瓷、内衬奶油色缎面
                ["Porcelain"] = BoxMat("Box_Porcelain", 0, new Color(0.93f, 0.9f, 0.86f), $"{bt}/porcelain.png", "lacquer", 0.26f, 0.03f),   // 整个盒子收暗一档   // 釉面：高光更明显
                ["Ivory"] = BoxMat("Box_Ivory", 0, new Color(0.92f, 0.88f, 0.81f), null, "lacquer", 0.24f, 0.03f),
            };
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i] ? ms[i].name : "";
                    foreach (var kv in map) if (n == kv.Key || n.StartsWith(kv.Key + " ") || n.StartsWith(kv.Key + ".")) { ms[i] = kv.Value; break; }
                    if (ms[i] && !ms[i].name.StartsWith("Box_") && !ms[i].name.StartsWith("Isle_")) Debug.LogWarning($"[TK] 八音盒材质槽没对上：{n}");
                }
                r.sharedMaterials = ms;
            }
        }

        /// <summary>曲目面板：八音盒左侧、往里转 22° 面向玩家的一块圆角木板（盒子同款清漆木 + 金边 + 内凹的深色面板），
        /// 跟着盒子缩放、弹出；TKSongPanel 在面板前面写字、放高亮。</summary>
        public static TKSongPanel SongPanel(TKMusicBox box, HandTapInput hands, Material glow)
        {
            var root = new GameObject("SongPanel").transform;
            root.SetParent(box.body, false);
            // 选曲卡片：初始藏在铭牌的位置（小且扁）；打开时从铭牌放大、浮到正面盖住按钮（动画在 TKSongPanel）
            root.localPosition = new Vector3(0f, 0.066f, -0.1f);
            root.localRotation = Quaternion.identity;
            var board = new GameObject("Board").transform;
            board.SetParent(root, false);
            PanelVelvetMat();
            // 第十四轮：和圆鼓瓷盒同一套——一道细金边 → 一圈手绘玫瑰瓷边 → 象牙白瓷面（深褐字刻在瓷面上）+ 珍珠串边
            Slab(board, "Frame", 0.27f, 0.254f, 0.008f, 0.0f, "Box_PanelGold");
            Slab(board, "Wood", 0.266f, 0.25f, 0.012f, -0.0015f, "Box_PanelPorcelain");
            Slab(board, "Face", 0.244f, 0.228f, 0.004f, -0.0072f, "Box_PanelIvory");
            PanelPearls(board, 0.25f, 0.234f, -0.0094f);
            // 分隔线（细金线）：标题下、分类下、底下一排上
            foreach (float y in new[] { 0.09f, 0.059f, -0.081f })
            {
                var l = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(l.GetComponent<Collider>());
                l.name = "Rule"; l.transform.SetParent(board, false);
                l.transform.localPosition = new Vector3(0, y, -0.0098f); l.transform.localScale = new Vector3(0.228f, 0.0008f, 0.0006f);
                l.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_Gold.mat");
            }
            var text = new GameObject("Text").transform;
            text.SetParent(root, false);
            text.localPosition = new Vector3(0, 0, -0.0095f);
            var panel = text.gameObject.AddComponent<TKSongPanel>();
            panel.box = box; panel.hands = hands; panel.board = board; panel.glowMaterial = glow; panel.letterMaterial = box.letterMaterial;
            panel.highlightMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/GlowInside.mat");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
            return panel;
        }

    /// <summary>结算瓷卡片（单位高度 1、宽 0.86，底边中心在原点，正面朝 −Z）：金边 → 玫瑰瓷边 → 象牙瓷面，
    /// 上下两道细金线、中间评级圆章（金圈 + 瓷心）、底部提示瓷条（金边）。存成 Prefabs/ResultCard.prefab，ScoreHUD 按 boardHeight 缩放。</summary>
    public static GameObject ResultCard()
    {
        PanelVelvetMat();
        var root = new GameObject("ResultCard");
        var body = new GameObject("Body").transform; body.SetParent(root.transform, false); body.localPosition = new Vector3(0, 0.5f, 0);
        Slab(body, "CardFrame", 0.875f, 1.015f, 0.02f, 0f, "Box_PanelGold");
        Slab(body, "CardBorder", 0.86f, 1.0f, 0.03f, -0.004f, "Box_PanelPorcelain");
        Slab(body, "CardFace", 0.78f, 0.92f, 0.012f, -0.019f, "Box_PanelIvory");
        var gold = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_PanelGold.mat");
        var ivory = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_PanelIvory.mat");
        void Rule(float y, float w)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(c.GetComponent<Collider>());
            c.name = "Rule"; c.transform.SetParent(root.transform, false);
            c.transform.localPosition = new Vector3(0, y, -0.0262f); c.transform.localScale = new Vector3(w, 0.0035f, 0.002f);
            c.GetComponent<MeshRenderer>().sharedMaterial = gold;
        }
        Rule(0.835f, 0.62f); Rule(0.37f, 0.62f);
        void Disc(string n, float r, float z, float d, Material m, Vector3 at)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = n; g.transform.SetParent(root.transform, false);
            g.transform.localRotation = Quaternion.Euler(90, 0, 0); g.transform.localScale = new Vector3(r * 2, d / 2, r * 2);
            g.transform.localPosition = at + new Vector3(0, 0, z);
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
        var med = new Vector3(0, 0.672f, 0);
        Disc("MedalRing", 0.118f, -0.027f, 0.012f, gold, med);
        Disc("MedalFace", 0.1f, -0.031f, 0.012f, ivory, med);
        var pill = new GameObject("PromptBar").transform; pill.SetParent(root.transform, false); pill.localPosition = new Vector3(0, 0.112f, 0);
        Slab(pill, "CardPromptRim", 0.64f, 0.105f, 0.012f, -0.027f, "Box_PanelGold");
        Slab(pill, "CardPromptFace", 0.625f, 0.09f, 0.012f, -0.029f, "Box_PanelIvory");
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        string path = "Assets/_Twilight/Prefabs/ResultCard.prefab";
        System.IO.Directory.CreateDirectory("Assets/_Twilight/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }
        /// <summary>演奏中的暂停键：挂在琴（Stage）上，琴键左上方（左边 22 cm、比键面高 11 cm、往后 4 cm），往后仰 30° 正对视线。
        /// 和八音盒同一套材质：胡桃木钮面 + 拉丝铜圈 + 两道竖条图标，下面一行“暂停”。</summary>
        public static TKPauseButton PauseButton(Transform stage, HandTapInput hands, Material glow)
        {
            var root = new GameObject("PauseButton").transform;
            root.SetParent(stage, false);
            root.localPosition = new Vector3(-0.235f, 0.16f, 0.17f);       // 琴左上、比分数牌更靠后更高（不和“得分”叠在一起）
            root.localRotation = Quaternion.Euler(30f, 0f, 0f);
            var gold = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_Gold.mat");
            var wood = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_Ivory.mat");   // 第十四轮：和八音盒按钮同一套白瓷
            GameObject Cyl(string n, Transform p, float r, float d, float z, Material m)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.name = n; g.transform.SetParent(p, false);
                g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);          // 圆柱轴朝 Z（朝人）
                g.transform.localScale = new Vector3(r * 2, d / 2, r * 2);
                g.transform.localPosition = new Vector3(0, 0, z);
                g.GetComponent<MeshRenderer>().sharedMaterial = m;
                return g;
            }
            Cyl("Ring", root, 0.025f, 0.006f, 0.002f, gold);
            var face = new GameObject("Face").transform; face.SetParent(root, false);
            Cyl("Cap", face, 0.021f, 0.008f, -0.002f, wood);
            foreach (float x in new[] { -0.0045f, 0.0045f })
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(bar.GetComponent<Collider>());
                bar.name = "Bar"; bar.transform.SetParent(face, false);
                bar.transform.localPosition = new Vector3(x, 0, -0.0065f); bar.transform.localScale = new Vector3(0.003f, 0.012f, 0.0015f);
                bar.GetComponent<MeshRenderer>().sharedMaterial = gold;
            }
            var label = TKFont.Text("Label", root, TKFont.Mat(glow, 0, TKFont.Brush), 0.0017f, TKFont.Brush);
            label.transform.localPosition = new Vector3(0, -0.036f, -0.004f);
            var pb = root.gameObject.AddComponent<TKPauseButton>();
            pb.hands = hands; pb.face = face; pb.label = label; pb.radius = 0.025f;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
            return pb;
        }

        /// <summary>圆角薄板（z 方向厚 depth、正面在 -z）。</summary>
        /// <summary>选曲面板内面的深蓝丝绒（哑光，不吃反光），没有就建。</summary>
        static void PanelVelvetMat()
        {
            // 深蓝丝绒：用盒子同一个棚拍着色器的"哑光"模式（不吃场景灯光，MR 里不会发黑）
            BoxMat("Box_PanelVelvet", 2, new Color(0.2f, 0.19f, 0.5f), null, "lacquer", 0f, 0.06f);
            // 边框珐琅：单独一份，按面板比例重设平铺（贴图 4:1，直接用会竖着拉长 4 倍）
            var en = BoxMat("Box_PanelEnamel", 0, new Color(1.05f, 1.02f, 1.0f), $"{Dir}/Tex/enamel.png", "lacquer", 0.38f, 0.05f);
            en.SetTextureScale("_MainTex", new Vector2(0.5f, 2f)); EditorUtility.SetDirty(en);
            // 第十四轮：面板边框用盒身同款手绘玫瑰瓷（贴图 4:1，按面板 UV 比例平铺，花不拉长）
            var pc = BoxMat("Box_PanelPorcelain", 0, new Color(1.05f, 1.02f, 1.0f), $"{Dir}/Tex/porcelain.png", "lacquer", 0.45f, 0.05f);
            pc.SetTextureScale("_MainTex", new Vector2(1.2f, 4.8f)); pc.SetFloat("_NoAO", 1f); EditorUtility.SetDirty(pc);
            // 面板是平板，盒子那套“越往下越暗”的压暗不适用（会让整块面板发灰）：面板用自己的一份材质，关掉压暗
            var pi = BoxMat("Box_PanelIvory", 0, new Color(1.04f, 1.01f, 0.95f), null, "lacquer", 0.32f, 0.04f); pi.SetFloat("_NoAO", 1f); EditorUtility.SetDirty(pi);
            var pg = BoxMat("Box_PanelGold", 1, new Color(1.08f, 1.0f, 0.86f), $"{Dir}/Tex/box_gold.png", "goldsoft", 0f, 0f); pg.SetFloat("_NoAO", 1f); EditorUtility.SetDirty(pg);
        }

        /// <summary>内面一圈珍珠（和八音盒上沿同款 Box_Pearl）：一个网格里排满小球。</summary>
        static void PanelPearls(Transform board, float w, float h, float z)
        {
            // 低模小球（8×5，40 个顶点）：内置球体 515 个顶点，170 颗合起来超过 65535 上限
            var src = new Mesh { name = "pearl" };
            { var vv = new List<Vector3>(); var tt = new List<int>(); int U = 8, V = 5;
              for (int j = 0; j <= V; j++) for (int i = 0; i <= U; i++) { float th = Mathf.PI * j / V, ph = 2 * Mathf.PI * i / U; vv.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph))); }
              for (int j = 0; j < V; j++) for (int i = 0; i < U; i++) { int a0 = j * (U + 1) + i, b0 = a0 + U + 1; tt.AddRange(new[] { a0, a0 + 1, b0, b0, a0 + 1, b0 + 1 }); }
              src.SetVertices(vv); src.SetTriangles(tt, 0); src.RecalculateNormals(); }
            var pts = new List<Vector3>(); float step = 0.0052f, r = Mathf.Min(w, h) * 0.07f;
            void Edge(Vector2 a, Vector2 b) { int n = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(a, b) / step)); for (int i = 0; i < n; i++) { var p = Vector2.Lerp(a, b, i / (float)n); pts.Add(new Vector3(p.x, p.y, z)); } }
            void Arc(Vector2 c, float a0) { int n = Mathf.Max(1, Mathf.RoundToInt(r * Mathf.PI / 2 / step)); for (int i = 0; i < n; i++) { float a = (a0 + 90f * i / n) * Mathf.Deg2Rad; pts.Add(new Vector3(c.x + r * Mathf.Cos(a), c.y + r * Mathf.Sin(a), z)); } }
            float hw = w / 2, hh = h / 2;
            Edge(new(hw, -hh + r), new(hw, hh - r)); Arc(new(hw - r, hh - r), 0);
            Edge(new(hw - r, hh), new(-hw + r, hh)); Arc(new(-hw + r, hh - r), 90);
            Edge(new(-hw, hh - r), new(-hw, -hh + r)); Arc(new(-hw + r, -hh + r), 180);
            Edge(new(-hw + r, -hh), new(hw - r, -hh)); Arc(new(hw - r, -hh + r), 270);
            var combine = new List<CombineInstance>();
            foreach (var p in pts) combine.Add(new CombineInstance { mesh = src, transform = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * 0.0017f) });
            var mesh = new Mesh { name = "PanelPearls" }; mesh.CombineMeshes(combine.ToArray(), true, true);
            string mp = $"{Dir}/Panel_Pearls.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(mp); if (old) { old.Clear(); EditorUtility.CopySerialized(mesh, old); mesh = old; } else AssetDatabase.CreateAsset(mesh, mp);
            var go = new GameObject("Pearls"); go.transform.SetParent(board, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_Pearl.mat");
        }

        static void Slab(Transform parent, string name, float w, float h, float depth, float z, string mat)
        {
            const int seg = 6; float r = Mathf.Min(w, h) * 0.07f;
            var outline = new List<Vector2>();
            Vector2[] cs = { new(w / 2 - r, h / 2 - r), new(-w / 2 + r, h / 2 - r), new(-w / 2 + r, -h / 2 + r), new(w / 2 - r, -h / 2 + r) };
            for (int c = 0; c < 4; c++)
                for (int k = 0; k <= seg; k++)
                {
                    float a = (c * 90f + 90f * k / seg) * Mathf.Deg2Rad;
                    outline.Add(cs[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            int n = outline.Count;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            v.Add(new Vector3(0, 0, z - depth / 2)); uv.Add(new Vector2(0.5f, 0.5f));
            foreach (var p in outline) { v.Add(new Vector3(p.x, p.y, z - depth / 2)); uv.Add(new Vector2(p.x / 0.3f + 0.5f, p.y / 0.3f + 0.5f)); }
            for (int i = 0; i < n; i++) tri.AddRange(new[] { 0, 1 + (i + 1) % n, 1 + i });
            int b0 = v.Count;
            v.Add(new Vector3(0, 0, z + depth / 2)); uv.Add(new Vector2(0.5f, 0.5f));
            foreach (var p in outline) { v.Add(new Vector3(p.x, p.y, z + depth / 2)); uv.Add(new Vector2(p.x / 0.3f + 0.5f, p.y / 0.3f + 0.5f)); }
            for (int i = 0; i < n; i++) tri.AddRange(new[] { b0, b0 + 1 + i, b0 + 1 + (i + 1) % n });
            int s0 = v.Count;
            for (int i = 0; i <= n; i++)
            {
                var p = outline[i % n]; float u = i / (float)n;
                v.Add(new Vector3(p.x, p.y, z - depth / 2)); uv.Add(new Vector2(u * 3f, 0));
                v.Add(new Vector3(p.x, p.y, z + depth / 2)); uv.Add(new Vector2(u * 3f, 0.05f));
                if (i < n) { int a = s0 + i * 2; tri.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            }
            string path = $"{Dir}/Panel_{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh { name = "Panel_" + name }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/{mat}.mat");
        }

        /// <summary>小岛湖面：Twilight/IsleWater（深浅、细波纹、菲涅尔反射暖色夜空、月光高光、岸边白边）。</summary>
        static Material WaterMat()
        {
            string path = $"{TKArt.MatDir}/Isle_Water.mat";
            var sh = Shader.Find("Twilight/IsleWater");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>小岛的釉面材质：和盒子同一个棚拍着色器（木 / 瓷模式 + 清漆高光），贴图平铺。</summary>
        static Material IsleMat(string name, string tex, Color c, float gloss, float tiling)
        {
            Tex(tex, 1024);
            var m = BoxMat(name, 0, c, tex, "lacquer", gloss, 0.04f);
            m.SetTextureScale("_MainTex", new Vector2(tiling, tiling));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>棚拍着色器材质（Twilight/MusicBoxLit）。mode：0 木（清漆）/ 1 金属 / 2 哑光。</summary>
        static Material BoxMat(string name, int mode, Color color, string tex, string matcap, float gloss, float rim)
        {
            string path = $"{TKArt.MatDir}/{name}.mat";
            var sh = Shader.Find("Twilight/MusicBoxLit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            string mp = $"{Dir}/Tex/matcap_{matcap}.png";
            Tex(mp, 512);
            m.SetTexture("_Matcap", AssetDatabase.LoadAssetAtPath<Texture2D>(mp));
            m.SetTexture("_MainTex", tex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(tex) : Texture2D.whiteTexture);
            m.SetColor("_Color", color); m.SetFloat("_Mode", mode); m.SetFloat("_Gloss", gloss); m.SetFloat("_Rim", rim);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>贴图导入：三线性 + 各向异性 8（斜着看不糊、不闪）、限制尺寸。</summary>
        static void Tex(string p, int max)
        {
            if (AssetImporter.GetAtPath(p) is TextureImporter ti && (ti.maxTextureSize != max || (p.Contains("matcap") && ti.wrapMode != TextureWrapMode.Clamp) || ti.anisoLevel != 8 || ti.filterMode != FilterMode.Trilinear || !ti.mipmapEnabled))
            {
                ti.wrapMode = p.Contains("matcap") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.maxTextureSize = max; ti.anisoLevel = 8; ti.filterMode = FilterMode.Trilinear; ti.mipmapEnabled = true; ti.textureType = TextureImporterType.Default;
                ti.SaveAndReimport();
            }
        }

        /// <summary>盒子自带的反射：暖色小房间（上亮下暗，前上方一盏暖灯、侧面一扇窗）。MR 里没有天空盒，没有它金和清漆都是死的。</summary>
        static void Reflection(Transform root)
        {
            string p = $"{Dir}/BoxReflection.cubemap";
            var cm = AssetDatabase.LoadAssetAtPath<Cubemap>(p);
            const int N = 64;
            if (!cm) { cm = new Cubemap(N, TextureFormat.RGBAHalf, true); AssetDatabase.CreateAsset(cm, p); }
            var faces = new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ };
            var lamp = new Vector3(0.2f, 0.75f, -0.6f).normalized;      // 玩家头顶前方的暖灯
            var win = new Vector3(-0.9f, 0.3f, 0.3f).normalized;        // 左侧的窗
            var px = new Color[N * N];
            foreach (var f in faces)
            {
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                        Vector3 d = f switch
                        {
                            CubemapFace.PositiveX => new Vector3(1, -v, -u), CubemapFace.NegativeX => new Vector3(-1, -v, u),
                            CubemapFace.PositiveY => new Vector3(u, 1, v), CubemapFace.NegativeY => new Vector3(u, -1, -v),
                            CubemapFace.PositiveZ => new Vector3(u, -v, 1), _ => new Vector3(-u, -v, -1),
                        };
                        d.Normalize();
                        float up = d.y * 0.5f + 0.5f;
                        var c = Color.Lerp(new Color(0.16f, 0.1f, 0.07f), new Color(0.75f, 0.58f, 0.42f), Mathf.SmoothStep(0, 1, up));
                        c += new Color(1.8f, 1.45f, 1.0f) * Mathf.Pow(Mathf.Max(0, Vector3.Dot(d, lamp)), 40f);
                        c += new Color(0.7f, 0.75f, 0.85f) * Mathf.Pow(Mathf.Max(0, Vector3.Dot(d, win)), 12f);
                        c.a = 1;
                        px[y * N + x] = c;
                    }
                cm.SetPixels(px, f);
            }
            cm.Apply(true);
            EditorUtility.SetDirty(cm);
            var probe = new GameObject("Reflection").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(root, false);
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = cm;
            probe.size = new Vector3(0.8f, 0.8f, 0.8f);
            probe.center = new Vector3(0, 0.15f, 0);
            probe.importance = 10;
            probe.intensity = 1.1f;
        }

        /// <summary>金色藤蔓贴花：Lit + 透明度裁剪（双面无所谓，贴花只从外面看）。</summary>
        static Material ReliefMat()
        {
            string p = $"{Dir}/Tex/relief_frieze.png";
            // RGBA 里 RGB 是法线、A 是高度：按普通贴图导（不能当 NormalMap，否则 A 丢掉），线性空间，横向平铺
            if (AssetImporter.GetAtPath(p) is TextureImporter ti && (ti.sRGBTexture || ti.wrapModeU != TextureWrapMode.Repeat || ti.maxTextureSize != 2048 || ti.anisoLevel != 8))
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp; ti.maxTextureSize = 2048;
                ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.filterMode = FilterMode.Trilinear;
                ti.SaveAndReimport();
            }
            var m = BoxMat("Box_Relief", 3, new Color(1.0f, 0.95f, 0.88f), null, "goldpolish", 0f, 0.05f);
            m.SetTexture("_Relief", AssetDatabase.LoadAssetAtPath<Texture2D>(p));
            m.SetFloat("_ReliefDepth", 1.6f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Ornament()
        {
            // 花纹改成木镶嵌（浅色缎木 + 乌木描边 + 沙烫阴影，Inlay.png 由 Ornament.png 的轮廓生成）：不再是一片会反光的亮金
            string p = $"{Dir}/Inlay.png";
            if (AssetImporter.GetAtPath(p) is TextureImporter ti && (ti.wrapMode != TextureWrapMode.Clamp || !ti.alphaIsTransparency))
            { ti.wrapMode = TextureWrapMode.Clamp; ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.SaveAndReimport(); }
            var m = TKArt.Lit("Box_Ornament", new Color(1f, 0.97f, 0.92f), 0.22f, 0f, albedo: p);
            m.SetTexture("_EmissionMap", null);
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.45f); m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>草地高度（Blender 坐标 x, y → z），与 blender_island.py 的 top_z 一致。</summary>
        static float TopZ(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y) / IR;
            float z = 0.007f * Mathf.Max(0f, 1 - r * r) + 0.0012f * Mathf.Sin(x * 90) * Mathf.Cos(y * 80);
            float dl = Mathf.Sqrt((x - LX) * (x - LX) + (y - LY) * (y - LY)) / LR;
            float w = Mathf.Clamp01((1.18f - dl) / 0.3f); w = w * w * (3 - 2 * w);
            float bowl = WZ - 0.0015f - 0.005f * Mathf.Max(0f, 1 - dl * dl);
            return z * (1 - w) + bowl * w;
        }

        /// <summary>浮岛上的小村子（P2 模型缩小，只留最精细的 LOD0）：后排风车、橡树、小屋，左边小屋和松树，湖上小船和睡莲，湖边芦苇、路灯、长椅、栅栏、花丛。
        /// (x, y) 是 Blender 坐标（y 越大越靠后）。</summary>
        static void Decorate(Transform island)
        {
            var items = new (string name, float x, float y, float size, int axis, float yaw)[]
            {
                ("windmill_tower", -0.04f, 0.03f, 0.068f, 0, 150f),
                ("great_oak", 0.035f, 0.042f, 0.05f, 0, 30f),
                ("house_tall", 0.07f, -0.012f, 0.034f, 0, 120f),
                ("cottage_tile", -0.062f, -0.02f, 0.026f, 0, 235f),
                ("cottage_cream", -0.022f, -0.019f, 0.026f, 0, 190f),
                ("pine_tree", -0.028f, -0.058f, 0.04f, 0, 0f),
                ("pine_tree", -0.056f, -0.054f, 0.032f, 0, 70f),
                ("lamp_post", -0.006f, 0.006f, 0.024f, 0, 180f),
                ("picket_fence", 0.0f, -0.06f, 0.026f, 1, 180f),
                ("rowboat", 0.03f, -0.02f, 0.018f, 1, 120f),
                ("lily_pads", 0.014f, -0.036f, 0.011f, 1, 0f),
                ("reeds", 0.05f, -0.042f, 0.013f, 0, 0f),
                ("bush_flower", -0.008f, -0.04f, 0.011f, 1, 0f),
            };
            var placed = new List<(string n, Bounds b)>();
            foreach (var it in items)
            {
                var pf = TKArt.P2Prefab(it.name);
                if (!pf) { Debug.LogWarning($"[TK] 浮岛缺模型 {it.name}"); continue; }
                var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, island);
                PrefabUtility.UnpackPrefabInstance(g, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // 只留一级 LOD：几厘米高的小模型在半米外屏幕占比约 5%–15%，大件用 LOD1、小件用 LOD2（LOD0 全岛三十多万面，头显扛不住）
                string keep = it.size >= 0.026f ? "LOD1" : "LOD2";
                if (!g.transform.Find(keep)) keep = "LOD0";
                var lg = g.GetComponent<LODGroup>(); if (lg) Object.DestroyImmediate(lg);
                for (int i = g.transform.childCount - 1; i >= 0; i--)
                    if (g.transform.GetChild(i).name != keep) Object.DestroyImmediate(g.transform.GetChild(i).gameObject);
                g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
                var b = LocalBounds(g, island);
                float cur = it.axis == 0 ? b.size.y : Mathf.Max(b.size.x, b.size.z);
                float s = it.size / Mathf.Max(cur, 1e-5f);
                bool onWater = it.name == "rowboat" || it.name == "lily_pads";
                float y = onWater ? WZ + (it.name == "lily_pads" ? 0.0003f : -0.0012f) : TopZ(it.x, it.y) - 0.0012f;
                g.transform.localScale = Vector3.one * s;
                g.transform.localRotation = Quaternion.Euler(0, it.yaw, 0);
                g.transform.localPosition = new Vector3(it.x, y, it.y);
                g.name = "Isle_" + it.name;
                if (!onWater) { if (UprightOK.Contains(it.name)) Upright(g, island); Ground(g, island); }
                if (it.name == "windmill_tower") TKVillage.Sails(g.transform);
                placed.Add((it.name, LocalBounds(g, island)));
            }
            // 穿模自查：大件之间水平占地不重叠（花丛、睡莲、芦苇这类地被不算）
            var big = placed.FindAll(p => p.n != "wildflower_patch" && p.n != "lily_pads" && p.n != "reeds" && p.n != "bush_flower");
            for (int i = 0; i < big.Count; i++)
                for (int j = i + 1; j < big.Count; j++)
                {
                    var a = big[i].b; var c = big[j].b;
                    float ox = Mathf.Min(a.max.x, c.max.x) - Mathf.Max(a.min.x, c.min.x), oz = Mathf.Min(a.max.z, c.max.z) - Mathf.Max(a.min.z, c.min.z);
                    if (ox > 0.002f && oz > 0.002f) Debug.LogWarning($"[TK] 浮岛占地重叠 {big[i].n} × {big[j].n}：{ox * 1000:F0}×{oz * 1000:F0} mm");
                }
            foreach (var p in placed)
            {
                float r = new Vector2(Mathf.Max(Mathf.Abs(p.b.min.x), Mathf.Abs(p.b.max.x)), Mathf.Max(Mathf.Abs(p.b.min.z), Mathf.Abs(p.b.max.z))).magnitude;
                if (r > IR * 1.05f) Debug.LogWarning($"[TK] 浮岛边缘外伸 {p.n}：{r * 1000:F0} mm");
            }
        }

        /// <summary>落地：取模型最低处一圈顶点（最低点往上 3 mm 以内，草地坐标），逐点和脚下草地高度比，
        /// 按“离地最高的那一点”整体往下压，再多压 0.6 mm——任何一点都不悬空（坡上高的那侧嵌进草里）。</summary>
        /// <summary>扶正：有些 P2 模型本身就是斜的（风车塔身前后歪约 11°、底面是个斜面）。
        /// 按高度切 11 层，取每层的中心，拟合出塔身中心线，把它转回竖直（绕底部中心转，不改朝向）。风车叶 / 灯笼这类挂件不参与拟合。</summary>
        /// <summary>塔身 / 墙身大致对称、可以按中心线扶正的模型（灯柱有弯臂、大树树冠偏一边，按中心线算会被转歪，不参与）。</summary>
        static readonly HashSet<string> UprightOK = new() { "windmill_tower", "house_tall", "cottage_tile", "cottage_cream", "pine_tree" };

        static void Upright(GameObject g, Transform island)
        {
            var pts = new List<Vector3>();
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            {
                bool skip = false;
                for (var t = mf.transform; t && t != g.transform; t = t.parent) if (t.name.Contains("Sail") || t.name.Contains("Glow")) skip = true;
                var m = mf.sharedMesh; if (skip || !m || !m.isReadable) continue;
                foreach (var v in m.vertices) pts.Add(island.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            if (pts.Count < 50) return;
            float y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var p in pts) { y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
            float H = y1 - y0;
            float sy = 0, sx = 0, sz = 0, syy = 0, syx = 0, syz = 0; int n = 0;
            for (int k = 1; k < 9; k++)                                 // 只看 11%–46% 高度（墙身 / 塔身；再往上是屋顶、风车头，会偏）
            {
                float a = y0 + H * (0.06f + 0.05f * k), b = a + H * 0.05f;
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue; int m = 0;
                foreach (var p in pts) if (p.y >= a && p.y < b) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); m++; }
                if (m < 20) continue;
                float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, yy = (a + b) / 2;
                sy += yy; sx += cx; sz += cz; syy += yy * yy; syx += yy * cx; syz += yy * cz; n++;
            }
            if (n < 4) return;
            float den = n * syy - sy * sy; if (Mathf.Abs(den) < 1e-12f) return;
            float kx = (n * syx - sy * sx) / den, kz = (n * syz - sy * sz) / den;
            var axis = new Vector3(kx, 1f, kz).normalized;
            float lean = Vector3.Angle(axis, Vector3.up);
            if (lean < 2.5f) return;                                   // 树、篱笆这类本来就不规整的不动
            var fix = Quaternion.FromToRotation(axis, Vector3.up);
            var pivot = new Vector3((sx - kx * sy) / n + kx * y0, y0, (sz - kz * sy) / n + kz * y0);   // 中心线在底面高度的位置
            var t0 = g.transform;
            var wp = island.TransformPoint(pivot);
            var wFix = island.rotation * fix * Quaternion.Inverse(island.rotation);
            t0.position = wp + wFix * (t0.position - wp);
            t0.rotation = wFix * t0.rotation;
            Debug.Log($"[TK] 浮岛扶正 {g.name}：原来歪 {lean:F1}°");
        }

        static void Ground(GameObject g, Transform island)
        {
            var pts = new List<Vector3>();
            float minY = float.MaxValue;
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; if (!m || !m.isReadable) continue;
                foreach (var v in m.vertices) { var p = island.InverseTransformPoint(mf.transform.TransformPoint(v)); pts.Add(p); minY = Mathf.Min(minY, p.y); }
            }
            if (pts.Count == 0) { Debug.LogWarning($"[TK] 浮岛落地：{g.name} 网格不可读"); return; }
            float gap = -1f;
            foreach (var p in pts)
                if (p.y < minY + 0.003f) gap = Mathf.Max(gap, p.y - TopZ(p.x, p.z));
            g.transform.localPosition += Vector3.down * (gap + 0.0006f);
            if (Mathf.Abs(gap) > 0.002f) Debug.Log($"[TK] 浮岛落地 {g.name}：调整 {(gap + 0.0006f) * 1000f:F1} mm");
        }

        static Bounds LocalBounds(GameObject g, Transform space)
        {
            Bounds b = default; bool has = false;
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; if (!m) continue;
                var mb = m.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!has) { b = new Bounds(p, Vector3.zero); has = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static MeshRenderer Glow(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 size, Color c, float strength, float falloff, float ring)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = new Vector3(size.x, size.y, 1);
            string mp = $"{TKArt.MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m) { m = new Material(Shader.Find("Twilight/BoxGlow")); AssetDatabase.CreateAsset(m, mp); }
            m.SetColor("_Color", c); m.SetFloat("_Strength", strength); m.SetFloat("_Falloff", falloff); m.SetFloat("_Ring", ring);
            EditorUtility.SetDirty(m);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m;
            return r;
        }

        /// <summary>小岛周围的“可以转”提示环：平放的圆环（内径 0.94、外径 1.06，半径由运行时按小岛缩放），材质 Twilight/SpinHint。</summary>
        static MeshRenderer SpinRing(Transform body)
        {
            const int N = 160;
            var v = new Vector3[(N + 1) * 2]; var uv = new Vector2[v.Length]; var tri = new List<int>();
            for (int i = 0; i <= N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i * 2] = d * 0.94f; v[i * 2 + 1] = d * 1.06f;
                uv[i * 2] = new Vector2(1f - i / (float)N, 0); uv[i * 2 + 1] = new Vector2(1f - i / (float)N, 1);
                if (i > 0) { int k = (i - 1) * 2; tri.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
            }
            string path = $"{Dir}/SpinRing.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh { name = "SpinRing" }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.vertices = v; mesh.uv = uv; mesh.triangles = tri.ToArray(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            var go = new GameObject("SpinHint");
            go.transform.SetParent(body, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            string mp = $"{TKArt.MatDir}/SpinHint.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!m) { m = new Material(Shader.Find("Twilight/SpinHint")); AssetDatabase.CreateAsset(m, mp); }
            r.sharedMaterial = m;
            return r;
        }
    }
}
