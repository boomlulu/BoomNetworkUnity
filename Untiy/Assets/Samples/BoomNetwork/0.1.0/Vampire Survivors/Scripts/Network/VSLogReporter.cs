using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using UnityEngine;

namespace BoomNetwork.Samples.VampireSurvivors
{
    public class VSLogReporter : MonoBehaviour
    {
        const string ServerUrl = "http://localhost:9878/client-logs";
        const float FlushIntervalSec = 3f;
        const int FlushBatchSize = 50;

        int _pid = -1;
        readonly List<string> _pending = new List<string>();
        readonly object _pendingLock = new object();
        float _flushTimer;

        static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // ──────────────────────────────────────────────────────────
        // Channel prefix table (priority: first match wins)
        // ──────────────────────────────────────────────────────────
        static readonly (string prefix, string channel)[] PrefixMap = {
            ("[FrameSyncClient]",    "FrameSync"),
            ("[BoomNetwork]",        "Network"),
            ("[CM]",                 "Reconnect"),
            ("[Composite]",          "Reconnect"),
            ("[SnapshotReconnect]",  "Reconnect"),
            ("[VS][Wave]",           "Game"),
            ("[VS][Player]",         "Game"),
            ("[VS-Jitter]",          "Jitter"),
            ("[VSRenderer Perf",     "Perf"),
            ("[VS]",                 "VS"),
        };

        public void SetPid(int pid) => _pid = pid;

        void OnEnable()
        {
            Application.logMessageReceivedThreaded += OnLogReceived;
        }

        void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLogReceived;
            TryFlush();
        }

        void OnLogReceived(string message, string stackTrace, LogType type)
        {
            string channel = ResolveChannel(message, type);
            if (channel == null) return;

            string level = type == LogType.Error || type == LogType.Exception ? "ERROR"
                         : type == LogType.Warning ? "WARN"
                         : "INFO";

            long tsMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int pid = _pid;

            // Build compact JSON entry without reflection
            var sb = new StringBuilder(256);
            sb.Append("{\"ts\":");
            sb.Append(tsMs);
            sb.Append(",\"channel\":\"");
            sb.Append(EscapeJson(channel));
            sb.Append("\",\"level\":\"");
            sb.Append(level);
            sb.Append("\",\"pid\":");
            sb.Append(pid);
            sb.Append(",\"msg\":\"");
            sb.Append(EscapeJson(message.Length > 512 ? message.Substring(0, 512) : message));
            sb.Append("\",\"extra\":{}}");

            bool flush = false;
            lock (_pendingLock)
            {
                _pending.Add(sb.ToString());
                if (_pending.Count >= FlushBatchSize) flush = true;
            }
            if (flush) TryFlush();
        }

        void Update()
        {
            _flushTimer += Time.unscaledDeltaTime;
            if (_flushTimer >= FlushIntervalSec)
            {
                _flushTimer = 0f;
                TryFlush();
            }
        }

        void TryFlush()
        {
            List<string> batch;
            lock (_pendingLock)
            {
                if (_pending.Count == 0) return;
                batch = new List<string>(_pending);
                _pending.Clear();
            }
            SendAsync(batch);
        }

        static void SendAsync(List<string> entries)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var sb = new StringBuilder(entries.Count * 200);
                    sb.Append('[');
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(entries[i]);
                    }
                    sb.Append(']');
                    var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json");
                    _http.PostAsync(ServerUrl, content).Wait();
                }
                catch
                {
                    // server offline — swallow silently
                }
            });
        }

        static string ResolveChannel(string msg, LogType type)
        {
            foreach (var (prefix, channel) in PrefixMap)
                if (msg.StartsWith(prefix, StringComparison.Ordinal))
                    return channel;

            // Other Error/Warning → System; other Info/Debug → discard
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
                return "System";

            return null;
        }

        static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
