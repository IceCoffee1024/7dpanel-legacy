using System;
using Microsoft.Owin.Hosting;

namespace LSTY.SevenDPanel.Web
{
    public sealed class OwinWebHost : Hosting.IPanelWebHost
    {
        private readonly string url;
        private IDisposable host;

        public OwinWebHost(string url) { this.url = url ?? throw new ArgumentNullException(nameof(url)); }

        public void Start()
        {
            if (host != null) return;
            host = WebApp.Start(url, OwinStartup.Configure);
        }

        public void Dispose()
        {
            var current = host;
            host = null;
            if (current != null) current.Dispose();
        }
    }
}
