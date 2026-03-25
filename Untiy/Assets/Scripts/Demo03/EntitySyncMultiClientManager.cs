using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using BoomNetwork.Core.FrameSync;
using BoomNetworkDemo.EntitySync;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo03 — 实体权威同步 × 真实多客户端
    ///
    /// 每个编辑器一个玩家。ParrelSync 或两台电脑各开一个 Unity。
    /// 本地玩家输入立刻执行（零延迟），远端玩家 Dead Reckoning + 惯性平滑。
    ///
    /// 验证流程：
    /// 1. BoomNetwork > Server Window > Start Server (非 autoroom)
    /// 2. Editor A: Connect → Create Room → Join → Start Game → WASD 移动
    /// 3. Editor B: 填 Target Room ID → Connect → Join
    /// 4. 两边互相看到对方角色平滑移动
    /// 5. Server Window > Network Simulation > 加 200ms 延迟 → 验证远端平滑纠偏
    /// </summary>
    public class EntitySyncMultiClientManager : MonoBehaviour
    {
        [InfoBox(
            "Demo03 - Entity Authority Sync (Multi-Client)\n" +
            "每个编辑器一个真实玩家。用 ParrelSync 或两台电脑测试。\n" +
            "Editor A: Connect → Create Room → Join → Start Game\n" +
            "Editor B: 填 Target Room ID → Connect → Join\n" +
            "Server Window 开 Network Simulation 测试延迟效果",
            InfoMessageType.Info)]
        [TitleGroup("Config")]
        [InlineEditor(ObjectFieldMode = InlineEditorObjectFieldModes.Boxed)]
        public NetworkConfig config;

        [TitleGroup("Config")]
        public float moveSpeed = 5f;

        [TitleGroup("Config")]
        public Color localColor = Color.green;

        [TitleGroup("Config")]
        public InputMode inputMode = InputMode.WASD;

        // ===================== Room =====================

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H1")]
        [LabelWidth(100), LabelText("Target Room ID")]
        public int targetRoomId = 0;

        [HorizontalGroup("Room/H1")]
        [Button("Create"), GUIColor(0.2f, 0.7f, 0.4f)]
        void CreateRoom()
        {
            if (_person == null || _person.State < PersonState.Connected) { Log("Not connected"); return; }
            _person.CreateRoom(config.defaultMaxPlayers, rid =>
            {
                targetRoomId = rid;
                Log($"Room {rid} created");
            });
        }

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H2")]
        [Button("Connect", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.3f)]
        void DoConnect()
        {
            if (_person != null && _person.State != PersonState.Disconnected) return;
            ConnectPerson();
        }

        [HorizontalGroup("Room/H2")]
        [Button("Join", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.5f)]
        void DoJoin()
        {
            if (_person?.State != PersonState.Connected) { Log("Not connected"); return; }
            if (targetRoomId <= 0) { Log("Set Target Room ID first"); return; }
            _person.JoinRoom(targetRoomId);
        }

        [HorizontalGroup("Room/H2")]
        [Button("Start", ButtonSizes.Medium), GUIColor(0.9f, 0.8f, 0.2f)]
        void DoStart()
        {
            if (_person?.State != PersonState.InRoom) { Log("Not in room"); return; }
            _person.RequestStart();
        }

        [HorizontalGroup("Room/H2")]
        [Button("Disc", ButtonSizes.Medium), GUIColor(0.8f, 0.3f, 0.3f)]
        void DoDisconnect()
        {
            if (_person == null) return;
            _person.DisconnectAndClear();
            _person = null;
            ClearEntities();
        }

        [HorizontalGroup("Room/H2")]
        [Button("Drop"), GUIColor(1f, 0.7f, 0.3f)]
        void DoDrop() => _person?.SimulateNetworkDrop();

        // ===================== Status =====================

        [TitleGroup("Status")]
        [DisplayAsString, HideLabel]
        public string statusText = "Idle";

        [TitleGroup("Status")]
        [DisplayAsString, HideLabel]
        public string logText = "";

        // ===================== Internal =====================

        private Person _person;
        private IInputProvider _input;
        private Dictionary<int, PlayerEntity> _entities = new();
        private Dictionary<int, NetworkTransformSync> _syncs = new();
        private byte[] _inputBuf = new byte[8];

        void Update()
        {
            if (_person == null) return;

            float dt = Time.deltaTime;
            _person.Tick(dt * 1000f);

            // 状态显示
            statusText = $"{_person.State}  P{_person.PlayerId}  Room:{_person.RoomId}  " +
                         $"F#{_person.FrameNumber}  RTT:{_person.RttMs:F0}ms";

            // ===== 权威实体：本地立刻执行 =====
            if (_person.State != PersonState.Syncing) return;
            if (_input == null) return;

            int myPid = _person.PlayerId;
            if (!_entities.TryGetValue(myPid, out var myEntity) || myEntity == null) return;
            if (!_syncs.TryGetValue(myPid, out var mySync) || !mySync.IsAuthority) return;

            var dir = _input.GetMoveInput();
            if (dir.sqrMagnitude > 0.01f)
            {
                myEntity.ApplyMove(dir * moveSpeed, dt);
                mySync.SetVelocity(dir * moveSpeed);
                EncodeInput(dir, _inputBuf);
                _person.SendInput(_inputBuf);
            }
            else
            {
                mySync.SetVelocity(Vector2.zero);
            }
        }

        // ===================== Person Setup =====================

        void ConnectPerson()
        {
            _person = new Person();
            _input = InputProviderFactory.Create(inputMode);

            _person.OnConnected += p => Log($"Connected as P{p.PlayerId}");
            _person.OnJoinedRoom += p => Log($"Joined room {p.RoomId}");
            _person.OnReady += p => Log("Ready");

            _person.OnFrameSyncStart += (p, data) =>
            {
                Log($"FrameSync started (rate={data.FrameRate})");
                SetupLocalAuthority();
            };

            _person.OnFrame += (p, frame) => OnServerFrame(frame);

            _person.OnEntityState += (senderPid, entityId, data, offset, length) =>
            {
                if (_syncs.TryGetValue(entityId, out var sync) && !sync.IsAuthority)
                    sync.OnRemoteState(data, offset, length, senderPid);
            };

            _person.OnRemotePlayerJoined += (p, pid) =>
            {
                Log($"Remote P{pid} joined");
                SpawnRemoteEntity(pid);
            };
            _person.OnRemotePlayerLeft += (p, pid) =>
            {
                Log($"Remote P{pid} left");
                DestroyEntity(pid);
            };
            _person.OnRemotePlayerOffline += (p, pid) =>
            {
                Log($"Remote P{pid} offline");
                if (_entities.TryGetValue(pid, out var e) && e != null) e.SetOffline();
            };
            _person.OnRemotePlayerOnline += (p, pid) =>
            {
                Log($"Remote P{pid} online");
                if (_entities.TryGetValue(pid, out var e) && e != null) e.SetOnline();
            };
            _person.OnReconnected += p => Log("Reconnected");
            _person.OnDisconnected += p => Log("Disconnected");
            _person.OnLog += (p, msg) => Log(msg);

            _person.TakeSnapshot = TakeWorldSnapshot;
            _person.LoadSnapshot = LoadWorldSnapshot;

            _person.Connect(config);
        }

        // ===================== Entity Authority =====================

        void SetupLocalAuthority()
        {
            int myPid = _person.PlayerId;
            var entity = SpawnLocalEntity(myPid);
            var sync = _syncs[myPid];
            sync.SetAuthority(true);
            sync.InitVisual((Vector2)entity.transform.position, 0);

            _person.RegisterAuthorityEntity(sync);
            Log($"Authority: P{myPid}");
        }

        PlayerEntity SpawnLocalEntity(int pid)
        {
            if (_entities.TryGetValue(pid, out var e) && e != null) return e;
            var entity = PlayerEntity.Spawn(pid, localColor, inputMode.ToString());
            entity.gameObject.AddComponent<NetworkTransformSync>();
            var sync = entity.GetComponent<NetworkTransformSync>();
            sync.EntityId = pid;
            sync.InitVisual((Vector2)entity.transform.position, 0);
            _entities[pid] = entity;
            _syncs[pid] = sync;
            return entity;
        }

        PlayerEntity SpawnRemoteEntity(int pid)
        {
            if (_entities.TryGetValue(pid, out var e) && e != null) return e;
            var entity = PlayerEntity.Spawn(pid, Color.gray, $"P{pid}");
            entity.gameObject.AddComponent<NetworkTransformSync>();
            var sync = entity.GetComponent<NetworkTransformSync>();
            sync.EntityId = pid;
            sync.InitVisual((Vector2)entity.transform.position, 0);
            // Remote: authority = false (default), Dead Reckoning + Inertia 自动工作
            _entities[pid] = entity;
            _syncs[pid] = sync;
            return entity;
        }

        void DestroyEntity(int pid)
        {
            if (_entities.TryGetValue(pid, out var e) && e != null) Destroy(e.gameObject);
            _entities.Remove(pid);
            _syncs.Remove(pid);
        }

        void ClearEntities()
        {
            foreach (var kv in _entities)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _entities.Clear();
            _syncs.Clear();
        }

        // ===================== Server Frame =====================

        void OnServerFrame(FrameData frame)
        {
            int myPid = _person?.PlayerId ?? 0;

            for (int i = 0; i < frame.Inputs.Length; i++)
            {
                ref var input = ref frame.Inputs[i];
                int pid = input.PlayerId;

                // 跳过本地（已在 Update 立刻执行）
                if (pid == myPid) continue;

                // 确保远端实体存在
                if (!_entities.ContainsKey(pid))
                    SpawnRemoteEntity(pid);

                // 远端由 OnEntityState → NetworkTransformSync 自动纠偏
                // 输入作为兜底（还没收到 entity state 时）
                if (_syncs.TryGetValue(pid, out var sync) && sync.CorrectionCount == 0 && input.DataLength >= 8)
                {
                    if (_entities.TryGetValue(pid, out var entity) && entity != null)
                    {
                        var dir = new Vector2(
                            BitConverter.ToSingle(input.Data, 0),
                            BitConverter.ToSingle(input.Data, 4));
                        entity.ApplyMove(dir * moveSpeed, 1f / 20f);
                    }
                }
            }
        }

        // ===================== Snapshot（重连用）=====================

        byte[] TakeWorldSnapshot()
        {
            var keys = new List<int>(_entities.Keys);
            keys.Sort();
            int count = keys.Count;
            var buf = new byte[2 + count * 16];
            buf[0] = (byte)(count & 0xFF);
            buf[1] = (byte)((count >> 8) & 0xFF);
            int offset = 2;
            foreach (var key in keys)
            {
                var e = _entities[key];
                var pos = e != null ? (Vector2)e.transform.position : Vector2.zero;
                float angle = e != null ? e.FacingAngle : 0f;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), key);   offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), pos.x); offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), pos.y); offset += 4;
                BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), angle); offset += 4;
            }
            return buf;
        }

        void LoadWorldSnapshot(byte[] data)
        {
            if (data == null || data.Length < 2) return;
            int count = data[0] | (data[1] << 8);
            int offset = 2;
            for (int i = 0; i < count && offset + 16 <= data.Length; i++)
            {
                int pid = BitConverter.ToInt32(data, offset);  offset += 4;
                float x = BitConverter.ToSingle(data, offset); offset += 4;
                float y = BitConverter.ToSingle(data, offset); offset += 4;
                float ang = BitConverter.ToSingle(data, offset); offset += 4;
                if (!_entities.ContainsKey(pid)) SpawnRemoteEntity(pid);
                if (_entities.TryGetValue(pid, out var entity) && entity != null)
                {
                    entity.transform.position = new Vector3(x, y, 0);
                    entity.SetFacing(ang);
                }
                if (_syncs.TryGetValue(pid, out var sync))
                    sync.InitVisual(new Vector2(x, y), ang);
            }
            Log($"Snapshot loaded: {count} entities");
        }

        // ===================== Helpers =====================

        static void EncodeInput(Vector2 dir, byte[] buf)
        {
            BitConverter.TryWriteBytes(new Span<byte>(buf, 0, 4), dir.x);
            BitConverter.TryWriteBytes(new Span<byte>(buf, 4, 4), dir.y);
        }

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            logText = line + "\n" + logText;
            if (logText.Length > 3000) logText = logText.Substring(0, 3000);
            Debug.Log($"[Demo03] {msg}");
        }
    }
}
