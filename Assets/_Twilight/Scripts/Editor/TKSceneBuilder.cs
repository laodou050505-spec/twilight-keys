using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Twilight.EditorTools
{
    /// <summary>搭建主场景 Assets/_Twilight/Scenes/Main.unity。
    /// 坐标约定：玩家坐在原点附近面朝 +Z；桌面高 0.74 m；琴键前沿在 z = 0.30；墙在 z = 2.6。
    /// 预览房间（地板 / 墙 / 桌子）只在编辑器里用来看效果，标记 EditorOnly，不进安装包（头显里是真实房间）。</summary>
    public static class TKSceneBuilder
    {
        public const string ScenePath = "Assets/_Twilight/Scenes/Main.unity";
        public const float TableY = 0.74f;
        public const float KeysZ = 0.30f;
        public const float WallZ = 2.6f;
        public const float WindowY = 1.25f;      // 窗洞中心离地
        public const float OpenW = 1.2f, OpenH = 0.9f;
        /// <summary>琴下沉：手指实际敲在真实桌面上，琴键顶面要和桌面齐平（白键顶比 Stage 原点高 2.8 cm）。</summary>
        public const float KeySink = 0.02f;

        [MenuItem("Twilight/1 搭建主场景")]
        public static void Build()
        {
            Debug.Log($"[TK] 哑光材质关高光 / 环境反射：{TKArt.MatteAll()} 个");   // 场景模型不反光（第十五轮）
            TKUIBake.Glyphs();                                     // 萤火虫 / 花瓣写字用的字形点云（Resources/TKGlyphs）
            TKArt.EnsureFolder("Assets/_Twilight/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Lighting();
            PreviewRoom();
            // World：放琴时整体移动 / 旋转到真实桌子上（琴、光河、窗户、窗中村、虚拟木桌、玩法）
            var world = new GameObject("World").transform;
            VirtualTable().transform.SetParent(world, true);
            var stage = new GameObject("Stage").transform;           // 摆在桌面上的一切（琴、光河、音块）
            stage.SetParent(world, false);
            stage.position = new Vector3(0f, TableY - KeySink, KeysZ);
            var piano = Piano(stage);
            var window = Window();
            window.SetParent(world, true);
            TKVillage.Build(window, out var sky);
            // 坡地上的模型下坡那侧会悬空：按脚下地面最低点放下去（上坡那侧嵌进山坡里）
            Debug.Log("[TK] 落地：" + TKAudit.Floating(true).Split('\n')[0]);
            sky.sun = Object.FindAnyObjectByType<Light>();
            var reveal = Reveal(window);
            reveal.transform.SetParent(world, true);
            var river = River(stage, window);
            var game = GameRoot(piano, river, reveal, window, sky);
            game.transform.SetParent(world, true);
            XRRig(world, stage, game.GetComponent<GameDirector>());
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void Lighting()
        {
            // 傍晚室内：暖色主光从左后上方来，环境光三色渐变（上暖下暗）
            var sun = new GameObject("Key Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.86f, 0.70f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(48f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.56f, 0.56f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.40f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.18f, 0.16f);
            RenderSettings.skybox = null;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        static void PreviewRoom()
        {
            var room = new GameObject("PreviewRoom (编辑器预览，不进包)") { tag = "EditorOnly" }.transform;
            var floor = TKArt.Lit("Preview_Floor", new Color(0.42f, 0.32f, 0.24f), 0.25f);
            var wall = TKArt.Lit("Preview_Wall", new Color(0.78f, 0.74f, 0.69f), 0.1f);
            var table = TKArt.Lit("Preview_Table", new Color(0.55f, 0.38f, 0.22f), 0.35f,
                albedo: $"{TKArt.TexDir}/oak_table.png", tiling: new Vector2(1.2f, 1.2f));
            Box("Floor", room, new Vector3(0, -0.01f, 1f), new Vector3(5f, 0.02f, 6f), floor);
            // 前墙在窗洞处开洞（头显里没有这面墙，是真实的墙）
            float hx = OpenW / 2, hy = OpenH / 2, zW = WallZ + 0.05f;
            Box("Wall Front L", room, new Vector3(-(2.5f + hx) / 2, 1.35f, zW), new Vector3(2.5f - hx, 2.7f, 0.1f), wall);
            Box("Wall Front R", room, new Vector3((2.5f + hx) / 2, 1.35f, zW), new Vector3(2.5f - hx, 2.7f, 0.1f), wall);
            Box("Wall Front B", room, new Vector3(0, (WindowY - hy) / 2, zW), new Vector3(OpenW, WindowY - hy, 0.1f), wall);
            Box("Wall Front T", room, new Vector3(0, (WindowY + hy + 2.7f) / 2, zW), new Vector3(OpenW, 2.7f - WindowY - hy, 0.1f), wall);
            Box("Wall Left", room, new Vector3(-2.2f, 1.35f, 1f), new Vector3(0.1f, 2.7f, 6f), wall);
            Box("Wall Right", room, new Vector3(2.2f, 1.35f, 1f), new Vector3(0.1f, 2.7f, 6f), wall);
            // 桌子：真实桌子位置放虚拟木桌（识别到桌面后出现，见 VirtualTable）
        }

        /// <summary>墙上的窗：窗框 + 两扇向房间推开的玻璃窗 + 窗洞遮罩（Portal 层）。根节点在窗洞中心，+Z 指向窗外（村庄）。</summary>
        public static Transform Window()
        {
            var root = new GameObject("Window").transform;
            root.position = new Vector3(0f, WindowY, WallZ);
            var src = TKArt.Model("Assets/_Twilight/Art/Models/Window/Window.fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, root);
            go.name = "WindowModel";
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // 手绘木纹（和村里的手绘卡通风格一致；原来的写实胡桃木照片贴图太突兀）
            var wood = TKArt.Lit("Window_Wood", new Color(0.92f, 0.8f, 0.7f), 0.12f, albedo: $"{TKArt.TexDir}/painted_wood.png", tiling: new Vector2(2f, 2f));
            var glass = TKArt.Lit("Window_Glass", new Color(0.85f, 0.92f, 0.95f, 0.18f), 0.92f);
            MakeTransparent(glass);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = r.name.StartsWith("Glass") ? glass : wood;
            // 两扇窗向房间推开
            var l = go.transform.Find("Sash_L"); var rr = go.transform.Find("Sash_R");
            if (l) l.Rotate(Vector3.up, 72f, Space.World);
            if (rr) rr.Rotate(Vector3.up, -72f, Space.World);
            WindowDecor(root);
            // 窗洞遮罩
            var mask = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mask.name = "PortalMask";
            Object.DestroyImmediate(mask.GetComponent<Collider>());
            mask.layer = TKRenderSetup.PortalLayer;
            mask.transform.SetParent(root, false);
            mask.transform.localScale = new Vector3(OpenW, OpenH, 1f);
            var pm = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/PortalMask.mat");
            if (!pm) { pm = new Material(Shader.Find("Twilight/PortalMask")); AssetDatabase.CreateAsset(pm, $"{TKArt.MatDir}/PortalMask.mat"); }
            mask.GetComponent<Renderer>().sharedMaterial = pm;
            return root;
        }

        static void SetLayerRecursive(GameObject g, int layer)
        {
            g.layer = layer;
            foreach (Transform c in g.transform) SetLayerRecursive(c.gameObject, layer);
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        /// <summary>虚拟木桌（Blender 程序建模）：顶面对齐真实桌面高度，琴摆在上面。识别到桌子后出现（玩法阶段做出现动画）。</summary>
        static GameObject VirtualTable()
        {
            var src = TKArt.Model("Assets/_Twilight/Art/Models/Table/Table.fbx");
            if (!src) return new GameObject("VirtualTable (缺模型)");
            var g = (GameObject)PrefabUtility.InstantiatePrefab(src);
            g.name = "VirtualTable";
            g.transform.position = new Vector3(0f, TableY, 0.62f);
            var wood = TKArt.Lit("Table_Oak", new Color(0.78f, 0.66f, 0.58f), 0.1f, albedo: $"{TKArt.TexDir}/painted_wood.png", tiling: new Vector2(1.6f, 1.6f));
            foreach (var r in g.GetComponentsInChildren<Renderer>()) r.sharedMaterial = wood;
            return g;
        }

        /// <summary>世界铺开：遮罩球（Portal 层，在已铺开的地方写模板，村庄画进去）+ 光边球。玩法按歌曲进度设 WorldReveal.reveal。</summary>
        static WorldReveal Reveal(Transform window)
        {
            var root = new GameObject("WorldReveal");
            var wr = root.AddComponent<WorldReveal>();
            wr.window = window;
            wr.fallbackEye = TKCapture.SeatEye;
            GameObject Ball(string name, string shader, float size, int layer)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.name = name; g.layer = layer;
                g.transform.SetParent(root.transform, false);
                g.transform.localScale = Vector3.one * size;
                string path = $"{TKArt.MatDir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
                var r = g.GetComponent<Renderer>(); r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                return g;
            }
            var mask = Ball("RevealMask", "Twilight/RevealMask", 6f, TKRenderSetup.PortalLayer);
            var rim = Ball("RevealRim", "Twilight/RevealRim", 16f, 0);
            wr.followers = new[] { mask.transform, rim.transform };
            wr.Apply();
            return wr;
        }

        /// <summary>窗户装饰（P2 模型，没完成就先不放）：两侧贴墙的百叶窗、窗台下挂的花箱。根节点 = 窗洞中心，房间在 -Z。</summary>
        static void WindowDecor(Transform window)
        {
            var shutter = TKArt.P2Prefab("window_shutter");
            if (shutter)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(shutter, window);
                    g.name = side < 0 ? "Shutter L" : "Shutter R";
                    var b = Bounds(g);
                    // 紧挨窗框外沿，贴墙（墙面在 z = 0），正面朝房间
                    g.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                    g.transform.localPosition = new Vector3(side * (OpenW / 2 + 0.085f + b.size.x / 2), -0.47f, -0.025f);
                    if (side > 0) g.transform.localScale = new Vector3(-1, 1, 1);      // 右边那扇镜像，合页都在靠窗一侧
                }
            }
            var box = TKArt.P2Prefab("flower_box");
            if (box)
            {
                var g = (GameObject)PrefabUtility.InstantiatePrefab(box, window);
                g.name = "Flower Box";
                var b = Bounds(g);
                g.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                // 挂在窗台下：顶沿比窗台底低 1 cm
                g.transform.localPosition = new Vector3(0, -OpenH / 2 - 0.1f - b.size.y, -b.size.z / 2 - 0.01f);
            }
        }

        static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>光河：挂在 Stage 下，从琴键前沿流到窗口。</summary>
        public static LightRiver River(Transform stage, Transform window)
        {
            var go = new GameObject("LightRiver");
            go.transform.SetParent(stage, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            string path = $"{TKArt.MatDir}/LightRiver.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Twilight/LightRiver")); AssetDatabase.CreateAsset(m, path); }
            mr.sharedMaterial = m;
            var r = go.AddComponent<LightRiver>();
            r.window = window;
            r.brass = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Piano_Brass.mat");
            r.haloMaterial = ShaderMat("RiverHalo", "Twilight/RiverHalo");
            r.motesMaterial = ShaderMat("RiverMotes", "Twilight/RiverMotes");
            r.tableMaterial = ShaderMat("TableCaustics", "Twilight/TableCaustics");
            r.skyMaterial = ShaderMat("SkyRiver", "Twilight/SkyRiver");
            r.Rebuild();
            return r;
        }

        /// <summary>萤火虫光点（叠加发光）。</summary>
        static Material FireflyMat()
        {
            var m = ShaderMat("Dots_Firefly", "Twilight/Dots");
            m.SetFloat("_Mode", 0); m.SetFloat("_Boost", 3.2f);
            m.SetFloat("_Src", (float)BlendMode.SrcAlpha); m.SetFloat("_Dst", (float)BlendMode.One);
            return m;
        }

        /// <summary>花瓣光点（正常混合）。</summary>
        static Material PetalDotsMat()
        {
            var m = ShaderMat("Dots_Petal", "Twilight/Dots");
            m.SetFloat("_Mode", 1); m.SetFloat("_Boost", 1.0f);
            m.SetFloat("_Src", (float)BlendMode.SrcAlpha); m.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha);
            m.renderQueue = 3034;
            return m;
        }

        static Material ShaderMat(string name, string shader)
        {
            string path = $"{TKArt.MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        /// <summary>钢琴采样文件名（原型 notes.js 的命名规则）：C#4 → sc1.mp3，A0 → A-2.mp3，C3 → c.mp3。</summary>
        static string SampleFile(int midi)
        {
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            string n = names[midi % 12]; int octave = midi / 12 - 1;
            string sharp = n.Length == 2 ? "s" : ""; string letter = n.Substring(0, 1);
            string b = octave <= 2 ? letter.ToUpperInvariant() + "-" + (2 - octave) : octave == 3 ? letter.ToLowerInvariant() : letter.ToLowerInvariant() + (octave - 3);
            return sharp + b + ".mp3";
        }

        /// <summary>玩法根节点：导演 + 音源 + 音块场 + 铺开导演；钢琴挂 PianoKeys。</summary>
        static GameObject GameRoot(GameObject piano, LightRiver river, WorldReveal reveal, Transform window, TwilightSky sky)
        {
            var keys = piano.AddComponent<PianoKeys>();
            keys.Bind();
            var root = new GameObject("Game");
            var audio = root.AddComponent<PianoAudio>();
            for (int m = 21; m <= 108; m++)
                audio.clips[m - 21] = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Twilight/Audio/Piano/" + SampleFile(m));
            var tf = root.AddComponent<TileField>();
            tf.river = river;
            tf.tilePrefab = TKArt.Model("Assets/_Twilight/Art/Models/Tile/Tile.fbx");
            tf.tileMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/GlassTile.mat");
            if (!tf.tileMaterial) { tf.tileMaterial = new Material(Shader.Find("Twilight/GlassTile")); AssetDatabase.CreateAsset(tf.tileMaterial, $"{TKArt.MatDir}/GlassTile.mat"); }
            tf.tileMaterial.enableInstancing = true;
            tf.holdMaterial = ShaderMat("GlassHold", "Twilight/GlassHold");
            tf.burstMaterial = ShaderMat("HitBurst", "Twilight/HitBurst");
            var rd = root.AddComponent<RevealDirector>();
            rd.reveal = reveal; rd.sky = sky;
            // 村子的环境音（风、湖水、蟋蟀、青蛙、远处猫头鹰；程序合成），跟着世界铺开慢慢变丰富
            var amb = new GameObject("Ambience");
            amb.transform.SetParent(root.transform, false);
            amb.AddComponent<AudioSource>();
            var ambience = amb.AddComponent<TKAmbience>();
            ambience.reveal = rd; ambience.sky = sky;
            var dis = new System.Collections.Generic.List<Renderer>();
            foreach (var r in window.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.layer != TKRenderSetup.PortalLayer && r.gameObject.layer != TKRenderSetup.VillageLayer) dis.Add(r);
            var wall = window.Find("Village/RuinWall");
            if (wall) dis.AddRange(wall.GetComponentsInChildren<Renderer>());
            rd.dissolve = dis.ToArray();
            rd.glass = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Window_Glass.mat");
            // 消散材质模板：场景里引用一个用 Twilight/LitDissolve 的材质，打包时着色器才会带进去（第十五轮：真机上消散一直没生效的根因）
            {
                string dp = $"{TKArt.MatDir}/LitDissolve_Template.mat";
                var dm = AssetDatabase.LoadAssetAtPath<Material>(dp);
                if (!dm) { dm = new Material(Shader.Find("Twilight/LitDissolve")); AssetDatabase.CreateAsset(dm, dp); }
                rd.dissolveTemplate = dm;
            }
            var gd = root.AddComponent<GameDirector>();
            gd.tiles = tf; gd.keys = keys; gd.pianoAudio = audio; gd.reveal = rd; gd.river = river;
            gd.riverMaterial = river.GetComponent<MeshRenderer>().sharedMaterial;
            gd.playOnStart = false;                  // 由 TKSession 开局（头显上先放琴）
            // 大师演奏：按大师谱放钢琴、琴键跟着动
            var mp = root.AddComponent<TKMasterPlayer>();
            mp.director = gd; mp.audio = audio; mp.keys = keys;
            gd.master = mp;
            // 打击感 / 节奏感：节奏伴奏 + 击中特效（判定字、火花、热度、里程碑）
            var bt = root.AddComponent<BeatTrack>();
            bt.director = gd;
            var fx = root.AddComponent<HitFX>();
            fx.director = gd; fx.river = river; fx.beatTrack = bt; fx.hitS = tf.hitS;
            fx.textMaterial = ShaderMat("GlowText", "Twilight/GlowText");          // 暖光字
            fx.sparkMaterial = ShaderMat("Spark", "Twilight/Spark");
            fx.flareMaterial = ShaderMat("Flare", "Twilight/Flare");
            fx.beamMaterial = ShaderMat("Beam", "Twilight/Beam");
            fx.petalMaterial = ShaderMat("Petal", "Twilight/Petal");
            var hud = root.AddComponent<ScoreHUD>();
            hud.director = gd; hud.river = river; hud.fx = fx; hud.beatTrack = bt; hud.hitS = tf.hitS; hud.glowMaterial = fx.textMaterial;
            hud.letterMaterial = ShaderMat("Letter", "Twilight/Letter");
            hud.plateMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_PanelIvory.mat");   // 提示瓷牌（和选曲面板同一套）
            // 第二十五轮：窗框顶上的歌名牌（运行时生成，不在消散列表里；第三十轮起窗户散完它自己跟着散）
            var title = window.gameObject.AddComponent<TKSongTitle>();
            title.director = gd; title.letterMaterial = hud.letterMaterial;
            title.plateMaterial = hud.plateMaterial; title.rimMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_PanelGold.mat");
            {   // 第三十轮：窗框散完歌名牌也散——消散版材质模板（引用住 Twilight/MusicBoxFade，打包才不会被裁掉）
                string fp = $"{TKArt.MatDir}/MusicBoxFade_Template.mat";
                var fm = AssetDatabase.LoadAssetAtPath<Material>(fp);
                if (!fm) { fm = new Material(Shader.Find("Twilight/MusicBoxFade")); AssetDatabase.CreateAsset(fm, fp); }
                title.fadeTemplate = fm;
            }
            hud.plateRimMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/Box_PanelGold.mat");
            hud.fireflyMaterial = FireflyMat();
            hud.petalMaterial = PetalDotsMat();
            hud.boardModel = TKArt.Model("Assets/_Twilight/Art/Models/Props/LeafBoard.fbx");   // 第十六轮（用户）：结算面板用回叶子木牌
            hud.boardMaterial = TKArt.Lit("LeafBoard", Color.white, 0.12f, 0f, albedo: "Assets/_Twilight/Art/Models/Props/LeafBoard.png");
            gd.fx = fx; gd.beatTrack = bt; gd.scoreHud = hud;
            return root;
        }

        /// <summary>XR 装置：追踪原点（地面）+ 头显相机（透明清屏、无后处理、声音监听）+ PXR_Manager + MR 启动 + 手部输入 + 白手套 + 会话流程。</summary>
        static void XRRig(Transform world, Transform stage, GameDirector director)
        {
            var origin = new GameObject("XR Origin").transform;
            var camGO = new GameObject("XR Camera");
            camGO.transform.SetParent(origin, false);
            camGO.transform.localPosition = TKCapture.SeatEye;
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.31f, 0.30f, 0f);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 2000f; cam.fieldOfView = 90f;
            var cd = camGO.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            cd.renderPostProcessing = false;
            camGO.AddComponent<AudioListener>();
            var tpd = camGO.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            tpd.positionInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("HeadPos", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("HeadRot", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            var pxr = origin.gameObject.AddComponent<ByteDance.PICO.XR.PXR_Manager>();
            pxr.eyeTracking = true;
            origin.gameObject.AddComponent<TKMR>().cam = cam;
            var hands = origin.gameObject.AddComponent<HandTapInput>();
            hands.trackingOrigin = origin; hands.director = director; hands.stage = stage;
            // 手部遮挡：真手不被八音盒 / 琴 / 选曲卡挡住（按关节画只写深度的胶囊，挖出透视里的手）
            var occ = origin.gameObject.AddComponent<TKHandOccluder>();
            occ.hands = hands; occ.material = ShaderMat("HandFeather", "Twilight/HandFeather");   // 第十九轮：软边擦淡，替代硬边深度挖洞
            // 不放虚拟手：透视里看到的就是自己的真手（手部关节由 HandTapInput 直接读 PICO 接口，不需要手部模型）
            // 封面 + 选歌：八音盒（掀盖、浮岛升起、◀ ▶ 开始 三个按钮）
            var box = TKMusicBoxBuilder.Build(camGO.transform, hands,
                ShaderMat("GlowText", "Twilight/GlowText"), ShaderMat("Letter", "Twilight/Letter"), FireflyMat());
            // 曲目面板：八音盒左边的木板（分类、曲目、我来弹 / 大师演奏）
            TKMusicBoxBuilder.SongPanel(box, hands, ShaderMat("GlowText", "Twilight/GlowText"));
            // 指尖光点 + 新手教学（认手 → 学敲 → 练习曲边玩边学）
            var marks = new GameObject("FingerMarks").AddComponent<TKFingerMarks>();
            marks.hands = hands; marks.stage = stage; marks.material = ShaderMat("FingerMark", "Twilight/FingerMark");
            var tut = new GameObject("Tutorial").AddComponent<TKTutorial>();
            tut.hands = hands; tut.director = director; tut.marks = marks;
            var session = new GameObject("Session").AddComponent<TKSession>();
            session.box = box; session.tutorial = tut;
            session.pauseButton = TKMusicBoxBuilder.PauseButton(stage, hands, ShaderMat("GlowText", "Twilight/GlowText"));
            session.world = world; session.stage = stage; session.head = camGO.transform; session.hands = hands; session.director = director;
            var songTitle = Object.FindAnyObjectByType<TKSongTitle>();   // 第二十九轮：看着歌名捏一下 → 窗边歌单
            if (songTitle) { songTitle.session = session; songTitle.hands = hands; songTitle.head = camGO.transform; }
            session.room = session.gameObject.AddComponent<TKRoomTable>(); session.room.trackingSpace = camGO.transform.parent;   // 系统房间扫描里的桌子
            session.authoredTableY = TableY;
        }

        static readonly Color[] LaneColors =
        {
            new(1.0f, 0.77f, 0.42f), new(1.0f, 0.56f, 0.37f), new(0.95f, 0.40f, 0.55f), new(0.48f, 0.72f, 1.0f),
        };

        /// <summary>编辑器预览用：在河上摆几块音块看效果（玩法阶段由谱面生成，这里标 EditorOnly 不进包）。</summary>
        static void PreviewTiles(LightRiver river)
        {
            var root = new GameObject("PreviewTiles (编辑器预览)") { tag = "EditorOnly" }.transform;
            root.SetParent(river.transform, false);
            var src = TKArt.Model("Assets/_Twilight/Art/Models/Tile/Tile.fbx");
            string path = $"{TKArt.MatDir}/GlassTile.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Twilight/GlassTile")); AssetDatabase.CreateAsset(m, path); }
            m.enableInstancing = true;
            var mpb = new MaterialPropertyBlock();
            // 一段旋律：(拍, 轨道)
            var notes = new (float beat, int lane)[] { (0.3f, 1), (1f, 2), (2f, 0), (3f, 3), (4f, 2), (5f, 1), (6f, 0), (7.5f, 2), (9f, 3), (10.5f, 1) };
            foreach (var (beat, lane) in notes)
            {
                float s = 0.04f + beat * 0.18f;
                if (s > river.Length - 0.1f) continue;
                var g = (GameObject)PrefabUtility.InstantiatePrefab(src, root);
                g.transform.position = river.Point(s, lane, 0.001f);
                g.transform.rotation = river.Rotation(s) * src.transform.localRotation;   // 保留 FBX 自带的轴向修正
                // 宽度跟着河宽（远处窄）
                g.transform.localScale = Vector3.one * (river.WidthAt(s) / river.widthNear);
                var r = g.GetComponentInChildren<Renderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                mpb.SetColor("_Color", LaneColors[lane]);
                mpb.SetFloat("_Hit", beat < 0.5f ? 0.6f : 0f);
                mpb.SetFloat("_Fade", Mathf.Clamp01((river.Length - s) / 0.35f));
                r.SetPropertyBlock(mpb);
            }
        }

        public static GameObject Piano(Transform stage)
        {
            var src = TKArt.Model("Assets/_Twilight/Art/Models/Piano/Piano.fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, stage);
            go.name = "Piano";
            go.transform.localPosition = Vector3.zero;
            // FBX 轴换算后琴是反的：转 180°，琴键前沿朝玩家（-Z）、C4 在左边
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            // 手绘风格：手绘木纹、哑光象牙白键、深棕黑键、画出来的金色描边（不再是写实的油亮材质）
            var walnut = TKArt.Lit("Piano_Walnut", new Color(0.86f, 0.72f, 0.62f), 0.14f, albedo: $"{TKArt.TexDir}/painted_wood.png", tiling: new Vector2(2.2f, 2.2f));
            var ivory = TKArt.Lit("Piano_Ivory", new Color(0.96f, 0.92f, 0.83f), 0.2f);
            var ebony = TKArt.Lit("Piano_Ebony", new Color(0.16f, 0.11f, 0.09f), 0.25f);
            var felt = TKArt.Lit("Piano_Felt", new Color(0.62f, 0.08f, 0.1f), 0.05f, albedo: $"{TKArt.TexDir}/felt_red.png", tiling: new Vector2(6f, 6f));
            var brass = TKArt.Lit("Piano_Brass", new Color(0.93f, 0.72f, 0.38f), 0.35f, 0.2f);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                string n = r.sharedMaterial ? r.sharedMaterial.name : "";
                r.sharedMaterial = n.StartsWith("Walnut") ? walnut : n.StartsWith("Ivory") ? ivory : n.StartsWith("Ebony") ? ebony
                    : n.StartsWith("Felt") ? felt : n.StartsWith("Brass") ? brass : walnut;
            }
            return go;
        }
    }
}
