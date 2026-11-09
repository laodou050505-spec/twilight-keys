using UnityEngine;
using UnityEngine.Rendering;

namespace Twilight
{
    /// <summary>天色：progress 0 = 黄昏、1 = 余晖、2 = 夜（连续值，随歌曲进度推进）。
    /// 每帧设置天空全景的全局参数（天空盒 / 湖面 / 远山共用），并按天色调整主光、环境光和村里窗灯的亮度。
    /// 全景接缝跟着窗户转：窗户贴到哪面墙，村庄（和天空）的正前方就是窗外。</summary>
    [ExecuteAlways]
    public class TwilightSky : MonoBehaviour
    {
        [System.Serializable]
        public struct Preset
        {
            public Color zenith, horizon;
            public Color sunColor; public float sunIntensity, sunElevation, sunAzimuth;
            public Color ambSky, ambEquator, ambGround;
            public float windowGlow;
            [Tooltip("远景（远山带）色调：远山不受光，靠它跟着天色变暗")] public Color farTint;
        }

        [Range(0f, 2f)] public float progress;
        public Texture2DArray sky;
        public Light sun;
        [Tooltip("村庄朝向参照（一般是窗户）：全景的正前方 = 它的 +Z")]
        public Transform facing;
        [Tooltip("窗灯材质：自发光强度随天色变化（_EmissionColor = glowColor × 强度）")]
        public Material[] glowMaterials;
        public Color glowColor = new Color(1f, 0.72f, 0.38f);
        public Preset[] presets = new Preset[3];
        [Tooltip("月亮（相对窗户正前方）：仰角、方位（度，右为正）")] public float moonElevation = 26f, moonAzimuth = 24f;
        [Tooltip("雾：只影响远景（房间里的东西都在 3 m 内）")]
        public float fogStart = 90f, fogEnd = 900f;

        static readonly int IdSky = Shader.PropertyToID("_TK_Sky"), IdRows = Shader.PropertyToID("_TK_SkyRows"),
            IdZenA = Shader.PropertyToID("_TK_ZenA"), IdZenB = Shader.PropertyToID("_TK_ZenB"),
            IdHorA = Shader.PropertyToID("_TK_HorA"), IdHorB = Shader.PropertyToID("_TK_HorB"),
            IdEmission = Shader.PropertyToID("_EmissionColor"), IdNight = Shader.PropertyToID("_TK_Night"), IdFar = Shader.PropertyToID("_TK_FarTint"),
            IdMoon = Shader.PropertyToID("_TK_Moon");

        float lastP = -1f, lastYaw = float.NaN;

        void OnEnable() { lastP = -1f; Apply(); }
        void Update()
        {
            // 天色和朝向都没变：全局参数、环境光、雾都不用重设（每帧设环境球谐有开销）
            float yaw = facing ? facing.eulerAngles.y : 0f;
            if (Application.isPlaying && Mathf.Abs(progress - lastP) < 1e-4f && Mathf.Abs(yaw - lastYaw) < 1e-3f) return;
            Apply();
        }
        void OnValidate() { lastP = -1f; Apply(); }

        public void Apply()
        {
            if (presets == null || presets.Length < 3) return;
            lastP = progress; lastYaw = facing ? facing.eulerAngles.y : 0f;
            float p = Mathf.Clamp(progress, 0f, 2f);
            int a = Mathf.Min((int)p, 1);
            float m = p - a;
            Preset A = presets[a], B = presets[a + 1];
            if (sky) Shader.SetGlobalTexture(IdSky, sky);
            float yaw = facing ? facing.eulerAngles.y : 0f;
            // 着色器里方位角按世界坐标算；窗户转了 yaw 度，全景也跟着转
            Shader.SetGlobalVector(IdRows, new Vector4(a, a + 1, m, -yaw / 360f));
            Shader.SetGlobalFloat(IdNight, Mathf.Clamp01(p - 1f));
            // 月亮：窗外右前上方（仰角 26°、偏右 24°：世界铺开后正好在视野里，湖面也映得到），入夜后慢慢亮起来
            var moonDir = Quaternion.Euler(-moonElevation, moonAzimuth + yaw, 0f) * Vector3.forward;
            Shader.SetGlobalVector(IdMoon, new Vector4(moonDir.x, moonDir.y, moonDir.z, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p - 1.25f) / 0.6f))));
            Shader.SetGlobalColor(IdFar, Color.Lerp(A.farTint, B.farTint, m));
            Shader.SetGlobalColor(IdZenA, A.zenith); Shader.SetGlobalColor(IdZenB, B.zenith);
            Shader.SetGlobalColor(IdHorA, A.horizon); Shader.SetGlobalColor(IdHorB, B.horizon);
            if (sun)
            {
                sun.color = Color.Lerp(A.sunColor, B.sunColor, m);
                sun.intensity = Mathf.Lerp(A.sunIntensity, B.sunIntensity, m);
                float el = Mathf.Lerp(A.sunElevation, B.sunElevation, m), az = Mathf.LerpAngle(A.sunAzimuth, B.sunAzimuth, m);
                sun.transform.rotation = Quaternion.Euler(el, az + yaw, 0f);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(A.ambSky, B.ambSky, m);
            RenderSettings.ambientEquatorColor = Color.Lerp(A.ambEquator, B.ambEquator, m);
            RenderSettings.ambientGroundColor = Color.Lerp(A.ambGround, B.ambGround, m);
            // 运行时改三色不会更新环境球谐：自己按“上 / 中 / 下”三色拼一个
            var sh = new SphericalHarmonicsL2();
            Color up = RenderSettings.ambientSkyColor.linear, eq = RenderSettings.ambientEquatorColor.linear, dn = RenderSettings.ambientGroundColor.linear;
            sh.AddAmbientLight(eq);
            sh.AddDirectionalLight(Vector3.up, (up - eq) * 1.6f, 1f);
            sh.AddDirectionalLight(Vector3.down, (dn - eq) * 1.6f, 1f);
            RenderSettings.ambientProbe = sh;
            // 远处雾色 = 地平线色（偏暗一点）
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.fogColor = Color.Lerp(A.horizon, B.horizon, m) * 0.85f;
            float glow = Mathf.Lerp(A.windowGlow, B.windowGlow, m);
            if (glowMaterials != null)
                foreach (var mat in glowMaterials)
                    if (mat)
                    {
                        mat.EnableKeyword("_EMISSION");          // URP 的材质检查会按条件关掉它，这里强制打开
                        mat.SetColor(IdEmission, glowColor * glow);
                    }
        }

        /// <summary>默认三套天色（和天空图配好的）。</summary>
        public static Preset[] DefaultPresets() => new[]
        {
            new Preset { zenith = new Color(0.598f, 0.691f, 0.771f), horizon = new Color(0.988f, 0.729f, 0.526f),
                sunColor = new Color(1f, 0.78f, 0.56f), sunIntensity = 1.35f, sunElevation = 16f, sunAzimuth = 38f,
                ambSky = new Color(0.62f, 0.62f, 0.70f), ambEquator = new Color(0.62f, 0.50f, 0.44f), ambGround = new Color(0.22f, 0.20f, 0.18f), windowGlow = 0.6f, farTint = new Color(1f, 1f, 1f) },
            new Preset { zenith = new Color(0.248f, 0.295f, 0.482f), horizon = new Color(0.836f, 0.475f, 0.553f),
                sunColor = new Color(0.98f, 0.56f, 0.55f), sunIntensity = 0.7f, sunElevation = 7f, sunAzimuth = 48f,
                ambSky = new Color(0.36f, 0.38f, 0.58f), ambEquator = new Color(0.50f, 0.38f, 0.46f), ambGround = new Color(0.16f, 0.14f, 0.18f), windowGlow = 2.0f, farTint = new Color(0.82f, 0.72f, 0.86f) },
            new Preset { zenith = new Color(0.068f, 0.081f, 0.214f), horizon = new Color(0.725f, 0.621f, 0.869f),
                sunColor = new Color(0.58f, 0.66f, 1f), sunIntensity = 0.38f, sunElevation = 38f, sunAzimuth = 20f,
                ambSky = new Color(0.16f, 0.19f, 0.34f), ambEquator = new Color(0.20f, 0.20f, 0.32f), ambGround = new Color(0.07f, 0.07f, 0.10f), windowGlow = 6f, farTint = new Color(0.36f, 0.40f, 0.62f) },
        };
    }
}
