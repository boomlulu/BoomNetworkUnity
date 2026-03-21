using UnityEngine;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using BoomNetwork.Core.FrameSync;

namespace BoomNetworkDemo
{
    public class PersonManager : MonoBehaviour
    {
        // ===================== Config =====================

        [TitleGroup("Config")]
        [InlineEditor(ObjectFieldMode = InlineEditorObjectFieldModes.Boxed)]
        public NetworkConfig config;

        [TitleGroup("Config")]
        [LabelWidth(80)]
        public float moveSpeed = 5f;

        // ===================== Person Slots =====================

        [TitleGroup("Persons")]
        [TableList(AlwaysExpanded = true)]
        public List<PersonSlot> persons = new()
        {
            new PersonSlot { inputMode = InputMode.WASD, color = Color.green },
            new PersonSlot { inputMode = InputMode.Arrows, color = new Color(0.3f, 0.5f, 1f) },
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

            [TableColumnWidth(60)]
            [Button("Connect")]
            public void BtnConnect()
            {
                _manager?.ConnectPerson(this, null);
            }

            [TableColumnWidth(70)]
            [Button("Disconnect")]
            public void BtnDisconnect()
            {
                _manager?.DisconnectPerson(this);
            }

            // 运行时数据（不序列化）
            [NonSerialized] public Person person;
            [NonSerialized] public IInputProvider inputProvider;
            [NonSerialized] internal PersonManager _manager;
        }

        // ===================== Batch Actions =====================

        [TitleGroup("Actions")]
        [HorizontalGroup("Actions/H")]
        [Button("Add Person", ButtonSizes.Medium)]
        void AddPerson()
        {
            persons.Add(new PersonSlot
            {
                inputMode = InputMode.None,
                color = UnityEngine.Random.ColorHSV(0, 1, 0.5f, 1, 0.7f, 1),
            });
        }

        [HorizontalGroup("Actions/H")]
        [Button("Connect All", ButtonSizes.Medium), GUIColor(0.3f, 0.8f, 0.3f)]
        void ConnectAll()
        {
            ConnectSequential(0);
        }

        /// <summary>
        /// 依次连接：等前一个加入房间后再连下一个
        /// </summary>
        void ConnectSequential(int index)
        {
            if (index >= persons.Count) return;
            var slot = persons[index];

            // 跳过已在线的
            if (slot.person != null && slot.person.State != PersonState.Disconnected)
            {
                ConnectSequential(index + 1);
                return;
            }

            ConnectPerson(slot, () =>
            {
                // OnReady（首次加入 或 重连成功）后，连下一个
                ConnectSequential(index + 1);
            });
        }

        [HorizontalGroup("Actions/H")]
        [Button("Start Game", ButtonSizes.Medium), GUIColor(0.9f, 0.8f, 0.2f)]
        void StartGame()
        {
            // 任意一个 InRoom 状态的 Person 发 RequestStart
            foreach (var slot in persons)
            {
                if (slot.person?.State == PersonState.InRoom)
                {
                    slot.person.RequestStart();
                    Log("Requested start!");
                    return;
                }
            }
            Log("No person in room to start");
        }

        [HorizontalGroup("Actions/H")]
        [Button("Disconnect All", ButtonSizes.Medium), GUIColor(0.8f, 0.3f, 0.3f)]
        void DisconnectAll()
        {
            foreach (var slot in persons)
                DisconnectPerson(slot);
        }

        // ===================== Room =====================

        [TitleGroup("Room")]
        [DisplayAsString, LabelWidth(80)]
        public string roomInfo = "No room";

        [TitleGroup("Room")]
        [Button("Refresh Rooms"), GUIColor(0.3f, 0.6f, 0.9f)]
        void RefreshRooms()
        {
            // 用第一个已连接的 Person 查询
            foreach (var slot in persons)
            {
                if (slot.person?.State == PersonState.Connected || slot.person?.State == PersonState.InRoom)
                {
                    slot.person.GetRoomClient()?.GetRooms(rooms =>
                    {
                        roomInfo = $"{rooms.Length} room(s)";
                        foreach (var r in rooms)
                            Log($"  Room {r.RoomId}: {r.PlayerCount}/{r.MaxPlayers} {(r.Running ? "Playing" : "Waiting")}");
                    });
                    return;
                }
            }
            Log("No connected person to query rooms");
        }

        // ===================== Sync Check =====================

        [TitleGroup("Sync")]
        [DisplayAsString, HideLabel]
        [GUIColor("GetSyncColor")]
        public string syncStatus = "Waiting...";

        // ===================== Log =====================

        [TitleGroup("Log")]
        [DisplayAsString, HideLabel, MultiLineProperty(8)]
        [PropertyOrder(100)]
        public string logText = "";

        [TitleGroup("Log")]
        [Button("Clear Log")]
        [PropertyOrder(101)]
        void ClearLog() => logText = "";

        // ===================== Internal =====================

        private Dictionary<int, PlayerEntity> _entities = new();
        private int _authorityIndex = 0; // 哪个 Person 的帧数据驱动世界
        private int _currentRoomId = -1;
        private byte[] _inputBuf = new byte[8];

        void Awake()
        {
            // 绑定 slot → manager 引用
            foreach (var slot in persons)
                slot._manager = this;
        }

        void Update()
        {
            float dt = Time.deltaTime * 1000;

            for (int i = 0; i < persons.Count; i++)
            {
                var slot = persons[i];
                slot._manager = this; // 新增的 slot 也要绑定

                if (slot.person == null) continue;

                // Tick 网络
                slot.person.Tick(dt);

                // Tick 输入
                slot.inputProvider?.Tick(Time.deltaTime);

                // 发送输入
                if (slot.person.State == PersonState.Syncing && slot.inputProvider != null)
                {
                    var dir = slot.inputProvider.GetMoveInput();
                    EncodeInput(dir, _inputBuf);
                    slot.person.SendInput(_inputBuf);
                }

                // 更新面板显示
                slot.state = slot.person.State.ToString();
                slot.pid = slot.person.PlayerId > 0 ? slot.person.PlayerId.ToString() : "-";
                slot.frame = slot.person.FrameNumber.ToString();
            }

            UpdateSyncStatus();
        }

        void OnDestroy()
        {
            foreach (var slot in persons)
            {
                slot.person?.Disconnect();
                slot.person = null;
            }
            foreach (var kv in _entities)
            {
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            }
        }

        // ===================== Person Lifecycle =====================

        public void ConnectPerson(PersonSlot slot, Action onReady = null)
        {
            if (config == null)
            {
                Log("ERROR: NetworkConfig not assigned!");
                return;
            }

            // 已有 Person 且正在连接/已连接，不重复
            if (slot.person != null && slot.person.State != PersonState.Disconnected)
                return;

            bool isReconnect = slot.person != null && slot.person.HasPreviousIdentity;
            var person = slot.person ?? new Person();
            slot.person = person;
            slot.inputProvider ??= InputProviderFactory.Create(slot.inputMode);

            // 只在首次创建时注册事件，避免重连时重复注册
            if (!isReconnect)
            {
                person.OnLog += (p, msg) => Log($"[{slot.inputMode}] {msg}");

                person.OnConnected += p =>
                {
                    if (_currentRoomId > 0)
                    {
                        p.JoinRoom(_currentRoomId);
                    }
                    else
                    {
                        p.CreateAndJoinRoom(config.defaultMaxPlayers);
                    }
                };

                person.OnJoinedRoom += p =>
                {
                    _currentRoomId = p.RoomId;
                    roomInfo = $"Room {p.RoomId}";
                    SpawnEntity(p.PlayerId, slot.color, slot.inputMode.ToString());
                };

                person.OnReconnected += p =>
                {
                    Log($"[{slot.inputMode}] Reconnected as P{p.PlayerId}!");
                };

                // Fix #3: OnReady 统一回调（首次加入 和 重连成功 都触发）
                person.OnReady += p =>
                {
                    onReady?.Invoke();
                };

                person.OnFrameSyncStart += (p, data) =>
                {
                    Log($"[{slot.inputMode}] Syncing!");
                };

                // Authority: 第一个 Person 的帧数据驱动世界
                int index = persons.IndexOf(slot);
                if (index == 0)
                {
                    person.OnFrame += (p, frame) => OnAuthorityFrame(frame);
                }

                person.OnDisconnected += p =>
                {
                    Log($"[{slot.inputMode}] Lost connection");
                };
            }

            person.Connect(config);
        }

        public void DisconnectPerson(PersonSlot slot)
        {
            if (slot.person == null) return;

            // Disconnect 保留身份（PlayerId/RoomId），下次 Connect 会发 Reconnect
            slot.person.Disconnect();
            // 保留 slot.person 引用！重连时 ConnectPerson 检测到 hasIdentity 走重连
            slot.state = "Disconnected";
            slot.frame = "0";
        }

        /// <summary>
        /// 完全移除 Person（不可重连）
        /// </summary>
        public void RemovePerson(PersonSlot slot)
        {
            if (slot.person != null)
            {
                slot.person.DisconnectAndClear();
                slot.person = null;
            }
            slot.inputProvider = null;
            slot.state = "Idle";
            slot.pid = "-";
            slot.frame = "0";

            // TODO: 移除实体
        }

        // ===================== Frame Handler =====================

        void OnAuthorityFrame(FrameData frame)
        {
            if (frame.Inputs == null) return;

            // 帧同步固定增量: moveSpeed * frameInterval(秒)
            float frameInterval = 1f / 20f;  // TODO: 从 FrameSyncInitData 获取
            float delta = moveSpeed * frameInterval;

            for (int i = 0; i < frame.Inputs.Length; i++)
            {
                ref var input = ref frame.Inputs[i];
                if (input.DataLength < 8) continue;

                var dir = DecodeInput(input.DataSpan);
                var pid = input.PlayerId;

                if (!_entities.ContainsKey(pid))
                    SpawnEntity(pid, Color.gray, $"P{pid}");

                if (_entities.TryGetValue(pid, out var entity))
                    entity.ApplyMove(dir, delta);
            }
        }

        // ===================== Entity =====================

        void SpawnEntity(int playerId, Color color, string label)
        {
            if (_entities.ContainsKey(playerId)) return;
            var entity = PlayerEntity.Spawn(playerId, color, label);
            _entities[playerId] = entity;
            Log($"Spawned {label} (Player {playerId})");
        }

        // ===================== Sync Check =====================

        void UpdateSyncStatus()
        {
            uint? first = null;
            int syncCount = 0;

            foreach (var slot in persons)
            {
                if (slot.person == null || slot.person.State != PersonState.Syncing) continue;
                syncCount++;
                var f = slot.person.FrameNumber;
                if (first == null) first = f;
                else if (f != first.Value)
                {
                    syncStatus = $"DIFF: {first} vs {f} ({syncCount} syncing)";
                    return;
                }
            }

            if (syncCount >= 2)
                syncStatus = $"IN SYNC (frame {first}, {syncCount} clients)";
            else if (syncCount == 1)
                syncStatus = $"1 client syncing (frame {first})";
            else
                syncStatus = "Waiting...";
        }

        // ===================== Codec =====================

        static void EncodeInput(Vector2 dir, byte[] buf)
        {
            var xb = BitConverter.GetBytes(dir.x);
            var yb = BitConverter.GetBytes(dir.y);
            Buffer.BlockCopy(xb, 0, buf, 0, 4);
            Buffer.BlockCopy(yb, 0, buf, 4, 4);
        }

        static Vector2 DecodeInput(ReadOnlySpan<byte> buf)
        {
            var tmp = new byte[8];
            buf.Slice(0, 8).CopyTo(tmp);
            return new Vector2(BitConverter.ToSingle(tmp, 0), BitConverter.ToSingle(tmp, 4));
        }

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            logText = line + "\n" + logText;
            if (logText.Length > 3000)
                logText = logText.Substring(0, 3000);
            Debug.Log($"[PersonMgr] {msg}");
        }

        Color GetSyncColor()
        {
            if (syncStatus.Contains("IN SYNC")) return Color.green;
            if (syncStatus.Contains("DIFF")) return Color.yellow;
            return Color.white;
        }
    }
}
