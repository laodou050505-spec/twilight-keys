using UnityEngine;

namespace Twilight
{
    /// <summary>游戏中的暂停键：琴左上方浮着的一颗小圆钮（深色胡桃木面 + 拉丝铜圈 + 两道竖条图标），正对着人。
    /// 指尖戳一下 = 暂停（八音盒弹出来：继续 / 换歌 / 回主页）。和八音盒按钮同样的规则：手指靠近先微微弹出来，
    /// 戳过一次要先退开才能再戳，另有冷却（演奏中手在琴上动，不会误触：它在琴键左边、比键面高 11 cm）。
    /// 只在演奏中显示（暂停、结算、菜单时藏起来）。编辑器：P 键。</summary>
    public class TKPauseButton : MonoBehaviour
    {
        public HandTapInput hands;
        public Transform face;              // 按下去会动的那一块
        public TextMesh label;
        public float radius = 0.022f;

        /// <summary>这一帧被按了（TKSession 读完清掉）。</summary>
        public bool Pressed { get; set; }
        /// <summary>要不要显示（TKSession 每帧设）。</summary>
        public bool Shown { get; set; }
        /// <summary>轻轻一涨一缩地招手（教学提示“按暂停调高度”时）。</summary>
        public bool Beckon { get; set; }

        float show, hover, press, cool;
        bool armed = true;
        Vector3 faceRest;

        void Awake() { if (face) faceRest = face.localPosition; transform.localScale = Vector3.one * 0.001f; }

        void Update()
        {
            float dt = Time.deltaTime;
            show = Mathf.MoveTowards(show, Shown ? 1f : 0f, dt * 3f);
            float e = show * show * (3 - 2 * show);
            transform.localScale = Vector3.one * Mathf.Max(0.001f, e) * (Beckon ? 1f + 0.12f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4f)) : 1f);
            if (show < 0.5f) { armed = false; return; }
            TKFont.Sync();

            // 指尖戳（按钮正面朝 -Z = 朝人）
            cool -= dt;
            bool inside = false, far = true, near = false;
            foreach (var j in new[] { hands ? hands.Left : null, hands ? hands.Right : null })
            {
                if (j == null) continue;
                var p = transform.InverseTransformPoint(j.indexTip);
                float along = -p.z, r = new Vector2(p.x, p.y).magnitude;
                bool over = r < radius + 0.012f;
                if (over && along < 0.008f && along > -0.03f) inside = true;
                if (over && along < 0.06f && along > -0.03f) near = true;
                if (!(along > 0.03f || r > radius + 0.03f)) far = false;
            }
            hover = Mathf.MoveTowards(hover, near ? 1f : 0f, dt * 6f);
            if (far) armed = true;
            if (inside && armed && cool <= 0f) Press();
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.pKey.wasPressedThisFrame) Press();

            press = Mathf.MoveTowards(press, 0f, dt * 5f);
            float pr = Mathf.Sin(press * Mathf.PI * 0.5f);
            if (face) face.localPosition = faceRest + Vector3.back * (0.003f * hover * (1 - pr)) + Vector3.forward * 0.005f * pr;
            if (label) TKFont.Alpha(label, new Color(1f, 0.92f, 0.78f), e * (0.55f + 0.45f * hover));
            if (label) label.text = TKLang.Pick("暂停", "Pause");
        }

        void Press() { armed = false; cool = 0.5f; press = 1f; Pressed = true; }
    }
}
