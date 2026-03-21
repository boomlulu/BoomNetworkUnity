using UnityEngine;

namespace BoomNetworkDemo
{
    public interface IInputProvider
    {
        Vector2 GetMoveInput();
        void Tick(float deltaTime); // Bot 等需要内部计时
    }
}
