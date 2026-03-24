using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BoomNetwork.Core.FrameSync;
using BoomNetwork.Client.FrameSync;

namespace BoomNetwork.Tests
{
    /// <summary>
    /// PlayMode 联调测试
    ///
    /// 前提: Go 帧同步服务器已启动（autoroom 模式）
    ///   cd svr && go run ./cmd/framesync/ -addr=:9000 -autoroom
    /// </summary>
    public class FrameSyncPlayTest
    {
        private const string Host = "127.0.0.1";
        private const int Port = 9000;

        private FrameSyncClient CreateClient()
        {
            // FrameSyncClient 内置 Transport/Session/ConnectionManager/ReconnectStrategy
            return new FrameSyncClient(heartbeatIntervalMs: 3000, heartbeatTimeoutMs: 30000);
        }

        [UnityTest, Order(3)]
        public IEnumerator ConnectAndBind()
        {
            var client = CreateClient();
            bool connected = false;
            int playerId = 0;
            client.OnConnected += () => { connected = true; playerId = client.PlayerId; };

            client.Connect(Host, Port);

            float timeout = 5f;
            while (!connected && timeout > 0)
            {
                client.Tick(Time.deltaTime * 1000);
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(connected, "Should connect within 5 seconds");
            Assert.Greater(playerId, 0, "PlayerId should be > 0");
            Debug.Log($"[Test] Connected as player {playerId}");

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

            bool c1Connected = false, c2Connected = false;
            client1.OnConnected += () => { c1Connected = true; Debug.Log($"[TwoClients] Client1 connected as player {client1.PlayerId}"); };
            client2.OnConnected += () => { c2Connected = true; Debug.Log($"[TwoClients] Client2 connected as player {client2.PlayerId}"); };
            client1.OnError += err => Debug.Log($"[TwoClients] Client1 error: {err}");
            client2.OnError += err => Debug.Log($"[TwoClients] Client2 error: {err}");

            Debug.Log("[TwoClients] Connecting client1...");
            client1.Connect(Host, Port);

            // 等 client1 连接成功
            float timeout = 10f;
            while (!c1Connected && timeout > 0)
            {
                client1.Tick(Time.deltaTime * 1000);
                timeout -= Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[TwoClients] Client1 connected={c1Connected}, connecting client2...");

            // client1 连接后再连 client2（autoroom 凑满房间后触发 Start）
            client2.Connect(Host, Port);

            // 等 client2 连接，然后 client1 发 RequestStart
            timeout = 10f;
            while (!c2Connected && timeout > 0)
            {
                client1.Tick(Time.deltaTime * 1000);
                client2.Tick(Time.deltaTime * 1000);
                timeout -= Time.deltaTime;
                yield return null;
            }

            // autoroom 模式下两个客户端已自动分配到同一房间，发 RequestStart 启动
            client1.RequestStart();

            timeout = 10f;
            while ((!c1Syncing || !c2Syncing) && timeout > 0)
            {
                client1.Tick(Time.deltaTime * 1000);
                client2.Tick(Time.deltaTime * 1000);
                timeout -= Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[TwoClients] c1={c1Connected}(id={client1.PlayerId}) c2={c2Connected}(id={client2.PlayerId}) c1Sync={c1Syncing} c2Sync={c2Syncing} timeout={timeout:F1}");
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
            int connectedCount = 0;
            int totalFrames = 0;

            for (int i = 0; i < clientCount; i++)
            {
                clients[i] = CreateClient();
                clients[i].OnConnected += () => connectedCount++;
                clients[i].OnFrame += _ => System.Threading.Interlocked.Increment(ref totalFrames);
            }

            // 批量连接
            for (int i = 0; i < clientCount; i++)
            {
                clients[i].Connect(Host, Port);
                if (i % 10 == 9)
                    yield return null; // 每 10 个让出一帧
            }

            // 等连接
            float timeout = 15f;
            while (connectedCount < clientCount && timeout > 0)
            {
                for (int i = 0; i < clientCount; i++)
                    clients[i].Tick(Time.deltaTime * 1000);
                timeout -= Time.deltaTime;
                yield return null;
            }
            Debug.Log($"[Stress] Connected: {connectedCount}/{clientCount}");
            Assert.AreEqual(clientCount, connectedCount, "All clients should connect");

            // autoroom 模式下自动分房，发 RequestStart（每组第一个客户端触发）
            for (int i = 0; i < clientCount; i++)
                clients[i].RequestStart();

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
