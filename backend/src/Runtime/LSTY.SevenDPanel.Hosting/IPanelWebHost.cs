using System;

namespace LSTY.SevenDPanel.Hosting
{
    public interface IPanelWebHost : IDisposable
    {
        void Start();
    }
}
