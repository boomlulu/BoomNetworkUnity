using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// 共享 HUD 样式，所有 Demo 场景统一风格
    /// </summary>
    public static class HUDStyles
    {
        private static bool _inited;
        public static GUIStyle Box, Title, Text, Tip, Hash, StatusGood, StatusBad;

        private static Texture2D _bgTex;

        public static void Init()
        {
            if (_inited) return;
            _inited = true;

            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.1f, 0.85f));
            _bgTex.Apply();

            Box = new GUIStyle("box")
            {
                padding = new RectOffset(10, 10, 8, 8),
                normal = { background = _bgTex },
            };

            Title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                richText = true,
                normal = { textColor = new Color(0.9f, 0.9f, 0.95f) },
            };

            Text = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = true,
                normal = { textColor = new Color(0.8f, 0.8f, 0.85f) },
            };

            Tip = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                richText = true,
                normal = { textColor = new Color(0.6f, 0.6f, 0.65f) },
            };

            Hash = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                richText = true,
            };

            StatusGood = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 1f, 0.3f) },
            };

            StatusBad = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.4f, 0.3f) },
            };
        }
    }
}
