using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Twilight.EditorTools
{
    /// <summary>性能巡检（菜单 Twilight/测试/性能巡检）：从座位眼睛朝 8 个水平方向，按头显视场（约 100°×100°）做视锥剔除 + LOD 选择，
    /// 统计实际要画的三角形和绘制次数（静态合批的按每种材质一次估算）。预览房间不算（头显里是真实房间）。</summary>
    public static class TKPerfAudit
    {
        [MenuItem("Twilight/测试/性能巡检")]
        public static string Run()
        {
            var eye = TKCapture.SeatEye;
            var go = new GameObject("__perf");
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 100f; cam.aspect = 1f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000f;
            var sb = new StringBuilder();
            long worstTri = 0; int worstDc = 0;
            try
            {
                var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
                for (int k = 0; k < 8; k++)
                {
                    cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(8f, k * 45f, 0));
                    var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                    long tris = 0; int dc = 0;
                    var staticMats = new HashSet<Material>();
                    var lodPick = new Dictionary<LODGroup, int>();
                    foreach (var r in renderers)
                    {
                        if (!r.enabled || r.transform.root.name.StartsWith("PreviewRoom") || r.transform.root.name.StartsWith("__")) continue;
                        if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                        var lg = r.GetComponentInParent<LODGroup>();
                        if (lg)
                        {
                            if (!lodPick.TryGetValue(lg, out int pick))
                            {
                                var lods = lg.GetLODs();
                                float dist = Vector3.Distance(eye, lg.transform.TransformPoint(lg.localReferencePoint));
                                float h = lg.size * lg.transform.lossyScale.y / (2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                                pick = -1;
                                for (int i = 0; i < lods.Length; i++) if (h >= lods[i].screenRelativeTransitionHeight) { pick = i; break; }
                                lodPick[lg] = pick;
                            }
                            if (pick < 0) continue;
                            bool inPick = System.Array.IndexOf(lg.GetLODs()[pick].renderers, r) >= 0;
                            if (!inPick) continue;
                        }
                        var mf = r.GetComponent<MeshFilter>();
                        if (!mf || !mf.sharedMesh) continue;
                        tris += mf.sharedMesh.triangles.Length / 3;
                        // 静态合批：打包后同材质合成一批（近似：每种材质一次）
                        var batch = r.GetComponentInParent<RuntimeBatch>();
                        if (batch && !RuntimeBatch.IsExcluded(r.transform, batch.transform))
                        { foreach (var m in r.sharedMaterials) if (m && staticMats.Add(m)) dc++; }
                        else dc += mf.sharedMesh.subMeshCount;
                    }
                    sb.AppendLine($"朝 {k * 45,3}°：{tris / 1000f,6:F0}k 三角，{dc} 次绘制");
                    if (tris > worstTri) worstTri = tris;
                    if (dc > worstDc) worstDc = dc;
                }
            }
            finally { Object.DestroyImmediate(go); }
            sb.Insert(0, $"最坏：{worstTri / 1000f:F0}k 三角，{worstDc} 次绘制（目标：≤ 400k、≤ 250）\n");
            Debug.Log(sb.ToString());
            return sb.ToString();
        }
    }
}
