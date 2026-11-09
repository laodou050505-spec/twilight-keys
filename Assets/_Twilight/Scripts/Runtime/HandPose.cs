using UnityEngine;

namespace Twilight
{
    /// <summary>一只手的关键关节（世界坐标）。</summary>
    public class HandJoints
    {
        public Vector3 wrist, thumbTip, indexTip, middleTip, ringTip, pinkyTip, indexProx, middleProx, ringProx, pinkyProx;
        public bool right;
    }

    /// <summary>手部姿态（移植自原型 handpose.js）：放平手掌定桌面、掌心朝上。
    /// 注意：Unity 是左手系，掌心法线的叉乘和原型（three.js 右手系）相反——
    /// 右手 Cross(b, a)、左手 Cross(a, b) 时，手掌平放朝下得到 y &lt; 0。</summary>
    public static class HandPose
    {
        public static Vector3 PalmNormal(HandJoints j)
        {
            var a = j.middleProx - j.wrist;
            var b = j.pinkyProx - j.indexProx;
            return (j.right ? Vector3.Cross(b, a) : Vector3.Cross(a, b)).normalized;
        }

        public static bool FingersExtended(HandJoints j)
        {
            int n = 0;
            if (Vector3.Distance(j.indexTip, j.wrist) > 1.45f * Vector3.Distance(j.indexProx, j.wrist)) n++;
            if (Vector3.Distance(j.middleTip, j.wrist) > 1.45f * Vector3.Distance(j.middleProx, j.wrist)) n++;
            if (Vector3.Distance(j.ringTip, j.wrist) > 1.45f * Vector3.Distance(j.ringProx, j.wrist)) n++;
            if (Vector3.Distance(j.pinkyTip, j.wrist) > 1.45f * Vector3.Distance(j.pinkyProx, j.wrist)) n++;
            return n >= 3;
        }
    }

    /// <summary>放平手掌定位：手掌朝下、手指伸平、静止 hold 秒 → 接触高度、位置、手指朝向（水平）。</summary>
    public class PalmPlacer
    {
        public float normalY = -0.8f, spread = 0.018f, still = 0.02f, hold = 1f, grace = 0.15f;
        public struct Result { public float contactY; public Vector3 center; public Vector3 fwd; }
        float k, bad; Vector3? last; int n; Vector3 acc; float accY;

        public void Reset() { k = 0; bad = 0; last = null; n = 0; acc = Vector3.zero; accY = 0; }
        public float Progress => Mathf.Clamp01(k);

        /// <summary>返回 true 表示完成，result 有效。</summary>
        public bool Update(float dt, HandJoints j, out Result result)
        {
            result = default;
            if (j == null) { Reset(); return false; }
            float y0 = j.indexTip.y, y1 = j.middleTip.y, y2 = j.ringTip.y, y3 = j.pinkyTip.y;
            float meanY = (y0 + y1 + y2 + y3) / 4f;
            var center = (j.indexTip + j.middleTip + j.ringTip + j.pinkyTip) / 4f;
            float speed = last.HasValue ? Vector3.Distance(center, last.Value) / Mathf.Max(dt, 1e-3f) : 0f;
            last = center;
            float sp = Mathf.Max(Mathf.Max(y0, y1), Mathf.Max(y2, y3)) - Mathf.Min(Mathf.Min(y0, y1), Mathf.Min(y2, y3));
            bool ok = HandPose.PalmNormal(j).y < normalY && HandPose.FingersExtended(j) && sp < spread && speed < still;
            if (ok)
            {
                bad = 0; k += dt / hold;
                n++; acc += center; accY += meanY;
            }
            else
            {
                bad += dt;
                if (bad > grace) { k = 0; n = 0; acc = Vector3.zero; accY = 0; }
            }
            if (k >= 1f && n > 0)
            {
                var f = j.middleTip - j.wrist; f.y = 0;
                result = new Result { contactY = accY / n, center = acc / n, fwd = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward };
                Reset();
                return true;
            }
            return false;
        }
    }

    /// <summary>掌心朝上保持 hold 秒 → 触发一次；手翻回去（法线 y &lt; reset）才能再触发。</summary>
    public class PalmUp
    {
        public float upY = 0.7f, hold = 0.25f, resetY = 0.3f;
        float k; bool fired;
        public float Progress => Mathf.Clamp01(k);

        public bool Update(float dt, HandJoints j)
        {
            if (j == null) { k = 0; fired = false; return false; }
            float ny = HandPose.PalmNormal(j).y;
            if (ny < resetY) { k = 0; fired = false; }
            if (!fired && ny > upY && HandPose.FingersExtended(j))
            {
                k += dt / hold;
                if (k >= 1f) { fired = true; k = 0; return true; }
            }
            else if (!fired) k = Mathf.Max(0, k - dt * 2f);
            return false;
        }
    }
}
