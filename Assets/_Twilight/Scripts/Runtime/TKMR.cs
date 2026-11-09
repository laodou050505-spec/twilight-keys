using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
#if ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace Twilight
{
    /// <summary>MR 启动（只在头显上生效）：
    /// - 开 PICO 视频透视，相机清屏为全透明（真实房间就是背景）；从后台恢复时 PICO 会关掉透视，要重新开；
    /// - 追踪原点设为地面；
    /// - 性能：刷新率固定 72Hz（稳定比 90/45 跳动舒服）、眼动注视点渲染（不支持时退回固定注视点）、关后处理；
    /// - 保险：场景里没有 AudioListener 就补一个（没有它整个游戏没声音）。</summary>
    public class TKMR : MonoBehaviour
    {
        public Camera cam;
        public float refreshRate = 72f, eyeScale = 1f;
        bool floorSet; float retry;

        void Start()
        {
            if (!cam) cam = Camera.main;
            if (cam && !Object.FindAnyObjectByType<AudioListener>()) cam.gameObject.AddComponent<AudioListener>();
            if (cam) { var cd = cam.GetComponent<UniversalAdditionalCameraData>(); if (cd) cd.renderPostProcessing = false; }
            Apply();
#if UNITY_ANDROID && !UNITY_EDITOR && ENABLE_PICO_XR_SDK
            XRSettings.eyeTextureResolutionScale = eyeScale;
            int rr = PXR_Plugin.System.UPxr_SetSystemDisplayFrequency(refreshRate);
            bool etfr = PXR_FoveationRendering.SetFoveationLevel(FoveationLevel.High, true);
            if (!etfr) PXR_FoveationRendering.SetFoveationLevel(FoveationLevel.Med, false);
            Debug.Log($"[TK] refresh={refreshRate}(r={rr}) foveation={(etfr ? "眼动" : "固定")}");
#endif
        }

        void OnApplicationPause(bool paused) { if (!paused) Apply(); }

        void Apply()
        {
            if (Application.isEditor || !XRSettings.isDeviceActive) return;
#if ENABLE_PICO_XR_SDK
            PXR_Manager.EnableVideoSeeThrough = true;
#endif
            if (cam) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0); }
        }

        void Update()
        {
            if (floorSet || !XRSettings.isDeviceActive) return;
            retry -= Time.unscaledDeltaTime;
            if (retry > 0) return;
            retry = 1f;
            var subs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs)
                if ((s.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                    floorSet |= s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
        }
    }
}
