using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo01 HUD — 单编辑器版，统一样式
    /// </summary>
    [RequireComponent(typeof(BasicPersonManager))]
    public class BasicGameHUD : MonoBehaviour
    {
        private BasicPersonManager _mgr;
        private float _fps;
        private int _frameCount;
        private float _fpsTimer;

        void Awake() => _mgr = GetComponent<BasicPersonManager>();

        void Update()
        {
            _frameCount++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f) { _fps = _frameCount / _fpsTimer; _frameCount = 0; _fpsTimer = 0; }
        }

        void OnGUI()
        {
            HUDStyles.Init();

            float panelHeight = 60 + (_mgr != null ? _mgr.persons.Count * 20 : 0) + 50;
            GUILayout.BeginArea(new Rect(10, 10, 340, panelHeight), HUDStyles.Box);

            GUILayout.Label("BoomNetwork Demo01", HUDStyles.Title);
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

            GUILayout.Space(4);
            bool inSync = _mgr?.syncStatus?.Contains("IN SYNC") ?? false;
            GUILayout.Label($"  {_mgr?.syncStatus ?? "Waiting..."}", inSync ? HUDStyles.StatusGood : HUDStyles.Text);
            GUILayout.Label($"  FPS: {_fps:F0}", HUDStyles.Tip);

            GUILayout.EndArea();

            // Controls
            GUILayout.BeginArea(new Rect(Screen.width - 240, 10, 230, 160), HUDStyles.Box);
            GUILayout.Label("Controls", HUDStyles.Title);
            GUILayout.Label("  WASD  = Player 1", HUDStyles.Tip);
            GUILayout.Label("  Arrows = Player 2", HUDStyles.Tip);
            GUILayout.Space(6);
            GUILayout.Label("Quick Start:", HUDStyles.Text);
            GUILayout.Label("  1. Start Go server", HUDStyles.Tip);
            GUILayout.Label("  2. Connect All", HUDStyles.Tip);
            GUILayout.Label("  3. Start Game", HUDStyles.Tip);
            GUILayout.EndArea();
        }
    }
}
