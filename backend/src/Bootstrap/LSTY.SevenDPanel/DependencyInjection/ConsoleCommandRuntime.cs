using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class ConsoleCommandRuntime : IModRuntime
    {
        private readonly object operationSync = new object();
        private readonly IModRuntime audit;
        private readonly IModRuntime commands;
        private readonly IModRuntime inner;
        private bool auditStarted;
        private bool commandsStarted;
        private bool innerStarted;
        private bool stopped;

        public ConsoleCommandRuntime(
            IModRuntime audit,
            IModRuntime commands,
            IModRuntime inner)
        {
            this.audit = audit ?? throw new ArgumentNullException(nameof(audit));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            lock (operationSync)
            {
                if (auditStarted || commandsStarted || innerStarted || stopped) return;
                try
                {
                    audit.Start();
                    auditStarted = true;
                    commands.Start();
                    commandsStarted = true;
                    inner.Start();
                    innerStarted = true;
                }
                catch
                {
                    RollBackStart();
                    stopped = true;
                    throw;
                }
            }
        }

        public void MarkGameReady()
        {
            lock (operationSync)
            {
                if (stopped) return;
                if (auditStarted) audit.MarkGameReady();
                if (commandsStarted) commands.MarkGameReady();
                if (innerStarted) inner.MarkGameReady();
            }
        }

        public void Stop()
        {
            lock (operationSync)
            {
                if (stopped && !innerStarted && !commandsStarted && !auditStarted) return;
                stopped = true;
                var failures = new List<Exception>();
                StopOne(inner, ref innerStarted, failures);
                StopOne(commands, ref commandsStarted, failures);
                StopOne(audit, ref auditStarted, failures);
                if (failures.Count > 0) throw new AggregateException(failures);
            }
        }

        private void RollBackStart()
        {
            var ignored = new List<Exception>();
            StopOne(inner, ref innerStarted, ignored);
            StopOne(commands, ref commandsStarted, ignored);
            StopOne(audit, ref auditStarted, ignored);
        }

        private static void StopOne(
            IModRuntime runtime,
            ref bool wasStarted,
            ICollection<Exception> failures)
        {
            if (!wasStarted) return;
            try
            {
                runtime.Stop();
                wasStarted = false;
            }
            catch (Exception ex) { failures.Add(ex); }
        }
    }
}