using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace BoomNetworkDemo.Editor
{
    public class ServerWindow : EditorWindow
    {
        [SerializeField] private string _serverPath = "/Users/boom/Demo/BoomNetwork/svr";
        [SerializeField] private string _configFile = "cmd/framesync/config.yaml";
        [SerializeField] private string _addr       = ":9000";
        [SerializeField] private string _proto      = "tcp";
        [SerializeField] private int    _ppr        = 2;
        [SerializeField] private string _adminAddr  = "http://127.0.0.1:9091";

        private bool   _lastCheckAlive;
        private double _nextCheckTime;
        private const double CHECK_INTERVAL = 2.0;

        // Health data
        private int    _rooms   = -1;
        private int    _players = -1;
        private string _uptime  = "";

        [MenuItem("BoomNetwork/Server Window")]
        public static void ShowWindow() => GetWindow<ServerWindow>("BoomNetwork Server");

        void OnEnable()  => EditorApplication.update += OnEditorUpdate;
        void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextCheckTime) return;
            _nextCheckTime = EditorApplication.timeSinceStartup + CHECK_INTERVAL;

            bool alive = FetchHealth();
            if (alive != _lastCheckAlive || alive)
            {
                _lastCheckAlive = alive;
                if (!alive) { _rooms = -1; _players = -1; _uptime = ""; }
                Repaint();
            }
        }

        void OnGUI()
        {
            // ===== Status =====
            EditorGUILayout.Space(5);
            var prevColor = GUI.contentColor;
            GUI.contentColor = _lastCheckAlive ? Color.green : Color.gray;
            EditorGUILayout.LabelField(
                $"Server: {(_lastCheckAlive ? "RUNNING" : "STOPPED")}",
                EditorStyles.boldLabel);
            GUI.contentColor = prevColor;

            if (_lastCheckAlive && _rooms >= 0)
            {
                EditorGUILayout.LabelField(
                    $"  Rooms: {_rooms}    Players: {_players}    Uptime: {_uptime}");
            }

            EditorGUILayout.Space(5);

            // ===== Config =====
            EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
            _serverPath = EditorGUILayout.TextField("Server Path", _serverPath);
            _configFile = EditorGUILayout.TextField("Config File", _configFile);
            _adminAddr  = EditorGUILayout.TextField("Admin URL",   _adminAddr);

            EditorGUILayout.BeginHorizontal();
            _addr  = EditorGUILayout.TextField("Address", _addr);
            _proto = EditorGUILayout.TextField("Proto",   _proto);
            _ppr   = EditorGUILayout.IntField("PPR",      _ppr);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // ===== Actions =====
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = _lastCheckAlive ? Color.gray : Color.green;
            GUI.enabled = !_lastCheckAlive;
            if (GUILayout.Button("Start Server", GUILayout.Height(32)))
                OpenTerminalWithServer();
            GUI.enabled = true;

            GUI.backgroundColor = _lastCheckAlive ? new Color(1f, 0.4f, 0.3f) : Color.gray;
            GUI.enabled = _lastCheckAlive;
            if (GUILayout.Button("Stop Server", GUILayout.Height(32)))
                KillPort();
            GUI.enabled = true;

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // ===== Quick Command =====
            EditorGUILayout.LabelField("Manual Command", EditorStyles.boldLabel);
            var cmd = BuildCommand();
            EditorGUILayout.SelectableLabel(cmd, EditorStyles.textField, GUILayout.Height(20));
            if (GUILayout.Button("Copy Command"))
            {
                GUIUtility.systemCopyBuffer = cmd;
                ShowNotification(new GUIContent("Copied!"));
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                $"Status checks every 2s via GET {_adminAddr}/health (no TCP game noise).",
                MessageType.Info);
        }

        // ===================== Health Check =====================

        /// <summary>
        /// HTTP GET /health — 不走 TCP 游戏协议，不产生服务器日志
        /// </summary>
        bool FetchHealth()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = System.TimeSpan.FromMilliseconds(500);
                var task = client.GetStringAsync($"{_adminAddr.TrimEnd('/')}/health");
                task.Wait(600);
                if (!task.IsCompletedSuccessfully) return false;

                var json = task.Result;
                _rooms   = ParseJsonInt(json, "rooms");
                _players = ParseJsonInt(json, "players");
                _uptime  = ParseJsonString(json, "uptime");
                return json.Contains("\"ok\"");
            }
            catch { return false; }
        }

        // ===================== Helpers =====================

        string BuildCommand() =>
            string.IsNullOrEmpty(_configFile)
                ? $"cd {_serverPath} && go run ./cmd/framesync/ -addr={_addr} -proto={_proto} -ppr={_ppr}"
                : $"cd {_serverPath} && go run ./cmd/framesync/ -config={_configFile}";

        void OpenTerminalWithServer()
        {
            var cmd    = BuildCommand();
            var script = $"tell application \"Terminal\" to do script \"{cmd}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName  = "osascript",
                Arguments = $"-e '{script}'",
                UseShellExecute  = false,
                CreateNoWindow   = true,
            });
            ShowNotification(new GUIContent("Server starting..."));
            _nextCheckTime = EditorApplication.timeSinceStartup + 3.0;
        }

        void KillPort()
        {
            // 从 _addr 提取端口号（":9000" → "9000"）
            var port = _addr.TrimStart(':');
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName  = "/bin/bash",
                    Arguments = $"-c \"lsof -ti:{port} -sTCP:LISTEN | xargs kill -9 2>/dev/null; echo done\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow         = true,
                };
                Process.Start(psi)?.WaitForExit(3000);
                _lastCheckAlive = false;
                _rooms = _players = -1;
                _uptime = "";
                Repaint();
                ShowNotification(new GUIContent("Server stopped"));
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Kill port failed: {e.Message}");
            }
        }

        static int ParseJsonInt(string json, string key)
        {
            var m = Regex.Match(json, $@"""{key}""\s*:\s*(\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : -1;
        }

        static string ParseJsonString(string json, string key)
        {
            var m = Regex.Match(json, $@"""{key}""\s*:\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}
