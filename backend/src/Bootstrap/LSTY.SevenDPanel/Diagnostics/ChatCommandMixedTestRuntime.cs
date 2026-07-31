using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Diagnostics
{
    internal sealed class ChatCommandMixedTestRuntime : IModRuntime, IDisposable
    {
        private readonly PanelChatCommandTestingOptions options;
        private readonly GameChatCommandCatalog commands;
        private readonly Func<string, IReadOnlyList<string>> runBoundary;
        private readonly IModRuntime inner;
        private readonly object lifecycleSync = new object();
        private IDisposable? registration;
        private int gameReady;

        public ChatCommandMixedTestRuntime(
            PanelChatCommandTestingOptions options,
            GameChatCommandCatalog commands,
            Func<string, IReadOnlyList<string>> runBoundary,
            IModRuntime inner)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.runBoundary = runBoundary ?? throw new ArgumentNullException(nameof(runBoundary));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            lock (lifecycleSync)
            {
                if (registration != null) return;
                registration = ChatCommandTestConsoleBridge.Register(Execute);
                try
                {
                    inner.Start();
                }
                catch
                {
                    registration.Dispose();
                    registration = null;
                    throw;
                }
            }
        }

        public void MarkGameReady()
        {
            inner.MarkGameReady();
            Volatile.Write(ref gameReady, 1);
        }

        public void Stop()
        {
            lock (lifecycleSync)
            {
                if (registration == null) return;
                try
                {
                    inner.Stop();
                }
                finally
                {
                    Volatile.Write(ref gameReady, 0);
                    registration.Dispose();
                    registration = null;
                }
            }
        }

        public void Dispose() => Stop();

        private IReadOnlyList<string> Execute(IReadOnlyList<string> parameters)
        {
            if (parameters.Count != 2 ||
                !string.Equals(parameters[0], "chat", StringComparison.OrdinalIgnoreCase))
                return new[] { "Usage: 7dp-test chat <status|virtual|boundary|all>" };

            var mode = parameters[1];
            if (string.Equals(mode, "status", StringComparison.OrdinalIgnoreCase))
                return Status();
            if (string.Equals(mode, "virtual", StringComparison.OrdinalIgnoreCase))
                return RunVirtual();
            if (string.Equals(mode, "boundary", StringComparison.OrdinalIgnoreCase))
                return RunBoundary();
            if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
                return RunVirtual().Concat(RunBoundary()).ToArray();
            return new[] { "Usage: 7dp-test chat <status|virtual|boundary|all>" };
        }

        private IReadOnlyList<string> Status() => new[]
        {
            "enabled=" + options.Enabled.ToString().ToLowerInvariant(),
            "gameReady=" + (Volatile.Read(ref gameReady) != 0).ToString().ToLowerInvariant(),
            "testPlayerConfigured=" + (!string.IsNullOrWhiteSpace(options.TestPlayerId)).ToString().ToLowerInvariant(),
            "allowTeleport=" + options.AllowTeleport.ToString().ToLowerInvariant(),
            "allowRewardDelivery=" + options.AllowRewardDelivery.ToString().ToLowerInvariant(),
            "kickBoundary=disabled",
            "restartBoundary=disabled"
        };

        private IReadOnlyList<string> RunVirtual()
        {
            var failures = new List<string>();
            var descriptors = commands.Commands;
            var expectedIds = CommunityGameCommandDirectory.Definitions
                .Select(definition => definition.Id.ToString())
                .Concat(new[] { "Help" })
                .ToArray();
            var actualIds = descriptors.Select(descriptor => descriptor.CommandId).ToArray();
            foreach (var expectedId in expectedIds)
            {
                if (!actualIds.Contains(expectedId, StringComparer.OrdinalIgnoreCase))
                    failures.Add("missing command id " + expectedId);
            }

            var allNames = descriptors
                .SelectMany(descriptor => new[] { descriptor.Name }.Concat(descriptor.Aliases))
                .ToArray();
            if (allNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != allNames.Length)
                failures.Add("command names or aliases conflict");

            var context = new GameChatCommandContext(
                "virtual:test-actor",
                "virtual_actor",
                Array.Empty<string>());
            var help = commands.Handle("help", context);
            if (!help.IsHandled) failures.Add("help did not handle a virtual identity");
            if (commands.Handle("7dp-unknown-command", context).IsHandled)
                failures.Add("unknown command was handled");

            return failures.Count == 0
                ? new[]
                {
                    "virtual: PASSED (catalog, aliases, virtual identity, help and unknown-command routing).",
                    "virtual: stateful command coverage runs in the isolated backend test host."
                }
                : failures.Select(failure => "virtual: FAILED - " + failure).ToArray();
        }

        private IReadOnlyList<string> RunBoundary()
        {
            if (!options.Enabled)
                return new[] { "boundary: SKIPPED - disabled in config.json." };
            if (Volatile.Read(ref gameReady) == 0)
                return new[] { "boundary: SKIPPED - the game is not ready." };
            if (string.IsNullOrWhiteSpace(options.TestPlayerId))
                return new[] { "boundary: SKIPPED - no stable test player id is configured." };
            return runBoundary(options.TestPlayerId!);
        }
    }
}
