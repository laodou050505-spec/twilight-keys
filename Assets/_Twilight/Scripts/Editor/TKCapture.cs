using UnityEngine;

namespace Twilight.EditorTools
{
    /// <summary>编辑器截图：临时相机渲染到 PNG。默认机位 = 坐在桌前的玩家（眼睛比桌面高约 45 cm、离琴键约 45 cm）。
    /// 坐标约定：玩家坐在原点附近、面朝 +Z；桌面高 0.74 m，琴键前沿在 z = 0.30。</summary>
    public static class TKCapture
    {
        public static readonly Vector3 SeatEye = new Vector3(0f, 1.18f, -0.12f);

        public static string Shot(string path, Vector3 pos, Vector3 lookAt, float fov = 60f, int w = 1600, int h = 1000, Color? bg = null, float far = 60f)
        {
            var go = new GameObject("__tkcap");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.position = pos;
                cam.transform.rotation = Quaternion.LookRotation(lookAt - pos);
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = far;
                cam.clearFlags = bg.HasValue ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
                cam.backgroundColor = bg ?? Color.black;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                return path;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>近看村庄（调试用）：村庄只画在窗洞模板里，这里临时在相机前放一块铺满视野的窗洞遮罩，任何机位都能看到村里的东西。</summary>
        public static string ShotVillage(string path, Vector3 pos, Vector3 lookAt, float fov = 40f, int w = 1600, int h = 1000, float far = 2000f)
        {
            var mask = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                Object.DestroyImmediate(mask.GetComponent<Collider>());
                mask.layer = TKRenderSetup.PortalLayer;
                mask.GetComponent<Renderer>().sharedMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>($"{TKArt.MatDir}/PortalMask.mat");
                var rot = Quaternion.LookRotation(lookAt - pos);
                mask.transform.position = pos + rot * Vector3.forward * 0.05f;
                mask.transform.rotation = rot;
                mask.transform.localScale = Vector3.one * 0.5f;
                return Shot(path, pos, lookAt, fov, w, h, null, far);
            }
            finally { Object.DestroyImmediate(mask); }
        }

        /// <summary>玩家视角：坐着看琴（略低头）。</summary>
        public static string Seat(string path, float fov = 70f) => Shot(path, SeatEye, new Vector3(0f, 0.80f, 0.42f), fov);
    }
}
