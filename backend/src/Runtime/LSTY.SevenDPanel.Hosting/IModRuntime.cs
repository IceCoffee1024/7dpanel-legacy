namespace LSTY.SevenDPanel.Hosting
{
    public interface IModRuntime
    {
        void Start();
        void MarkGameReady();
        void Stop();
    }
}
