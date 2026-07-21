using System;

namespace LSTY.SevenDPanel.Hosting
{
    public sealed class ModHost : IModRuntime, IPanelRuntimeStatus, IDisposable
    {
        private readonly object sync = new object();
        private readonly Func<IPanelWebHost> webHostFactory;
        private readonly Action<string> log;
        private IPanelWebHost? webHost;
        private ModHostState state = ModHostState.Created;
        private GameReadinessState gameReadiness = GameReadinessState.Loading;

        public ModHost(Func<IPanelWebHost> webHostFactory, Action<string>? log = null)
        {
            this.webHostFactory = webHostFactory ?? throw new ArgumentNullException(nameof(webHostFactory));
            this.log = log ?? (_ => { });
        }

        public ModHostState State
        {
            get { lock (sync) return state; }
        }

        public GameReadinessState GameReadiness
        {
            get { lock (sync) return gameReadiness; }
        }

        public void Start()
        {
            IPanelWebHost? candidate = null;
            lock (sync)
            {
                if (state == ModHostState.Running || state == ModHostState.Starting)
                    return;
                if (state == ModHostState.Draining || state == ModHostState.Stopped || state == ModHostState.Faulted)
                    return;
                state = ModHostState.Starting;
            }

            try
            {
                candidate = webHostFactory();
                candidate.Start();
                lock (sync)
                {
                    if (state != ModHostState.Starting)
                    {
                        webHost = null;
                    }
                    else
                    {
                        webHost = candidate;
                        state = ModHostState.Running;
                        log("7DPanel OWIN host started.");
                        return;
                    }
                }
                candidate.Dispose();
                lock (sync) state = ModHostState.Stopped;
            }
            catch (Exception ex)
            {
                lock (sync)
                {
                    webHost = null;
                    if (state == ModHostState.Starting) state = ModHostState.Faulted;
                }
                log("7DPanel OWIN host failed to start: " + ex);
                try { if (candidate != null) candidate.Dispose(); } catch { }
            }
        }

        public void MarkGameReady()
        {
            lock (sync)
            {
                if (gameReadiness != GameReadinessState.Stopping)
                    gameReadiness = GameReadinessState.Ready;
            }
        }

        public void Stop()
        {
            IPanelWebHost? candidate = null;
            lock (sync)
            {
                gameReadiness = GameReadinessState.Stopping;
                if (state == ModHostState.Draining || state == ModHostState.Stopped || state == ModHostState.Faulted)
                    return;
                if (state == ModHostState.Created)
                {
                    state = ModHostState.Stopped;
                    return;
                }
                state = ModHostState.Draining;
                candidate = webHost;
                webHost = null;
            }

            try
            {
                if (candidate != null) candidate.Dispose();
                lock (sync) state = ModHostState.Stopped;
                log("7DPanel OWIN host stopped.");
            }
            catch (Exception ex)
            {
                lock (sync) state = ModHostState.Faulted;
                log("7DPanel OWIN host failed to stop: " + ex);
                throw;
            }
        }

        public void Dispose() { Stop(); }
    }
}
