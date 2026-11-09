using UnityEngine;

namespace Twilight
{
    /// <summary>绕本地轴匀速转（风车叶等）。编辑器里也转，方便看效果。</summary>
    [ExecuteAlways]
    public class Spin : MonoBehaviour
    {
        public Vector3 axis = Vector3.forward;
        [Tooltip("度 / 秒")] public float speed = 14f;

        void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : 1f / 60f;
            transform.Rotate(axis, speed * dt, Space.Self);
        }
    }
}
