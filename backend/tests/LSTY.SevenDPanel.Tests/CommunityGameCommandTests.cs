using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Community;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Community;
using LSTY.SevenDPanel.Application.Community;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "Application")]
    public sealed class CommunityGameCommandTests
    {
        [Fact]
        public void Fixed_directory_routes_every_command_and_alias_to_one_enabled_consumer()
        {
            var consumers = CommunityGameCommandDirectory.Definitions
                .Select(definition => new RecordingConsumer(definition.Id))
                .ToArray();
            var router = new CommunityGameCommandRouter(consumers);

            foreach (var definition in CommunityGameCommandDirectory.Definitions)
            {
                var consumer = consumers.Single(candidate => candidate.Command == definition.Id);
                var result = router.Route(
                    definition.Name,
                    Context(ValidArguments[definition.Id]));
                Assert.True(result.IsHandled);
                Assert.Equal("community.command." + definition.Name + ".succeeded", result.Code);
                Assert.Equal(1, consumer.CallCount);

                foreach (var alias in definition.Aliases)
                {
                    router.Route(alias.ToUpperInvariant(), Context(ValidArguments[definition.Id]));
                    Assert.Equal(++consumer.ExpectedCallCount, consumer.CallCount);
                }
            }

            Assert.Equal(
                new[]
                {
                    "bal", "pay", "moneytop", "daily", "shop", "buy", "redeem",
                    "homes", "sethome", "delhome", "home", "cities", "city", "tpa",
                    "tpaccept", "tpreject", "back", "votekick", "voterestart"
                },
                CommunityGameCommandDirectory.Definitions.Select(definition => definition.Name));
        }

        [Fact]
        public void Fixed_command_set_has_no_hard_coded_contract_gap_commands()
        {
            Assert.Empty(CommunityGameCommandConsumerSet.ContractGapCommands);
        }

        [Fact]
        public void Tpa_commands_are_not_left_as_contract_gaps()
        {
            Assert.DoesNotContain(
                CommunityGameCommandId.TeleportAsk,
                CommunityGameCommandConsumerSet.ContractGapCommands);
            Assert.DoesNotContain(
                CommunityGameCommandId.TeleportAccept,
                CommunityGameCommandConsumerSet.ContractGapCommands);
            Assert.DoesNotContain(
                CommunityGameCommandId.TeleportReject,
                CommunityGameCommandConsumerSet.ContractGapCommands);
        }

        [Fact]
        public void Shop_command_is_not_left_as_a_contract_gap()
        {
            Assert.DoesNotContain(
                CommunityGameCommandId.Shop,
                CommunityGameCommandConsumerSet.ContractGapCommands);
        }

        [Fact]
        public void Unknown_disabled_invalid_and_permission_denied_commands_return_stable_private_codes()
        {
            var balance = new RecordingConsumer(CommunityGameCommandId.Balance);
            var pay = new RecordingConsumer(CommunityGameCommandId.Pay, enabled: false);
            var redeem = new RecordingConsumer(
                CommunityGameCommandId.Redeem,
                result: CommunityCommandConsumerResult.PermissionDenied());
            var router = new CommunityGameCommandRouter(new[] { balance, pay, redeem });

            Assert.Equal("community.command.unknown", router.Route("shutdown", Context()).Code);
            Assert.Equal("community.command.pay.unavailable", router.Route("pay", Context("EOS-B", "10")).Code);
            Assert.Equal("community.command.bal.invalid_arguments", router.Route("bal", Context("extra")).Code);
            Assert.Equal("community.command.redeem.permission_denied", router.Route(
                "redeem",
                Context("ABCD-EFGH-IJKL-MNOP")).Code);
            Assert.Equal(0, balance.CallCount);
            Assert.Equal(0, pay.CallCount);
            Assert.Equal(1, redeem.CallCount);
        }

        [Theory]
        [InlineData("pay", "../../serverconfig.xml", "10")]
        [InlineData("redeem", "{\"command\":\"shutdown\"}", null)]
        [InlineData("buy", "product;DROP_TABLE", "1")]
        [InlineData("votekick", "yes", "extra")]
        [InlineData("voterestart", "shutdown", null)]
        public void Router_rejects_paths_scripts_json_and_console_like_arguments(
            string command,
            string first,
            string? second)
        {
            var definition = CommunityGameCommandDirectory.Find(command)!;
            var consumer = new RecordingConsumer(definition.Id);
            var router = new CommunityGameCommandRouter(new[] { consumer });
            var arguments = second == null ? new[] { first } : new[] { first, second };

            var result = router.Route(command, Context(arguments));

            Assert.Equal("community.command." + command + ".invalid_arguments", result.Code);
            Assert.Equal(0, consumer.CallCount);
        }

        [Fact]
        public void Runtime_subscribes_when_ready_and_only_sends_private_structured_results()
        {
            var source = new RecordingCommandSource();
            var replies = new RecordingPrivateReplies();
            var balance = new RecordingConsumer(CommunityGameCommandId.Balance);
            using var runtime = new CommunityCommandRuntime(
                source,
                new CommunityGameCommandRouter(new[] { balance }),
                replies);

            runtime.Start();
            runtime.MarkGameReady();
            source.Emit(new CommunityCommandEnvelope("EOS-A", "Alice", "balance", Array.Empty<string>()));

            var reply = Assert.Single(replies.Replies);
            Assert.Equal("EOS-A", reply.CrossplatformId);
            Assert.Equal("community.command.bal.succeeded", reply.Code);
            Assert.Equal(1, balance.CallCount);
            runtime.Stop();
            source.Emit(new CommunityCommandEnvelope("EOS-A", "Alice", "bal", Array.Empty<string>()));
            Assert.Single(replies.Replies);
        }

        [Fact]
        public void Fixed_game_chat_handlers_register_every_command_and_preserve_structured_codes()
        {
            var consumers = CommunityGameCommandDirectory.Definitions
                .Select(definition => new RecordingConsumer(definition.Id))
                .ToArray();
            var handlers = CommunityGameChatCommandHandlerSet.Create(
                new CommunityGameCommandRouter(consumers));
            var catalog = new GameChatCommandCatalog(handlers);

            Assert.Equal(CommunityGameCommandDirectory.Definitions.Count, handlers.Count);
            Assert.Equal(
                CommunityGameCommandDirectory.Definitions.Select(definition => definition.Name),
                handlers.Select(handler => handler.Descriptor.Name));

            var result = catalog.Handle(
                "BALANCE",
                new GameChatCommandContext("EOS-A", "Alice", Array.Empty<string>()));

            Assert.True(result.IsHandled);
            Assert.Equal(new[] { "community.command.bal.succeeded" }, result.Messages);
            Assert.Equal(1, consumers.Single(consumer =>
                consumer.Command == CommunityGameCommandId.Balance).CallCount);
        }

        [Fact]
        public void Seven_days_snapshot_resolves_unique_players_and_tracks_vote_eligibility_duration()
        {
            var now = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
            var players = new[]
            {
                NativePlayer("EOS-A", "Alice", 1),
                NativePlayer("EOS-B", "Bob", 2)
            };
            var provider = new SevenDaysCommunityPlayerSnapshotProvider(
                () => players,
                () => now);

            Assert.Equal("EOS-B", provider.ResolveOnline("bob")!.CrossplatformId);
            now = now.AddMinutes(7);

            var vote = provider.Capture(VoteKind.Kick, "EOS-A", "Bob");

            Assert.Equal("EOS-B", vote.TargetCrossplatformId);
            Assert.All(vote.EligiblePlayers, player =>
                Assert.Equal(TimeSpan.FromMinutes(7), player.OnlineDuration));
        }

        private static readonly IReadOnlyDictionary<CommunityGameCommandId, string[]> ValidArguments =
            new Dictionary<CommunityGameCommandId, string[]>
            {
                [CommunityGameCommandId.Balance] = Array.Empty<string>(),
                [CommunityGameCommandId.Pay] = new[] { "EOS-B", "10" },
                [CommunityGameCommandId.MoneyTop] = Array.Empty<string>(),
                [CommunityGameCommandId.Daily] = Array.Empty<string>(),
                [CommunityGameCommandId.Shop] = Array.Empty<string>(),
                [CommunityGameCommandId.Buy] = new[] { "product-1", "2" },
                [CommunityGameCommandId.Redeem] = new[] { "ABCD-EFGH-IJKL-MNOP" },
                [CommunityGameCommandId.Homes] = Array.Empty<string>(),
                [CommunityGameCommandId.SetHome] = new[] { "base" },
                [CommunityGameCommandId.DeleteHome] = new[] { "base" },
                [CommunityGameCommandId.Home] = new[] { "base" },
                [CommunityGameCommandId.Cities] = Array.Empty<string>(),
                [CommunityGameCommandId.City] = new[] { "trader" },
                [CommunityGameCommandId.TeleportAsk] = new[] { "EOS-B" },
                [CommunityGameCommandId.TeleportAccept] = Array.Empty<string>(),
                [CommunityGameCommandId.TeleportReject] = Array.Empty<string>(),
                [CommunityGameCommandId.Back] = Array.Empty<string>(),
                [CommunityGameCommandId.VoteKick] = new[] { "EOS-B" },
                [CommunityGameCommandId.VoteRestart] = Array.Empty<string>()
            };

        private static CommunityGameCommandContext Context(params string[] arguments) =>
            new CommunityGameCommandContext("EOS-A", "Alice", arguments);

        private static SevenDaysCommunityNativePlayer NativePlayer(
            string crossplatformId,
            string displayName,
            int entityId) =>
            new SevenDaysCommunityNativePlayer(
                crossplatformId,
                displayName,
                new TeleportPlayerSnapshot(
                    crossplatformId,
                    entityId,
                    new WorldPosition("world", entityId, 70, entityId, 0),
                    true,
                    true,
                    true,
                    false,
                    false,
                    new WorldBounds(-1000, 1000, -1000, 1000)));

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingConsumer : ICommunityGameCommandConsumer
        {
            private readonly CommunityCommandConsumerResult result;

            public RecordingConsumer(
                CommunityGameCommandId command,
                bool enabled = true,
                CommunityCommandConsumerResult? result = null)
            {
                Command = command;
                IsEnabled = enabled;
                this.result = result ?? CommunityCommandConsumerResult.Succeeded();
            }

            public CommunityGameCommandId Command { get; }
            public bool IsEnabled { get; }
            public int CallCount { get; private set; }
            public int ExpectedCallCount { get; set; } = 1;

            public CommunityCommandConsumerResult Execute(CommunityGameCommandContext context)
            {
                CallCount++;
                return result;
            }
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingCommandSource : ICommunityCommandSource
        {
            private Action<CommunityCommandEnvelope>? handler;

            public IDisposable Subscribe(Action<CommunityCommandEnvelope> callback)
            {
                handler = callback;
                return new CallbackDisposable(() => handler = null);
            }

            public void Emit(CommunityCommandEnvelope command) => handler?.Invoke(command);
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingPrivateReplies : ICommunityPrivateReplyPort
        {
            public List<Reply> Replies { get; } = new List<Reply>();

            public void Send(
                string crossplatformId,
                string code,
                IReadOnlyList<string> messages) =>
                Replies.Add(new Reply(crossplatformId, code));
        }

        private sealed record Reply(string CrossplatformId, string Code);

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class CallbackDisposable : IDisposable
        {
            private Action? callback;

            public CallbackDisposable(Action callback) => this.callback = callback;

            public void Dispose()
            {
                var current = callback;
                callback = null;
                current?.Invoke();
            }
        }
    }
}
