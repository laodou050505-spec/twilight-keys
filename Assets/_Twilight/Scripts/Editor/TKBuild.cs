using System;
using System.IO;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Twilight.EditorTools
{
    /// <summary>打包（菜单 Twilight/打包 APK（PICO））→ Builds/TwilightKeys_MMdd_HHmm.apk。经验（来自之前几个 PICO 项目）：
    /// - 切平台后必须先停：同一次调用里打包会漏掉 PICO 清单注入，头显里变成 2D 窗口（TKManifestGuard 兜底）；
    /// - PXR_ProjectSetting 要存成资产并打开手部 / 眼动 / 透视，否则真机读手部数据为空、清单没有权限；
    /// - 打包前清掉 Gradle 里的旧资源目录（残留的旧数据分片曾让引擎加载时卡死）；
    /// - Activity 入口、IL2CPP + ARM64、minSdk 29。</summary>
    public static class TKBuild
    {
        public const string Package = "com.toybox.twilightkeys";

        [MenuItem("Twilight/0 PICO 工程设置（手部 / 眼动 / 透视）")]
        public static string EnsurePico()
        {
            const string path = "Assets/Resources/PXR_ProjectSetting.asset";
            var c = AssetDatabase.LoadAssetAtPath<PXR_ProjectSetting>(path);
            if (!c)
            {
                c = ScriptableObject.CreateInstance<PXR_ProjectSetting>();
                c.handTrackingSupportType = HandTrackingSupport.ControllersAndHands;
                AssetDatabase.CreateAsset(c, path);
            }
            c.portalInited = true;
            c.handTracking = true;
            c.eyeTracking = true;
            c.videoSeeThrough = true;
            c.sceneCapture = true;          // 第十七轮：读系统房间扫描里的桌子（琴摆到真实桌面上）
            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            return $"hand={c.handTracking} eye={c.eyeTracking} vst={c.videoSeeThrough} support={c.handTrackingSupportType}";
        }

        /// <summary>应用图标：Art/Icon/icon_1024.png（Tripo 参照封面概念图生成：暮色里打开的八音盒、立柱托着小岛）。
        /// 默认图标 + 安卓各尺寸（传统 / 圆形 / 自适应）全部用同一张。</summary>
        public static string SetIcon()
        {
            const string p = "Assets/_Twilight/Art/Icon/icon_1024.png";
            if (AssetImporter.GetAtPath(p) is TextureImporter ti && (ti.textureType != TextureImporterType.Default || ti.npotScale != TextureImporterNPOTScale.None || ti.maxTextureSize < 1024 || ti.textureCompression != TextureImporterCompression.Uncompressed))
            { ti.textureType = TextureImporterType.Default; ti.npotScale = TextureImporterNPOTScale.None; ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = false; ti.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (!tex) return "找不到图标 " + p;
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { tex }, IconKind.Any);
            int n = 0;
            foreach (var kind in new[] { UnityEditor.Android.AndroidPlatformIconKind.Legacy, UnityEditor.Android.AndroidPlatformIconKind.Round, UnityEditor.Android.AndroidPlatformIconKind.Adaptive })
            {
                var icons = PlayerSettings.GetPlatformIcons(UnityEditor.Build.NamedBuildTarget.Android, kind);
                foreach (var ic in icons) { var arr = new Texture2D[ic.maxLayerCount]; for (int i = 0; i < arr.Length; i++) arr[i] = tex; ic.SetTextures(arr); n++; }
                PlayerSettings.SetPlatformIcons(UnityEditor.Build.NamedBuildTarget.Android, kind, icons);
            }
            AssetDatabase.SaveAssets();
            return $"图标已设置（安卓 {n} 个尺寸）";
        }

        /// <summary>启动画面（11-17 用户：去掉 Made with Unity，换成排版一样的 MADE WITH + TRIPO 标志）。
        /// 仍然用 Unity 自带的启动画面（位置、大小、淡入淡出都是同一套），只是关掉 Unity 标志、换成现成的那张图（Art/Brand/made_with_tripo.png，透明底）。
        /// 头显（XR）里 Unity 不画“标志列表”，只认 Virtual Reality Splash Image 这一张贴图，所以两处都要填（头显那张是不透明深色底、内容居中）。
        /// 写法照 MonsterCity 的 MonsterBuild.ApplySplash；每次打包都会调用。图不在就什么都不改。</summary>
        public static string ApplySplash()
        {
            const string path = "Assets/_Twilight/Art/Brand/made_with_tripo.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[TK] 没找到启动画面图 " + path + "，启动画面保持原样"); return "没找到 " + path; }
            if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sp) { Debug.LogWarning("[TK] 启动画面图导入失败 " + path); return "导入失败 " + path; }
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, sp) };
            const string vrPath = "Assets/_Twilight/Art/Brand/made_with_tripo_vr.png";
            var vi = AssetImporter.GetAtPath(vrPath) as TextureImporter;
            if (vi == null) { Debug.LogWarning("[TK] 没找到头显启动图 " + vrPath); return "没找到 " + vrPath; }
            if (vi.textureType != TextureImporterType.Default || vi.mipmapEnabled || vi.textureCompression != TextureImporterCompression.Uncompressed || vi.npotScale != TextureImporterNPOTScale.None)
            { vi.textureType = TextureImporterType.Default; vi.mipmapEnabled = false; vi.textureCompression = TextureImporterCompression.Uncompressed; vi.npotScale = TextureImporterNPOTScale.None; vi.wrapMode = TextureWrapMode.Clamp; vi.sRGBTexture = true; vi.SaveAndReimport(); }
            var vt = AssetDatabase.LoadAssetAtPath<Texture2D>(vrPath);
            var all = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (!vt || all == null || all.Length == 0) { Debug.LogWarning("[TK] 头显启动图没设上"); return "头显启动图没设上"; }
            var so = new SerializedObject(all[0]); var prop = so.FindProperty("m_VirtualRealitySplashScreen");
            if (prop == null) { Debug.LogWarning("[TK] 这个版本的 Unity 没有 m_VirtualRealitySplashScreen"); return "没有 m_VirtualRealitySplashScreen"; }
            if (prop.objectReferenceValue != vt) { prop.objectReferenceValue = vt; so.ApplyModifiedPropertiesWithoutUndo(); }
            AssetDatabase.SaveAssets();
            return $"启动画面：显示 {PlayerSettings.SplashScreen.show}，Unity 标志 {PlayerSettings.SplashScreen.showUnityLogo}，标志列表 {PlayerSettings.SplashScreen.logos.Length} 张（{sp.name}），头显启动图 {(prop.objectReferenceValue ? prop.objectReferenceValue.name : "空")}";
        }

        [MenuItem("Twilight/打包 APK（PICO）")]
        public static string BuildApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android)) return "切换 Android 平台失败";
                return "已切到 Android，等编译完成后再执行一次打包";
            }
            EnsurePico();
            SetIcon();
            ApplySplash();
            // 先从头重建场景并保存：保证打进去的是干净的场景（巡检截图时临时改过的东西不会带进包里），
            // 也避免打包时弹“场景已修改，要保存吗”的框把编辑器卡住
            TKSceneBuilder.Build();
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, Package);
            PlayerSettings.productName = "黄昏琴村";
            PlayerSettings.companyName = "toybox";
            PlayerSettings.bundleVersion = "0.1." + DateTime.Now.ToString("MMdd");
            PlayerSettings.Android.bundleVersionCode = int.Parse(DateTime.Now.ToString("MMddHH"));
            // 旧的 Gradle 资源目录（残留分片会让引擎加载时卡死）
            var gradleAssets = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/src/main/assets"));
            if (Directory.Exists(gradleAssets)) { Directory.Delete(gradleAssets, true); Debug.Log("[TK] 清掉旧的 Gradle 资源目录"); }
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Builds"));
            var apk = Path.GetFullPath(Path.Combine(Application.dataPath, $"../Builds/TwilightKeys_{DateTime.Now:MMdd_HHmm}.apk"));
            var rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { TKSceneBuilder.ScenePath },
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
            var s = rep.summary;
            string msg = $"{s.result} 用时 {s.totalTime.TotalMinutes:0.0} 分钟 大小 {s.totalSize / 1048576f:0.0} MB 错误 {s.totalErrors} → {apk}";
            File.AppendAllText("/tmp/tk_build.txt", DateTime.Now.ToString("HH:mm:ss ") + msg + "\n");
            Debug.Log("[TK] 打包：" + msg);
            return msg;
        }
    }

#if UNITY_ANDROID
    /// <summary>清单守卫：Gradle 工程生成后（PICO 清单注入之后）检查 VR 标记和手部 / 眼动权限，缺了就让打包失败（否则头显里是 2D 窗口）。</summary>
    internal class TKManifestGuard : UnityEditor.Android.IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 10000;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var m = File.ReadAllText(Path.Combine(path, "src/main/AndroidManifest.xml"));
            foreach (var need in new[] { "pvr.app.type", "com.picovr.permission.HAND_TRACKING", "com.picovr.permission.EYE_TRACKING" })
                if (!m.Contains(need))
                    throw new BuildFailedException($"[TK] AndroidManifest 缺少 {need}：PICO 清单注入没生效，装到头显会变成 2D 窗口。确认 XR 加载器（PICO）已启用、平台已是 Android 并编译完成后再打包。");
        }
    }
#endif
}
