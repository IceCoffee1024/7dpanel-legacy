using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Diagnostics;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "SevenDays")]
    public sealed class ChatCommandMixedTestRuntimeTests
    {
        [Fact]
        public void Disabled_runtime_keeps_virtual_available_and_never_runs_the_real_boundary()
        {
            var boundaryCalls = 0;
            using var runtime = new ChatCommandMixedTestRuntime(
                PanelChatCommandTestingOptions.Disabled,
                CreateCatalog(),
                _ =>
                {
                    boundaryCalls++;
                    return new[] { "unexpected" };
                },
                new RecordingRuntime());

            runtime.Start();
            var virtualResult = ChatCommandTestConsoleBridge.Execute(new[] { "chat", "virtual" });
            var boundaryResult = ChatCommandTestConsoleBridge.Execute(new[] { "chat", "boundary" });

            Assert.Contains(virtualResult, line => line.StartsWith("virtual: PASSED", StringComparison.Ordinal));
            Assert.Contains(boundaryResult, line => line.Contains("SKIPPED - disabled"));
            Assert.Equal(0, boundaryCalls);
        }

        [Fact]
        public void Enabled_boundary_requires_game_ready_and_then_uses_the_configured_identity()
        {
            string? receivedIdentity = null;
            var inner = new RecordingRuntime();
            using var runtime = new ChatCommandMixedTestRuntime(
                PanelChatCommandTestingOptions.FromBinding(true, "player-1", false, false),
                CreateCatalog(),
                identity =>
                {
                    receivedIdentity = identity;
                    return new[] { "boundary/identity: PASSED (passed)" };
                },
                inner);

            runtime.Start();
            var beforeReady = ChatCommandTestConsoleBridge.Execute(new[] { "chat", "boundary" });
            runtime.MarkGameReady();
            var afterReady = ChatCommandTestConsoleBridge.Execute(new[] { "chat", "boundary" });

            Assert.Contains(beforeReady, line => line.Contains("SKIPPED - the game is not ready"));
            Assert.Equal("player-1", receivedIdentity);
            Assert.Contains(afterReady, line => line.Contains("PASSED"));
            Assert.Equal(1, inner.MarkGameReadyCalls);
        }

        private static GameChatCommandCatalog CreateCatalog()
        {
            var handlers = CommunityGameCommandDirectory.Definitions
                .Select(definition => (IGameChatCommandHandler)new StubHandler(
                    new GameChatCommandDescriptor(
                        definition.Id.ToString(),
                        definition.Name,
                        definition.Aliases,
                        true)))
                .ToList();
            handlers.Add(new HelpGameChatCommandHandler(() => true));
            return new GameChatCommandCatalog(handlers);
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class StubHandler : IGameChatCommandHandler
        {
            public StubHandler(GameChatCommandDescriptor descriptor) => Descriptor = descriptor;
            public GameChatCommandDescriptor Descriptor { get; }
            public GameChatCommandResult Handle(GameChatCommandContext context) =>
                GameChatCommandResult.HelpSucceeded(Array.Empty<string>());
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingRuntime : IModRuntime
        {
            public int MarkGameReadyCalls { get; private set; }
            public void Start() { }
            public void MarkGameReady() => MarkGameReadyCalls++;
            public void Stop() { }
        }
    }
}
