using UnityEngine;

namespace BoomNetworkDemo
{
    public class NoneInput : IInputProvider
    {
        public Vector2 GetMoveInput() => Vector2.zero;
        public void Tick(float deltaTime) { }
    }
}
