using UnityEngine;

namespace Twilight
{
    /// <summary>世界铺开：从窗户方向往外，把窗中村一点点铺满整个视野（玩法里按歌曲进度设 reveal）。
    /// 设置全局 _TK_Reveal / _TK_RevealDir；遮罩球（Portal 层，写模板）和光边球跟着头走。</summary>
    [ExecuteAlways]
    public class WorldReveal : MonoBehaviour
    {
        [Range(0f, 1f)] public float reveal;
        public Transform window;
        [Tooltip("眼睛位置；空着就用主相机（编辑器里用座位默认位置）")]
        public Transform head;
        public Vector3 fallbackEye = new Vector3(0f, 1.18f, -0.12f);
        [Tooltip("跟着眼睛走的两个球（遮罩、光边）")]
        public Transform[] followers;

        static readonly int IdReveal = Shader.PropertyToID("_TK_Reveal"), IdDir = Shader.PropertyToID("_TK_RevealDir");

        void OnEnable() => Apply();
        void Update() => Apply();
        void OnValidate() => Apply();

        public Vector3 Eye
        {
            get
            {
                if (head) return head.position;
                if (Application.isPlaying && Camera.main) return Camera.main.transform.position;
                return fallbackEye;
            }
        }

        public void Apply()
        {
            var eye = Eye;
            Shader.SetGlobalFloat(IdReveal, reveal);
            var dir = window ? (window.position - eye).normalized : Vector3.forward;
            Shader.SetGlobalVector(IdDir, dir);
            if (followers != null)
                foreach (var f in followers)
                    if (f) { f.position = eye; f.gameObject.SetActive(reveal > 0.001f); }
        }
    }
}
