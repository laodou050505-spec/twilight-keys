using UnityEngine;

namespace Twilight
{
    /// <summary>手部遮挡（MR 里真手不被虚拟物体挡住）：每帧按 HandTapInput 读到的关节，
    /// 用胶囊拼出手的大致形状——四根手指（指根 → 指尖）、拇指（手腕附近 → 拇指尖）、手掌（手腕 → 中指根、食指根 → 小指根），
    /// 用只写深度的材质（Twilight/HandDepthMask）在所有虚拟物体之前画。手后面的虚拟东西在手的位置不画，露出透视里的真手。
    /// 胶囊比真手略粗一点（追踪有几毫米误差，宁可多挖一点也别让盒子边压在手指上）。</summary>
    public class TKHandOccluder : MonoBehaviour
    {
        public HandTapInput hands;
        public Material material;
        // 第十九轮（用户：手的剪影太难看）：胶囊收细到接近真手（原来手掌那根 2.8 cm 半径 = 5.6 cm 厚的一团），
        // 材质换成 Twilight/HandFeather（最后画、软边擦淡），不再是只写深度的硬边挖洞
        [Tooltip("手指胶囊半径（米）")] public float fingerRadius = 0.0088f;
        [Tooltip("手掌胶囊半径（米）")] public float palmRadius = 0.0135f;

        Mesh capsule;

        void Awake()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule = g.GetComponent<MeshFilter>().sharedMesh;
            Destroy(g);
        }

        void LateUpdate()
        {
            if (!hands || !material || !capsule) return;
            Draw(hands.Left);
            Draw(hands.Right);
        }

        void Draw(HandJoints j)
        {
            if (j == null) return;
            Seg(j.indexProx, j.indexTip, fingerRadius);
            Seg(j.middleProx, j.middleTip, fingerRadius);
            Seg(j.ringProx, j.ringTip, fingerRadius * 0.93f);
            Seg(j.pinkyProx, j.pinkyTip, fingerRadius * 0.8f);
            var thumbBase = Vector3.Lerp(j.wrist, j.indexProx, 0.3f);
            Seg(thumbBase, j.thumbTip, fingerRadius * 1.05f);
            // 手掌：从手腕扇形连到四个指根（薄薄一片，不是一个粗胶囊），再加指根一道、掌心一道横向
            var w0 = Vector3.Lerp(j.wrist, j.middleProx, 0.12f);
            Seg(w0, j.indexProx, palmRadius);
            Seg(w0, j.middleProx, palmRadius);
            Seg(w0, j.ringProx, palmRadius);
            Seg(w0, j.pinkyProx, palmRadius * 0.9f);
            Seg(j.indexProx, j.pinkyProx, palmRadius * 0.85f);
            Seg(Vector3.Lerp(w0, j.indexProx, 0.5f), Vector3.Lerp(w0, j.pinkyProx, 0.5f), palmRadius);
        }

        /// <summary>a → b 一根胶囊（Unity 胶囊：高 2、半径 0.5，沿 Y）。</summary>
        void Seg(Vector3 a, Vector3 b, float r)
        {
            var d = b - a; float len = d.magnitude;
            if (len < 1e-4f) return;
            var rot = Quaternion.FromToRotation(Vector3.up, d / len);
            var m = Matrix4x4.TRS((a + b) * 0.5f, rot, new Vector3(r * 2f, (len + 2f * r) * 0.5f, r * 2f));
            Graphics.DrawMesh(capsule, m, material, 0, null, 0, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }
}
