using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// 玩家可视化实体 — 纯渲染，不管网络
    /// </summary>
    public class PlayerEntity : MonoBehaviour
    {
        public int PlayerId { get; private set; }

        private TextMesh _label;

        public static PlayerEntity Spawn(int playerId, Color color, string label)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"{label} (P{playerId})";
            go.transform.position = new Vector3(playerId * 2.5f, 1f, 0);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = color;

            // 头顶标签
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform);
            labelGo.transform.localPosition = new Vector3(0, 1.5f, 0);
            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = $"{label}\nP{playerId}";
            tm.characterSize = 0.12f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;

            var entity = go.AddComponent<PlayerEntity>();
            entity.PlayerId = playerId;
            entity._label = tm;
            return entity;
        }

        public void ApplyMove(Vector2 dir, float delta)
        {
            transform.position += new Vector3(dir.x, 0, dir.y) * delta;
        }

        public void UpdateLabel(string text)
        {
            if (_label != null)
                _label.text = text;
        }
    }
}
