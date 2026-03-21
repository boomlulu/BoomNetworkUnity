namespace BoomNetworkDemo
{
    public enum InputMode
    {
        None,
        WASD,
        Arrows,
        IJKL,
        Bot,
    }

    public static class InputProviderFactory
    {
        public static IInputProvider Create(InputMode mode)
        {
            return mode switch
            {
                InputMode.WASD => new WASDInput(),
                InputMode.Arrows => new ArrowsInput(),
                InputMode.IJKL => new IJKLInput(),
                InputMode.Bot => new BotInput(),
                _ => new NoneInput(),
            };
        }
    }
}
