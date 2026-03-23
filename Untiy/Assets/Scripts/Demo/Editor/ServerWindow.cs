using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace BoomNetworkDemo.Editor
{
    public class ServerWindow : EditorWindow
    {
        [SerializeField] private string _serverPath = "/Users/boom/Demo/BoomNetwork/svr";
        [SerializeField] private string _configFile = "cmd/framesync/config.yaml";
        [SerializeField] private string _addr = ":9000";
        [SerializeField] private string _proto = "tcp";
        [SerializeField] private int _ppr = 2;

        private bool _lastCheckAlive;
        private double _nextCheckTime;
        private const double CHECK_INTERVAL = 2.0;

        // Metrics
        private int _connections = -1;
        private int _rooms = -1;

        [MenuItem("BoomNetwork/Server Window")]
        public static void ShowWindow()
        {
            GetWindow<ServerWindow>("BoomNetwork Server");
        }

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup > _nextCheckTime)
            {
                _nextCheckTime = EditorApplication.timeSinceStartup + CHECK_INTERVAL;
                bool alive = IsServerAlive();
                if (alive != _lastCheckAlive || alive)
                {
                    _lastCheckAlive = alive;
                    if (alive) FetchMetrics();
                    else { _connections = -1; _rooms = -1; }
                    Repaint();
                }
            }
        }

        void OnGUI()
        {
            // ===== Status =====
            EditorGUILayout.Space(5);
            var statusColor = _lastCheckAlive ? Color.green : Color.gray;
            var statusText = _lastCheckAlive ? "RUNNING" : "STOPPED";
            var prevColor = GUI.contentColor;
            GUI.contentColor = statusColor;
            EditorGUILayout.LabelField($"Server Status: {statusText}", EditorStyles.boldLabel);
            GUI.contentColor = prevColor;

            if (_lastCheckAlive && _connections >= 0)
            {
                EditorGUILayout.LabelField($"  Connections: {_connections}    Rooms: {_rooms}");
            }

            EditorGUILayout.Space(5);

            // ===== Config =====
            EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
            _serverPath = EditorGUILayout.TextField("Server Path", _serverPath);
            _configFile = EditorGUILayout.TextField("Config File", _configFile);

            EditorGUILayout.BeginHorizontal();
            _addr = EditorGUILayout.TextField("Address", _addr);
            _proto = EditorGUILayout.TextField("Proto", _proto);
            _ppr = EditorGUILayout.IntField("PPR", _ppr);
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
                "Status auto-checks every 2s (TCP probe + Prometheus metrics).\n" +
                "Metrics endpoint: http://127.0.0.1:9090/metrics",
                MessageType.Info);
        }

        string BuildCommand()
        {
            return string.IsNullOrEmpty(_configFile)
                ? $"cd {_serverPath} && go run ./cmd/framesync/ -addr={_addr} -proto={_proto} -ppr={_ppr}"
                : $"cd {_serverPath} && go run ./cmd/framesync/ -config={_configFile}";
        }

        bool IsServerAlive()
        {
            try
            {
                using var tcp = new TcpClient();
                var result = tcp.BeginConnect("127.0.0.1", 9000, null, null);
                bool connected = result.AsyncWaitHandle.WaitOne(200);
                if (connected && tcp.Connected)
                {
                    tcp.EndConnect(result);
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        void FetchMetrics()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = System.TimeSpan.FromMilliseconds(500);
                var task = client.GetStringAsync("http://127.0.0.1:9090/metrics");
                task.Wait(500);
                if (task.IsCompletedSuccessfully)
                {
                    var text = task.Result;
                    _connections = ParseMetric(text, "boom_connections_current");
                    _rooms = ParseMetric(text, "boom_rooms_current");
                }
            }
            catch { /* metrics not available */ }
        }

        static int ParseMetric(string text, string name)
        {
            var match = Regex.Match(text, $@"^{name}\s+(\d+)", RegexOptions.Multiline);
            return match.Success && int.TryParse(match.Groups[1].Value, out var v) ? v : -1;
        }

        void OpenTerminalWithServer()
        {
            var cmd = BuildCommand();
            var script = $"tell application \"Terminal\" to do script \"{cmd}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e '{script}'",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            ShowNotification(new GUIContent("Server starting..."));
            _nextCheckTime = EditorApplication.timeSinceStartup + 3.0;
        }

        void KillPort()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"lsof -ti:9000 -sTCP:LISTEN | xargs kill -9 2>/dev/null; echo done\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                var proc = Process.Start(psi);
                proc.WaitForExit(3000);
                _lastCheckAlive = false;
                _connections = -1;
                _rooms = -1;
                Repaint();
                ShowNotification(new GUIContent("Server stopped"));
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Kill port failed: {e.Message}");
            }
        }
    }
}
