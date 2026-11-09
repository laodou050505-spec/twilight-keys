using System.Collections.Generic;
using UnityEngine;

namespace Twilight
{
    /// <summary>运行时静态合批：开场时把这个根节点下不动的物体按材质合并（打包时的静态合批会把网格钉死在世界坐标里，
    /// 而放琴时整个世界要挪到真实桌子上，所以改成运行时合批——合并后根节点仍可以整体移动 / 旋转）。
    /// 排除：会动的（风车叶、萤火虫）、天空、湖面、要单独换材质的（石墙消散）。网格需要可读（导入设置 Read/Write）。</summary>
    public class RuntimeBatch : MonoBehaviour
    {
        // LampGlow 必须排除：它是按物体原点朝向相机的广告牌，合批后原点变成村庄根节点，
        // 三盏路灯的光晕会叠成一团挂在正前方地平线上（真机上看着像一个假太阳）
        public static readonly string[] Exclude = { "Sails", "Fireflies", "Sky", "Lake", "RuinWall", "LampGlow" };

        public static bool IsExcluded(Transform t, Transform root)
        {
            for (var p = t; p && p != root; p = p.parent)
                foreach (var n in Exclude) if (p.name.StartsWith(n)) return true;
            return false;
        }

        void Start()
        {
            if (!Application.isPlaying) return;
            var gos = new List<GameObject>();
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                if (IsExcluded(mf.transform, transform) || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                if (mf.GetComponent<MeshRenderer>()) gos.Add(mf.gameObject);
            }
            StaticBatchingUtility.Combine(gos.ToArray(), gameObject);
            Debug.Log($"[TK] 运行时合批 {gos.Count} 个物体");
        }
    }
}
