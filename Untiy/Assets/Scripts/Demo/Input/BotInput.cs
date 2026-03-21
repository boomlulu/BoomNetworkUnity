using UnityEngine;

namespace BoomNetworkDemo
{
    public class BotInput : IInputProvider
    {
        private Vector2 _dir;
        private float _timer;

        public Vector2 GetMoveInput() => _dir;

        public void Tick(float deltaTime)
        {
            _timer -= deltaTime;
            if (_timer <= 0)
            {
                _timer = Random.Range(0.5f, 2f);
                _dir = Random.insideUnitCircle.normalized;
            }
        }
    }
}
