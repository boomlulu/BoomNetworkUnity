using UnityEngine;

namespace BoomNetworkDemo.EntitySync
{
    // ===================== L2 策略接口 =====================

    /// <summary>看起来在动 — 两帧之间的外推</summary>
    public interface IDeadReckoning
    {
        void Extrapolate(ref Vector2 position, ref float rotation, Vector2 velocity, float dt);
    }

    /// <summary>动得像人 — visual 追 logical 的平滑方式</summary>
    public interface IInertiaModel
    {
        void Smooth(ref Vector2 visualPos, ref float visualRot,
                    Vector2 logicalPos, float logicalRot, float dt);
    }

    /// <summary>错了怎么办 — 收到权威状态时的修正策略</summary>
    public interface ICorrectionStrategy
    {
        void OnAuthorityReceived(ref Vector2 logicalPos, ref float logicalRot, ref Vector2 logicalVel,
                                 Vector2 authPos, float authRot, Vector2 authVel);
    }

    // ===================== 默认实现 =====================

    /// <summary>线性外推：pos += vel * dt</summary>
    public class LinearDeadReckoning : IDeadReckoning
    {
        public void Extrapolate(ref Vector2 position, ref float rotation, Vector2 velocity, float dt)
        {
            position += velocity * dt;
        }
    }

    /// <summary>弹簧阻尼惯性模型</summary>
    public class SpringInertia : IInertiaModel
    {
        public float positionSmoothTime = 0.1f;
        public float rotationSmoothTime = 0.05f;
        private Vector2 _velRef;

        public void Smooth(ref Vector2 vPos, ref float vRot,
                           Vector2 lPos, float lRot, float dt)
        {
            vPos = Vector2.SmoothDamp(vPos, lPos, ref _velRef, positionSmoothTime, Mathf.Infinity, dt);
            vRot = Mathf.LerpAngle(vRot, lRot, 1f - Mathf.Exp(-dt / Mathf.Max(rotationSmoothTime, 0.001f)));
        }
    }

    /// <summary>平滑纠偏：死区 + 正常 + 瞬移</summary>
    public class SmoothCorrection : ICorrectionStrategy
    {
        public float deadZone = 0.01f;
        public float snapThreshold = 5f;

        public void OnAuthorityReceived(ref Vector2 lPos, ref float lRot, ref Vector2 lVel,
                                        Vector2 aPos, float aRot, Vector2 aVel)
        {
            float dist = Vector2.Distance(lPos, aPos);
            if (dist < deadZone)
            {
                lVel = aVel; // 只更新速度用于后续 dead reckoning
                return;
            }
            // 正常和瞬移都直接更新 logical，visual 靠惯性追
            lPos = aPos;
            lRot = aRot;
            lVel = aVel;
        }
    }
}
