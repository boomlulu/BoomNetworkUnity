using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo01.1 HUD — 多客户端版，统一样式
    /// </summary>
    [RequireComponent(typeof(MultiClientPersonManager))]
    public class MultiClientGameHUD : MonoBehaviour
    {
        private MultiClientPersonManager _mgr;
        private float _fps;
        private int _frameCount;
        private float _fpsTimer;

        void Awake() => _mgr = GetComponent<MultiClientPersonManager>();

        void Update()
        {
            _frameCount++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f) { _fps = _frameCount / _fpsTimer; _frameCount = 0; _fpsTimer = 0; }
        }

        void OnGUI()
        {
            HUDStyles.Init();

            DrawStatusPanel();
            DrawControlsPanel();
        }

        void DrawStatusPanel()
        {
            float panelHeight = 60 + (_mgr != null ? _mgr.persons.Count * 20 : 0) + 80;
            GUILayout.BeginArea(new Rect(10, 10, 340, panelHeight), HUDStyles.Box);

            GUILayout.Label("BoomNetwork Demo01.1", HUDStyles.Title);
            GUILayout.Space(4);

            if (_mgr != null)
            {
                foreach (var slot in _mgr.persons)
                {
                    if (slot.person == null)
                    {
                        GUILayout.Label($"  {slot.inputMode}: <color=#666>Idle</color>", HUDStyles.Text);
                        continue;
                    }
                    var p = slot.person;
                    var stateColor = p.State switch
                    {
                        PersonState.Syncing => "#44ff44",
                        PersonState.Disconnected => "#ff4444",
                        PersonState.InRoom => "#44aaff",
                        PersonState.Connected => "#ffaa44",
                        _ => "#888888",
                    };
                    GUILayout.Label(
                        $"  {slot.inputMode}: <color={stateColor}>{p.State}</color>  P{p.PlayerId}  F{p.FrameNumber}",
                        HUDStyles.Text);
                }
            }

            GUILayout.Space(6);

            // Sync status
            bool inSync = _mgr?.syncStatus?.Contains("IN SYNC") ?? false;
            GUILayout.Label($"  {_mgr?.syncStatus ?? "Waiting..."}",
                inSync ? HUDStyles.StatusGood : HUDStyles.Text);

            // Hash
            GUILayout.Label($"  Hash: <color=#66ccff>{_mgr?.worldHash ?? "-"}</color>", HUDStyles.Hash);

            // Meta
            GUILayout.Label($"  Room: {_mgr?.targetRoomId ?? 0}   FPS: {_fps:F0}", HUDStyles.Tip);

            GUILayout.EndArea();
        }

        void DrawControlsPanel()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 270, 10, 260, 180), HUDStyles.Box);

            GUILayout.Label("Controls", HUDStyles.Title);
            GUILayout.Label("  WASD / Arrows = move", HUDStyles.Tip);
            GUILayout.Space(6);

            GUILayout.Label("Editor A (Host):", HUDStyles.Text);
            GUILayout.Label("  Connect All > Create Room", HUDStyles.Tip);
            GUILayout.Label("  > Join All > Start Game", HUDStyles.Tip);
            GUILayout.Space(4);

            GUILayout.Label("Editor B (Join):", HUDStyles.Text);
            GUILayout.Label("  Target Room ID > Connect All", HUDStyles.Tip);
            GUILayout.Label("  > Join All", HUDStyles.Tip);

            GUILayout.EndArea();
        }
    }
}
