using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BoomNetwork.Core.FrameSync;
using BoomNetwork.Client.Transport;
using BoomNetwork.Client.Session;
using BoomNetwork.Client.Connection;
using BoomNetwork.Client.FrameSync;

namespace BoomNetwork.Tests
{
    /// <summary>
    /// PlayMode 联调测试
    ///
    /// 前提: Go 帧同步服务器已启动
    ///   cd svr && go run ./cmd/framesync/ -addr=:9000 -ppr=2
    /// </summary>
    public class FrameSyncPlayTest
    {
        private const string Host = "127.0.0.1";
        private const int Port = 9000;

        private FrameSyncClient CreateClient()
        {
            var transport = new TcpClientTransport();
            var session = new NetworkSession(transport);
            // 测试环境不自动重连
            var cm = new ConnectionManager(session, reconnectStrategy: null);
            cm.HeartbeatIntervalMs = 3000;
            cm.HeartbeatTimeoutMs = 30000; // 测试时不触发心跳超时
            return new FrameSyncClient(session, cm);
        }

        [UnityTest, Order(3)]
        public IEnumerator ConnectAndBind()
        {
            var client = CreateClient();
            bool bound = false;
            int playerId = 0;
            client.OnBound += id => { bound = true; playerId = id; };

            client.Connect(Host, Port);

            float timeout = 5f;
            while (!bound && timeout > 0)
            {
                client.Tick(Time.deltaTime * 1000);
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(bound, "Should bind within 5 seconds");
            Assert.Greater(playerId, 0, "PlayerId should be > 0");
            Debug.Log($"[Test] Bound as player {playerId}");

            client.Disconnect();
        }

        [UnityTest, Order(1)]
        public IEnumerator TwoClientsReceiveFrames()
        {
            var client1 = CreateClient();
            var client2 = CreateClient();

            bool c1Syncing = false, c2Syncing = false;
            int c1Frames = 0, c2Frames = 0;

            client1.OnFrameSyncStart += _ => c1Syncing = true;
            client2.OnFrameSyncStart += _ => c2Syncing = true;
            client1.OnFrame += _ => c1Frames++;
            client2.OnFrame += _ => c2Frames++;

            bool c1Bound = false, c2Bound = false;
            int c1Id = 0, c2Id = 0;
            client1.OnBound += id => { c1Bound = true; c1Id = id; Debug.Log($"[TwoClients] Client1 bound as player {id}"); };
            client2.OnBound += id => { c2Bound = true; c2Id = id; Debug.Log($"[TwoClients] Client2 bound as player {id}"); };
            client1.OnError += err => Debug.Log($"[TwoClients] Client1 error: {err}");
            client2.OnError += err => Debug.Log($"[TwoClients] Client2 error: {err}");

            Debug.Log("[TwoClients] Connecting client1...");
            client1.Connect(Host, Port);

            // 等 client1 绑定成功
            float timeout = 10f;
            while (!c1Bound && timeout > 0)
            {
                client1.Tick(Time.deltaTime * 1000);
                timeout -= Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[TwoClients] Client1 bound={c1Bound}, connecting client2...");

            // client1 绑定后再连 client2（触发 Start）
            client2.Connect(Host, Port);

            timeout = 10f;
            while ((!c1Syncing || !c2Syncing) && timeout > 0)
            {
                client1.Tick(Time.deltaTime * 1000);
                client2.Tick(Time.deltaTime * 1000);
                timeout -= Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[TwoClients] c1Bound={c1Bound}(id={c1Id}) c2Bound={c2Bound}(id={c2Id}) c1Sync={c1Syncing} c2Sync={c2Syncing} timeout={timeout:F1}");
            Assert.IsTrue(c1Syncing && c2Syncing, "Both should be syncing");

            // 收帧 2 秒
            float elapsed = 0;
            while (elapsed < 2f)
            {
                client1.Tick(Time.deltaTime * 1000);
                client2.Tick(Time.deltaTime * 1000);
                client1.SendInput(new byte[] { 1, 2, 3 });
                elapsed += Time.deltaTime;
                yield return null;
            }

            Debug.Log($"[Test] Client1: {c1Frames} frames, Client2: {c2Frames} frames");
            Assert.Greater(c1Frames, 20, "Should receive >20 frames in 2 seconds");
            Assert.Greater(c2Frames, 20, "Should receive >20 frames in 2 seconds");
            Assert.AreEqual(client1.LastFrameNumber, client2.LastFrameNumber, "Frame numbers should match");

            client1.Disconnect();
            client2.Disconnect();
        }

        [UnityTest, Order(2)]
        public IEnumerator StressTest_50Clients()
        {
            int clientCount = 50;
            var clients = new FrameSyncClient[clientCount];
            int boundCount = 0;
            int totalFrames = 0;

            for (int i = 0; i < clientCount; i++)
            {
                clients[i] = CreateClient();
                clients[i].OnBound += _ => boundCount++;
                clients[i].OnFrame += _ => System.Threading.Interlocked.Increment(ref totalFrames);
            }

            // 批量连接
            for (int i = 0; i < clientCount; i++)
            {
                clients[i].Connect(Host, Port);
                if (i % 10 == 9)
                    yield return null; // 每 10 个让出一帧
            }

            // 等绑定
            float timeout = 15f;
            while (boundCount < clientCount && timeout > 0)
            {
                for (int i = 0; i < clientCount; i++)
                    clients[i].Tick(Time.deltaTime * 1000);
                timeout -= Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[Stress] Bound: {boundCount}/{clientCount}");
            Assert.AreEqual(clientCount, boundCount, "All clients should bind");

            // 等帧同步开始
            timeout = 10f;
            while (timeout > 0)
            {
                bool allSyncing = true;
                for (int i = 0; i < clientCount; i++)
                {
                    clients[i].Tick(Time.deltaTime * 1000);
                    if (clients[i].CurrentState < FrameSyncClient.State.Syncing)
                        allSyncing = false;
                }
                if (allSyncing) break;
                timeout -= Time.deltaTime;
                yield return null;
            }

            // 重置计数，跑 3 秒
            System.Threading.Interlocked.Exchange(ref totalFrames, 0);
            float testDuration = 3f;
            float elapsed = 0;
            var inputData = new byte[32];

            while (elapsed < testDuration)
            {
                for (int i = 0; i < clientCount; i++)
                {
                    clients[i].Tick(Time.deltaTime * 1000);
                    clients[i].SendInput(inputData);
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            int frames = totalFrames;
            float fps = frames / (float)clientCount / testDuration;
            Debug.Log($"[Stress] {clientCount} clients, {frames} total frames, {fps:F1} fps/client");

            Assert.Greater(fps, 15f, "Should maintain >15 fps per client");

            for (int i = 0; i < clientCount; i++)
                clients[i].Disconnect();
        }
    }
}
