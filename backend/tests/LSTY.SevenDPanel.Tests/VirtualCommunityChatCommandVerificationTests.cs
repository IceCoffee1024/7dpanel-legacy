using System;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Community;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "Application")]
    public sealed class VirtualCommunityChatCommandVerificationTests
    {
        [Fact]
        public void Virtual_catalog_registers_every_command_and_alias_and_preserves_identity_context()
        {
            var consumer = new IdentityRecordingConsumer();
            var catalog = new GameChatCommandCatalog(
                CommunityGameChatCommandHandlerSet.Create(
                    new CommunityGameCommandRouter(new[] { consumer })));

            Assert.Equal(CommunityGameCommandDirectory.Definitions.Count, catalog.Commands.Count);
            for (var index = 0; index < CommunityGameCommandDirectory.Definitions.Count; index++)
            {
                var definition = CommunityGameCommandDirectory.Definitions[index];
                var descriptor = catalog.Commands[index];
                Assert.Equal(definition.Id.ToString(), descriptor.CommandId);
                Assert.Equal(definition.Name, descriptor.Name);
                Assert.Equal(definition.Aliases, descriptor.Aliases);
                Assert.True(descriptor.IsEnabled);
            }

            var result = catalog.Handle(
                "MONEY",
                new GameChatCommandContext(
                    "  VIRTUAL-EOS-CONTEXT  ",
                    "  Virtual Context Player  ",
                    Array.Empty<string>()));

            Assert.True(result.IsHandled);
            Assert.Equal("chat.command.help.succeeded", result.Code);
            Assert.Equal(
                new[] { "community.command.bal.succeeded", "identity=accepted" },
                result.Messages);
            Assert.NotNull(consumer.Context);
            Assert.Equal("VIRTUAL-EOS-CONTEXT", consumer.Context!.CrossplatformId);
            Assert.Equal("Virtual Context Player", consumer.Context.DisplayName);
            Assert.Empty(consumer.Context.Arguments);
        }

        [Fact]
        public void Virtual_pay_alias_transfers_persisted_balance_between_isolated_identities()
        {
            using var fixture = new VirtualCommunityChatCommandTestHarness();

            var transfer = fixture.Catalog.Handle(
                "SEND",
                VirtualCommunityChatCommandTestHarness.Context(
                    VirtualCommunityChatCommandTestHarness.AliceId,
                    "VirtualAlice",
                    "VirtualBob",
                    "25"));

            Assert.True(transfer.IsHandled);
            Assert.Equal("chat.command.help.succeeded", transfer.Code);
            Assert.Equal(
                new[]
                {
                    "community.command.pay.succeeded",
                    "amount=25",
                    "target=VirtualBob"
                },
                transfer.Messages);
            AssertBalance(fixture, VirtualCommunityChatCommandTestHarness.AliceId, "VirtualAlice", 75);
            AssertBalance(fixture, VirtualCommunityChatCommandTestHarness.BobId, "VirtualBob", 25);
        }

        [Fact]
        public void Virtual_invalid_pay_returns_a_structured_result_without_mutating_balances()
        {
            using var fixture = new VirtualCommunityChatCommandTestHarness();

            var result = fixture.Catalog.Handle(
                "pay",
                VirtualCommunityChatCommandTestHarness.Context(
                    VirtualCommunityChatCommandTestHarness.AliceId,
                    "VirtualAlice",
                    "VirtualBob",
                    "0"));

            Assert.True(result.IsHandled);
            Assert.Equal("chat.command.help.succeeded", result.Code);
            Assert.Equal(new[] { "community.command.pay.invalid_arguments" }, result.Messages);
            AssertBalance(fixture, VirtualCommunityChatCommandTestHarness.AliceId, "VirtualAlice", 100);
            AssertBalance(fixture, VirtualCommunityChatCommandTestHarness.BobId, "VirtualBob", 0);
        }

        private static void AssertBalance(
            VirtualCommunityChatCommandTestHarness fixture,
            string crossplatformId,
            string displayName,
            long expected)
        {
            var result = fixture.Catalog.Handle(
                "balance",
                VirtualCommunityChatCommandTestHarness.Context(crossplatformId, displayName));

            Assert.True(result.IsHandled);
            Assert.Equal("chat.command.help.succeeded", result.Code);
            Assert.Equal(
                new[] { "community.command.bal.succeeded", "balance=" + expected },
                result.Messages);
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class IdentityRecordingConsumer : ICommunityGameCommandConsumer
        {
            public CommunityGameCommandId Command => CommunityGameCommandId.Balance;
            public bool IsEnabled => true;
            public CommunityGameCommandContext? Context { get; private set; }

            public CommunityCommandConsumerResult Execute(CommunityGameCommandContext context)
            {
                Context = context;
                return CommunityCommandConsumerResult.Succeeded("identity=accepted");
            }
        }
    }
}
