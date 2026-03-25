using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using BoomNetwork.Core.FrameSync;
using BoomNetworkDemo.EntitySync;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo03 — 实体权威同步 × 真实多客户端（ParrelSync）
    ///
    /// UI 结构与 Demo01.1 MultiClientPersonManager 一致:
    ///   PersonSlot 表格 + Room 管理 + Drop 测试
    /// 帧同步逻辑使用 Entity Authority Sync:
    ///   本地立刻执行 + 远端 Dead Reckoning + 惯性纠偏
    /// </summary>
    public class EntitySyncMultiClientManager : MonoBehaviour
    {
        // ===================== Config =====================

        [InfoBox(
            "Demo03 - Entity Authority Sync (Multi-Client)\n" +
            "Editor A: Connect All → Create Room → Join All → Start Game\n" +
            "Editor B: 填 Target Room ID → Connect All → Join All\n" +
            "Server Window 开 Network Simulation 测试延迟效果",
            InfoMessageType.Info)]
        [TitleGroup("Config")]
        [InlineEditor(ObjectFieldMode = InlineEditorObjectFieldModes.Boxed)]
        public NetworkConfig config;

        [TitleGroup("Config")]
        [LabelWidth(80)]
        public float moveSpeed = 5f;

        [TitleGroup("Config")]
        [LabelWidth(120), Tooltip("Drop 按钮断线秒数 (1=快速重连, 8=快照重连)")]
        public float dropSeconds = 1f;

        // ===================== Person Slots =====================

        [TitleGroup("Persons")]
        [TableList(AlwaysExpanded = true)]
        public List<PersonSlot> persons = new()
        {
            new PersonSlot { inputMode = InputMode.WASD, color = Color.green },
        };

        [Serializable]
        public class PersonSlot
        {
            [TableColumnWidth(80)]
            public InputMode inputMode = InputMode.WASD;
            [TableColumnWidth(60)]
            public Color color = Color.green;
            [TableColumnWidth(70), DisplayAsString]
            public string state = "Idle";
            [TableColumnWidth(40), DisplayAsString]
            public string pid = "-";
            [TableColumnWidth(60), DisplayAsString]
            public string frame = "0";
            [TableColumnWidth(50), DisplayAsString]
            public string rtt = "-";

            [TableColumnWidth(45), Button("Conn")]
            public void BtnConnect() => _manager?.ConnectPerson(this, null);
            [TableColumnWidth(45), Button("Leave")]
            public void BtnLeave() => person?.LeaveRoom();
            [TableColumnWidth(40), Button("Disc")]
            public void BtnDisconnect() => _manager?.DisconnectPerson(this);
            [TableColumnWidth(30), Button("X"), GUIColor(0.9f, 0.3f, 0.3f)]
            public void BtnRemove() => _manager?.RemovePerson(this);
            [TableColumnWidth(55), Button("Drop"), GUIColor(1f, 0.7f, 0.3f)]
            public void BtnDrop() => _manager?.SimulateNetworkDrop(this, _manager?.dropSeconds ?? 1f);

            [NonSerialized] public Person person;
            [NonSerialized] public IInputProvider inputProvider;
            [NonSerialized] internal EntitySyncMultiClientManager _manager;
        }

        // ===================== Actions =====================

        [TitleGroup("Actions")]
        [HorizontalGroup("Actions/H1")]
        [Button("Add Person", ButtonSizes.Medium)]
        void AddPerson()
        {
            persons.Add(new PersonSlot
            {
                inputMode = InputMode.None,
                color = UnityEngine.Random.ColorHSV(0, 1, 0.5f, 1, 0.7f, 1),
            });
        }

        [HorizontalGroup("Actions/H1")]
        [Button("Connect All", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.3f)]
        void ConnectAll()
        {
            foreach (var slot in persons)
                if (slot.person == null || slot.person.State == PersonState.Disconnected)
                    ConnectPerson(slot, null);
        }

        [HorizontalGroup("Actions/H1")]
        [Button("Disconnect All", ButtonSizes.Medium), GUIColor(0.8f, 0.3f, 0.3f)]
        void DisconnectAll()
        {
            foreach (var slot in persons) DisconnectPerson(slot);
        }

        // ===================== Room =====================

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H1")]
        [LabelWidth(100), LabelText("Target Room ID")]
        public int targetRoomId = 0;

        [HorizontalGroup("Room/H1")]
        [Button("Create Room"), GUIColor(0.2f, 0.7f, 0.4f)]
        void CreateRoom()
        {
            var p = FindConnectedPerson();
            if (p == null) { Log("No connected person"); return; }
            p.CreateRoom(config.defaultMaxPlayers, rid =>
            {
                targetRoomId = rid;
                Log($"Room {rid} created → targetRoomId updated");
                RefreshRooms();
            });
        }

        [HorizontalGroup("Room/H1")]
        [Button("Refresh"), GUIColor(0.3f, 0.6f, 0.9f)]
        void RefreshRooms()
        {
            var p = FindConnectedPerson();
            if (p == null) { Log("No connected person"); return; }
            p.GetRooms(rooms =>
            {
                _roomList.Clear();
                foreach (var r in rooms)
                    _roomList.Add(new RoomDisplay
                    {
                        roomId = r.RoomId,
                        players = $"{r.PlayerCount}/{r.MaxPlayers}",
                        status = r.Running ? "Playing" : "Waiting",
                        _mgr = this,
                    });
                Log($"Refreshed: {rooms.Length} room(s)");
            });
        }

        [TitleGroup("Room")]
        [TableList(IsReadOnly = true, AlwaysExpanded = true)]
        [SerializeField]
        private List<RoomDisplay> _roomList = new();

        [Serializable]
        public class RoomDisplay
        {
            [TableColumnWidth(50)] public int roomId;
            [TableColumnWidth(70)] public string players;
            [TableColumnWidth(70)] public string status;
            [TableColumnWidth(60), Button("Select")]
            public void BtnSelect()
            {
                if (_mgr != null) { _mgr.targetRoomId = roomId; _mgr.Log($"Selected room {roomId}"); }
            }
            [NonSerialized] internal EntitySyncMultiClientManager _mgr;
        }

        [TitleGroup("Room")]
        [HorizontalGroup("Room/H2")]
        [Button("Join All → Target Room", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.5f)]
        void JoinAllToTargetRoom()
        {
            if (targetRoomId <= 0) { Log("Set Target Room ID first (or Create Room)"); return; }
            foreach (var slot in persons)
                if (slot.person?.State == PersonState.Connected)
                {
                    slot.person.JoinRoom(targetRoomId);
                    Log($"[{slot.inputMode}] Joining room {targetRoomId}...");
                }
        }

        [HorizontalGroup("Room/H2")]
        [Button("Start Game", ButtonSizes.Medium), GUIColor(0.9f, 0.8f, 0.2f)]
        void StartGame()
        {
            foreach (var slot in persons)
                if (slot.person?.State == PersonState.InRoom)
                {
                    slot.person.RequestStart();
                    Log("Requested start!");
                    return;
                }
            Log("No person in room to start");
        }

        [HorizontalGroup("Room/H2")]
        [Button("Leave All"), GUIColor(0.8f, 0.5f, 0.2f)]
        void LeaveAll()
        {
            foreach (var slot in persons)
                if (slot.person?.State == PersonState.InRoom || slot.person?.State == PersonState.Syncing)
                    slot.person.LeaveRoom();
        }

        // ===================== Status =====================

        [TitleGroup("Log")]
        [DisplayAsString, HideLabel]
        public string logText = "";

        // ===================== Internal =====================

        private Dictionary<int, PlayerEntity> _entities = new();
        private Dictionary<int, NetworkTransformSync> _syncs = new();
        private byte[] _inputBuf = new byte[8];
        private uint _lastProcessedFrame;

        void Awake()
        {
            foreach (var slot in persons) slot._manager = this;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            foreach (var slot in persons)
            {
                if (slot.person == null) continue;
                slot._manager = this;
                slot.person.Tick(dt * 1000f);
                slot.state = slot.person.State.ToString();
                slot.pid = slot.person.PlayerId > 0 ? $"P{slot.person.PlayerId}" : "-";
                slot.frame = slot.person.FrameNumber.ToString();
                slot.rtt = slot.person.RttMs >= 0 ? $"{slot.person.RttMs:F0}" : "-";

                // ===== 权威实体：本地立刻执行输入 =====
                if (slot.person.State != PersonState.Syncing) continue;
                if (slot.inputProvider == null) continue;

                int pid = slot.person.PlayerId;
                if (!_entities.TryGetValue(pid, out var entity) || entity == null) continue;
                if (!_syncs.TryGetValue(pid, out var sync) || !sync.IsAuthority) continue;

                var dir = slot.inputProvider.GetMoveInput();
                if (dir.sqrMagnitude > 0.01f)
                {
                    entity.ApplyMove(dir * moveSpeed, dt);
                    sync.SetVelocity(dir * moveSpeed);
                    EncodeInput(dir, _inputBuf);
                    slot.person.SendInput(_inputBuf);
                }
                else
                {
                    sync.SetVelocity(Vector2.zero);
                }
            }
        }

        // ===================== Person Lifecycle =====================

        internal void ConnectPerson(PersonSlot slot, Action onReady)
        {
            if (slot.person == null)
            {
                slot.person = new Person();
                slot.inputProvider = InputProviderFactory.Create(slot.inputMode);
                WirePersonEvents(slot, onReady);
            }
            slot.person.Connect(config);
        }

        void WirePersonEvents(PersonSlot slot, Action onReady)
        {
            var person = slot.person;

            person.OnConnected += p => Log($"[{slot.inputMode}] Connected as P{p.PlayerId}");
            person.OnJoinedRoom += p => Log($"[{slot.inputMode}] Joined room {p.RoomId}");
            person.OnReady += p => { Log($"[{slot.inputMode}] Ready"); onReady?.Invoke(); };

            person.OnFrameSyncStart += (p, data) =>
            {
                Log($"[{slot.inputMode}] FrameSync started (rate={data.FrameRate})");
                SetupEntitySync(slot);
            };

            person.OnFrame += (p, frame) => OnServerFrame(slot, frame);

            person.OnRemotePlayerJoined += (p, pid) =>
            {
                Log($"[{slot.inputMode}] Remote P{pid} joined");
                SpawnEntity(pid, Color.gray, $"P{pid}");
            };
            person.OnRemotePlayerLeft += (p, pid) =>
            {
                Log($"[{slot.inputMode}] Remote P{pid} left");
                DestroyEntity(pid);
            };
            person.OnRemotePlayerOffline += (p, pid) =>
            {
                if (_entities.TryGetValue(pid, out var e) && e != null) e.SetOffline();
            };
            person.OnRemotePlayerOnline += (p, pid) =>
            {
                if (_entities.TryGetValue(pid, out var e) && e != null) e.SetOnline();
            };
            person.OnReconnected += p => Log($"[{slot.inputMode}] Reconnected");
            person.OnDisconnected += p => Log($"[{slot.inputMode}] Disconnected");
            person.OnLog += (p, msg) => Log($"[{slot.inputMode}] {msg}");

            person.TakeSnapshot = TakeWorldSnapshot;
            person.LoadSnapshot = LoadWorldSnapshot;
        }

        internal void DisconnectPerson(PersonSlot slot)
        {
            if (slot.person == null) return;
            int pid = slot.person.PlayerId;
            slot.person.DisconnectAndClear();
            slot.person = null;
            DestroyEntity(pid);
        }

        internal void RemovePerson(PersonSlot slot)
        {
            DisconnectPerson(slot);
            persons.Remove(slot);
        }

        internal void SimulateNetworkDrop(PersonSlot slot, float seconds)
        {
            if (slot.person?.State != PersonState.Syncing) return;
            slot.person.SimulateNetworkDrop();
            Log($"[{slot.inputMode}] Network dropped for {seconds}s");
        }

        // ===================== Entity Authority Sync =====================

        void SetupEntitySync(PersonSlot slot)
        {
            int pid = slot.person.PlayerId;
            var entity = SpawnEntity(pid, slot.color, slot.inputMode.ToString());
            var sync = _syncs[pid];
            sync.SetAuthority(true);
            sync.InitVisual((Vector2)entity.transform.position, 0);

            slot.person.RegisterAuthorityEntity(sync);

            // 接收远端实体状态
            slot.person.OnEntityState += (senderPid, entityId, data, offset, length) =>
            {
                if (_syncs.TryGetValue(entityId, out var remoteSyncComp) && !remoteSyncComp.IsAuthority)
                    remoteSyncComp.OnRemoteState(data, offset, length, senderPid);
            };

            Log($"[{slot.inputMode}] Entity sync: P{pid} is authority");
        }

        void OnServerFrame(PersonSlot slot, FrameData frame)
        {
            if (frame.FrameNumber <= _lastProcessedFrame) return;
            _lastProcessedFrame = frame.FrameNumber;

            int myPid = slot.person?.PlayerId ?? 0;

            for (int i = 0; i < frame.Inputs.Length; i++)
            {
                ref var input = ref frame.Inputs[i];
                int pid = input.PlayerId;
                if (pid == myPid) continue; // 本地已在 Update 处理

                if (!_entities.ContainsKey(pid))
                    SpawnEntity(pid, Color.gray, $"P{pid}");

                // 兜底：未收到 entity state 时用输入驱动
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

        // ===================== Entity Management =====================

        PlayerEntity SpawnEntity(int pid, Color color, string label)
        {
            if (_entities.TryGetValue(pid, out var existing) && existing != null)
                return existing;

            var entity = PlayerEntity.Spawn(pid, color, label);
            var sync = entity.gameObject.AddComponent<NetworkTransformSync>();
            sync.EntityId = pid;
            sync.InitVisual((Vector2)entity.transform.position, entity.FacingAngle);
            _entities[pid] = entity;
            _syncs[pid] = sync;
            return entity;
        }

        void DestroyEntity(int pid)
        {
            if (_entities.TryGetValue(pid, out var entity) && entity != null)
                Destroy(entity.gameObject);
            _entities.Remove(pid);
            _syncs.Remove(pid);
        }

        Person FindConnectedPerson()
        {
            foreach (var slot in persons)
                if (slot.person != null && (slot.person.State == PersonState.Connected
                    || slot.person.State == PersonState.InRoom
                    || slot.person.State == PersonState.Syncing))
                    return slot.person;
            return null;
        }

        // ===================== Snapshot（重连）=====================

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
                int pid   = BitConverter.ToInt32(data, offset);  offset += 4;
                float x   = BitConverter.ToSingle(data, offset); offset += 4;
                float y   = BitConverter.ToSingle(data, offset); offset += 4;
                float ang = BitConverter.ToSingle(data, offset); offset += 4;
                if (!_entities.ContainsKey(pid)) SpawnEntity(pid, Color.gray, $"P{pid}");
                if (_entities.TryGetValue(pid, out var entity) && entity != null)
                {
                    entity.transform.position = new Vector3(x, y, 0);
                    entity.SetFacing(ang);
                }
                if (_syncs.TryGetValue(pid, out var sync))
                    sync.InitVisual(new Vector2(x, y), ang);
            }
            _lastProcessedFrame = 0;
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
