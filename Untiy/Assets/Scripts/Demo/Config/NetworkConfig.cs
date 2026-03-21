using UnityEngine;

namespace BoomNetworkDemo
{
    [CreateAssetMenu(fileName = "NetworkConfig", menuName = "BoomNetwork/NetworkConfig")]
    public class NetworkConfig : ScriptableObject
    {
        [Header("Server")]
        public string host = "127.0.0.1";
        public int port = 9000;

        [Header("Heartbeat")]
        public int heartbeatIntervalMs = 3000;
        public int heartbeatTimeoutMs = 30000;

        [Header("Room")]
        public int defaultMaxPlayers = 4;

    }
}
