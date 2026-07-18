using System;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Owin.Hosting;
using Owin;

namespace LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting
{
    public sealed class OwinWebHost : IPanelWebHost
    {
        private readonly string url;
        private readonly Action<IAppBuilder> configure;
        private IDisposable host;

        public OwinWebHost(string url, Action<IAppBuilder> configure)
        {
            this.url = url ?? throw new ArgumentNullException(nameof(url));
            this.configure = configure ?? throw new ArgumentNullException(nameof(configure));
        }

        public void Start()
        {
            if (host != null) return;
            host = WebApp.Start(url, configure);
        }

        public void Dispose()
        {
            var current = host;
            host = null;
            if (current != null) current.Dispose();
        }
    }
}
