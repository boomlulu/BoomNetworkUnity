using System;
using UnityEngine;
using BoomNetwork.Core.FrameSync;

namespace BoomNetworkDemo.EntitySync
{
    /// <summary>
    /// 2D 实体网络同步组件（EntityView 的具体实现）
    ///
    /// 职责：
    ///   Authority 模式：每帧从 transform 读状态 → WriteState 序列化给框架
    ///   Remote 模式：OnRemoteState 收到权威 → Dead Reckoning 外推 → Inertia 平滑渲染
    ///
    /// 状态格式（20B）：[posX:4][posY:4][rot:4][velX:4][velY:4]
    /// </summary>
    public class NetworkTransformSync : MonoBehaviour, IEntitySync
    {
        public const int STATE_SIZE = 20;

        // ===== IEntitySync =====
        public int EntityId { get; set; }
        public int StateSize => STATE_SIZE;

        // ===== 模式 =====
        public bool IsAuthority { get; private set; }

        // ===== Logical State（权威真值 + Dead Reckoning 外推）=====
        [NonSerialized] public Vector2 LogicalPosition;
        [NonSerialized] public float LogicalRotation;
        [NonSerialized] public Vector2 LogicalVelocity;

        // ===== Visual State（渲染值，惯性追踪 Logical）=====
        private Vector2 _visualPos;
        private float _visualRot;

        // ===== 可插拔策略（默认开箱即用）=====
        public IDeadReckoning DeadReckoning = new LinearDeadReckoning();
        public IInertiaModel Inertia = new SpringInertia();
        public ICorrectionStrategy Correction = new SmoothCorrection();

        // ===== 统计 =====
        public int CorrectionCount { get; private set; }

        public void SetAuthority(bool isAuthority)
        {
            IsAuthority = isAuthority;
            if (isAuthority)
            {
                // 权威模式：logical = transform
                LogicalPosition = (Vector2)transform.position;
                LogicalRotation = transform.eulerAngles.z;
            }
        }

        // ===================== IEntitySync 实现 =====================

        public int WriteState(byte[] buf, int offset)
        {
            // Authority: 从 transform 读最新状态
            LogicalPosition = (Vector2)transform.position;
            LogicalRotation = transform.eulerAngles.z;

            BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), LogicalPosition.x);     offset += 4;
            BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), LogicalPosition.y);     offset += 4;
            BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), LogicalRotation);       offset += 4;
            BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), LogicalVelocity.x);     offset += 4;
            BitConverter.TryWriteBytes(new Span<byte>(buf, offset, 4), LogicalVelocity.y);
            return STATE_SIZE;
        }

        public void OnRemoteState(byte[] data, int offset, int length, int senderPlayerId)
        {
            if (length < STATE_SIZE) return;

            var authPos = new Vector2(
                BitConverter.ToSingle(data, offset),
                BitConverter.ToSingle(data, offset + 4));
            float authRot = BitConverter.ToSingle(data, offset + 8);
            var authVel = new Vector2(
                BitConverter.ToSingle(data, offset + 12),
                BitConverter.ToSingle(data, offset + 16));

            // 纠偏策略决定如何修正 logical
            Correction.OnAuthorityReceived(
                ref LogicalPosition, ref LogicalRotation, ref LogicalVelocity,
                authPos, authRot, authVel);

            CorrectionCount++;
        }

        // ===================== Unity 生命周期 =====================

        void LateUpdate()
        {
            if (IsAuthority)
            {
                // Authority: transform 由游戏逻辑直接控制，不插手
                return;
            }

            float dt = Time.deltaTime;

            // ① Dead Reckoning：帧间外推 logical
            DeadReckoning.Extrapolate(ref LogicalPosition, ref LogicalRotation, LogicalVelocity, dt);

            // ② Inertia：visual 平滑追 logical
            Inertia.Smooth(ref _visualPos, ref _visualRot, LogicalPosition, LogicalRotation, dt);

            // 应用到 Transform
            transform.position = new Vector3(_visualPos.x, _visualPos.y, 0);
            transform.rotation = Quaternion.Euler(0, 0, _visualRot);
        }

        /// <summary>初始化 visual 状态（避免第一帧闪跳）</summary>
        public void InitVisual(Vector2 pos, float rot)
        {
            LogicalPosition = pos;
            LogicalRotation = rot;
            _visualPos = pos;
            _visualRot = rot;
            transform.position = new Vector3(pos.x, pos.y, 0);
            transform.rotation = Quaternion.Euler(0, 0, rot);
        }

        /// <summary>Authority 每帧更新速度（游戏层调用，用于 Dead Reckoning）</summary>
        public void SetVelocity(Vector2 vel)
        {
            LogicalVelocity = vel;
        }
    }
}
