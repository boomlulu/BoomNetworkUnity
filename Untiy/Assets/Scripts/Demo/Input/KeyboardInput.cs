using UnityEngine;

namespace BoomNetworkDemo
{
    public class WASDInput : IInputProvider
    {
        public Vector2 GetMoveInput() => new(
            (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
            (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
        public void Tick(float deltaTime) { }
    }

    public class ArrowsInput : IInputProvider
    {
        public Vector2 GetMoveInput() => new(
            (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
            (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
        public void Tick(float deltaTime) { }
    }

    public class IJKLInput : IInputProvider
    {
        public Vector2 GetMoveInput() => new(
            (Input.GetKey(KeyCode.L) ? 1 : 0) - (Input.GetKey(KeyCode.J) ? 1 : 0),
            (Input.GetKey(KeyCode.I) ? 1 : 0) - (Input.GetKey(KeyCode.K) ? 1 : 0));
        public void Tick(float deltaTime) { }
    }
}
