using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Discord;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Discord;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "Application")]
    public sealed class DiscordInboundTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Slash_commands_bind_players_use_a_fixed_typed_whitelist_and_dedupe_interactions()
        {
            using var database = ConfiguredDatabase();
            database.Store.SaveCommandSetting(new DiscordCommandSetting("bind", true, true));
            database.Store.SaveCommandSetting(new DiscordCommandSetting("status", true, true));
            database.Store.SaveCommandSetting(new DiscordCommandSetting("players", false, true));
            SaveBindingCode(database.Store, "player-1", "ABCD-1234");
            var dispatcher = new RecordingCommandDispatcher();
            var useCase = new HandleDiscordInteractionUseCase(database.Store, dispatcher, () => Now);

            var bind = new DiscordInteractionEnvelope(
                "interaction-bind", 2, "guild-1", "channel-public", "discord-user-1",
                false, "bind", "ABCD-1234");
            var bound = await useCase.ExecuteAsync(bind, CancellationToken.None);

            Assert.Equal(DiscordInboundDisposition.Bound, bound.Disposition);
            Assert.Equal("player-1", database.Store.FindBinding("discord-user-1")!.CrossplatformId);
            Assert.DoesNotContain("ABCD-1234", bind.ToString(), StringComparison.Ordinal);

            var status = new DiscordInteractionEnvelope(
                "interaction-status", 2, "guild-1", "channel-public", "discord-user-1",
                false, "status", null);
            Assert.Equal(
                DiscordInboundDisposition.Dispatched,
                (await useCase.ExecuteAsync(status, CancellationToken.None)).Disposition);
            Assert.Equal(
                DiscordInboundDisposition.Duplicate,
                (await useCase.ExecuteAsync(status, CancellationToken.None)).Disposition);
            var command = Assert.Single(dispatcher.StatusCommands);
            Assert.Equal("player-1", command.CrossplatformId);

            var arbitrary = new DiscordInteractionEnvelope(
                "interaction-console", 2, "guild-1", "channel-public", "discord-user-1",
                false, "console", "say SECRET-CONTENT-SENTINEL");
            Assert.Equal(
                DiscordInboundDisposition.RejectedCommand,
                (await useCase.ExecuteAsync(arbitrary, CancellationToken.None)).Disposition);
            Assert.DoesNotContain("SECRET-CONTENT-SENTINEL", arbitrary.ToString(), StringComparison.Ordinal);
            Assert.Empty(dispatcher.PlayerCommands);

            var disabled = new DiscordInteractionEnvelope(
                "interaction-players", 2, "guild-1", "channel-public", "discord-user-1",
                false, "players", null);
            Assert.Equal(
                DiscordInboundDisposition.RejectedCommand,
                (await useCase.ExecuteAsync(disabled, CancellationToken.None)).Disposition);
            Assert.Equal(2, ScalarCount(database, "discord_interactions"));
            Assert.Equal(0, ScalarCount(database, "discord_interaction_secrets"));
        }

        [Fact]
        public void Interaction_work_items_persist_only_the_required_command_mapping_fields()
        {
            using var database = ConfiguredDatabase();

            Assert.Equal(
                new[]
                {
                    "binding_code_hash",
                    "channel_id",
                    "command_key",
                    "completed_utc",
                    "discord_subject",
                    "expires_utc",
                    "guild_id",
                    "interaction_id",
                    "status"
                },
                TableColumns(database, "discord_interactions")
                    .OrderBy(column => column, StringComparer.Ordinal));
        }

        [Fact]
        public async Task Deferred_interactions_are_accepted_once_claimed_recovered_as_result_unknown_without_retaining_tokens()
        {
            using var database = ConfiguredDatabase();
            database.Store.SaveCommandSetting(new DiscordCommandSetting("status", true, true));
            Bind(database.Store, "discord-user-1", "player-1");
            var dispatcher = new RecordingCommandDispatcher();
            var accept = new AcceptDiscordInteractionUseCase(database.Store, database.Store, () => Now);
            var process = new ProcessDiscordInteractionUseCase(
                database.Store, database.Store, dispatcher, () => Now);
            var interaction = new DiscordInteractionEnvelope(
                "deferred-status", 2, "guild-1", "channel-public", "discord-user-1",
                false, "status", null);

            Assert.Equal(
                DiscordInboundDisposition.Accepted,
                accept.Execute(interaction, "original-interaction-token").Disposition);
            Assert.Equal(
                DiscordInboundDisposition.Duplicate,
                accept.Execute(interaction, "replacement-interaction-token").Disposition);
            Assert.Empty(dispatcher.StatusCommands);
            Assert.Equal("original-interaction-token", database.Store.GetInteractionToken(
                "deferred-status", Now)!.TokenValue);

            var claimed = database.Store.TryClaimNextInteraction(Now);
            Assert.Equal(DiscordInteractionStatuses.Running, claimed!.Status);
            Assert.Null(database.Store.TryClaimNextInteraction(Now));
            Assert.Equal(1, process.RecoverRunningInteractions());

            Assert.Null(await process.ExecuteNextAsync(CancellationToken.None));
            Assert.Empty(dispatcher.StatusCommands);
            Assert.Null(database.Store.GetInteractionToken("deferred-status", Now));

            Assert.True(database.Store.TrySaveInteractionWithToken(
                new DiscordInteraction(
                    "expired-deferred", "status", DiscordInteractionStatuses.Pending, Now, null,
                    "guild-1", "channel-public", "discord-user-1"),
                "expired-interaction-token"));
            Assert.Null(database.Store.TryClaimNextInteraction(Now));
            Assert.Null(database.Store.GetInteractionToken("expired-deferred", Now));
        }

        [Fact]
        public void Deferred_interaction_processing_requires_a_private_response_sender()
        {
            var constructor = typeof(ProcessDiscordInteractionUseCase)
                .GetConstructors()
                .Single(candidate => candidate.GetParameters().Length == 5);

            Assert.Equal(
                "IDiscordInteractionResponseSender",
                constructor.GetParameters()[4].ParameterType.Name);
        }

        [Fact]
        public async Task Deferred_status_interactions_send_a_private_result_before_completing()
        {
            using var database = ConfiguredDatabase();
            database.Store.SaveCommandSetting(new DiscordCommandSetting("status", true, true));
            Bind(database.Store, "discord-user-1", "player-1");
            var dispatcher = new RecordingCommandDispatcher
            {
                StatusResponseContent = "Server is online"
            };
            var sender = new RecordingInteractionResponseSender();
            var accept = new AcceptDiscordInteractionUseCase(database.Store, database.Store, () => Now);
            var process = new ProcessDiscordInteractionUseCase(
                database.Store, database.Store, dispatcher, () => Now, sender);
            var interaction = new DiscordInteractionEnvelope(
                "response-status", 2, "guild-1", "channel-public", "discord-user-1",
                false, "status", null);

            Assert.Equal(
                DiscordInboundDisposition.Accepted,
                accept.Execute(interaction, "original-interaction-token").Disposition);

            Assert.Equal(
                DiscordInboundDisposition.Dispatched,
                (await process.ExecuteNextAsync(CancellationToken.None))!.Disposition);
            var response = Assert.Single(sender.Responses);
            Assert.Equal("app-1", response.ApplicationId);
            Assert.Equal("original-interaction-token", response.InteractionToken);
            Assert.Equal("Server is online", response.Content);
        }

        [Fact]
        public async Task Recovered_running_interactions_are_not_resent_after_a_private_follow_up_may_have_succeeded()
        {
            using var database = ConfiguredDatabase();
            database.Store.SaveCommandSetting(new DiscordCommandSetting("status", true, true));
            Bind(database.Store, "discord-user-1", "player-1");
            var dispatcher = new RecordingCommandDispatcher
            {
                StatusResponseContent = "Server is online"
            };
            var sender = new RecordingInteractionResponseSender();
            var accept = new AcceptDiscordInteractionUseCase(database.Store, database.Store, () => Now);
            var process = new ProcessDiscordInteractionUseCase(
                database.Store, database.Store, dispatcher, () => Now, sender);
            var interaction = new DiscordInteractionEnvelope(
                "crashed-after-follow-up", 2, "guild-1", "channel-public", "discord-user-1",
                false, "status", null);

            Assert.Equal(
                DiscordInboundDisposition.Accepted,
                accept.Execute(interaction, "original-interaction-token").Disposition);
            Assert.Equal(
                DiscordInteractionStatuses.Running,
                database.Store.TryClaimNextInteraction(Now)!.Status);

            // Model Discord accepting the private follow-up immediately before the process crashes.
            Assert.Equal(
                DiscordInteractionResponseDisposition.Succeeded,
                await sender.SendEphemeralAsync(
                    new DiscordInteractionResponse(
                        "app-1",
                        "original-interaction-token",
                        "Server is online",
                        null),
                    CancellationToken.None));

            Assert.Equal(1, process.RecoverRunningInteractions());

            Assert.Null(await process.ExecuteNextAsync(CancellationToken.None));
            Assert.Single(sender.Responses);
            Assert.Empty(dispatcher.StatusCommands);
            Assert.Null(database.Store.GetInteractionToken("crashed-after-follow-up", Now));
        }

        [Fact]
        public async Task Discord_messages_require_the_persisted_route_and_player_binding_ignore_bots_and_dedupe_without_content_retention()
        {
            using var database = ConfiguredDatabase();
            Bind(database.Store, "discord-user-1", "player-1");
            var sender = new RecordingChatSender();
            var useCase = new BridgeDiscordMessageToGameUseCase(
                database.Store,
                sender,
                () => Now,
                () => Guid.NewGuid().ToString("N"));

            Assert.Equal(
                DiscordInboundDisposition.IgnoredBot,
                (await useCase.ExecuteAsync(Message("self", "guild-1", "channel-public", "bot-self", true, false, "SELF-CONTENT"), CancellationToken.None)).Disposition);
            Assert.Equal(
                DiscordInboundDisposition.IgnoredBot,
                (await useCase.ExecuteAsync(Message("webhook", "guild-1", "channel-public", "hook", false, true, "WEBHOOK-CONTENT"), CancellationToken.None)).Disposition);
            Assert.Equal(
                DiscordInboundDisposition.IgnoredRoute,
                (await useCase.ExecuteAsync(Message("wrong-route", "guild-2", "channel-public", "discord-user-1", false, false, "WRONG-ROUTE-CONTENT"), CancellationToken.None)).Disposition);
            Assert.Equal(
                DiscordInboundDisposition.RejectedBinding,
                (await useCase.ExecuteAsync(Message("unbound", "guild-1", "channel-public", "discord-user-2", false, false, "UNBOUND-CONTENT"), CancellationToken.None)).Disposition);

            var inbound = Message(
                "message-1", "guild-1", "channel-public", "discord-user-1",
                false, false, "HELLO-FROM-DISCORD-SENTINEL");
            Assert.Equal(
                DiscordInboundDisposition.Forwarded,
                (await useCase.ExecuteAsync(inbound, CancellationToken.None)).Disposition);
            Assert.Equal(
                DiscordInboundDisposition.Duplicate,
                (await useCase.ExecuteAsync(inbound, CancellationToken.None)).Disposition);

            Assert.Equal("[Discord] HELLO-FROM-DISCORD-SENTINEL", Assert.Single(sender.GlobalMessages));
            Assert.DoesNotContain("HELLO-FROM-DISCORD-SENTINEL", inbound.ToString(), StringComparison.Ordinal);
            Assert.Equal(1, ScalarCount(database, "discord_bridge_messages"));
            Assert.Equal(
                new[] { "bridge_message_id", "expires_utc", "source", "source_message_id" },
                TableColumns(database, "discord_bridge_messages").OrderBy(column => column, StringComparer.Ordinal));
        }

        [Fact]
        public void Game_chat_is_enqueued_once_and_system_echoes_are_not_bridged_back()
        {
            using var database = ConfiguredDatabase();
            var useCase = new BridgeGameChatToDiscordUseCase(
                database.Store,
                () => Now,
                () => Guid.NewGuid().ToString("N"),
                () => Guid.NewGuid().ToString("N"),
                "public");
            var playerMessage = GameMessage(42, ChatSourceKind.Player, "Alice", "HELLO-FROM-GAME");

            var first = useCase.Execute(playerMessage);
            var duplicate = useCase.Execute(playerMessage);
            var echo = useCase.Execute(GameMessage(
                43, ChatSourceKind.System, "Discord", "[Discord] HELLO-FROM-DISCORD"));

            Assert.Equal(DiscordInboundDisposition.Enqueued, first.Disposition);
            Assert.Equal(DiscordInboundDisposition.Duplicate, duplicate.Disposition);
            Assert.Equal(DiscordInboundDisposition.IgnoredEcho, echo.Disposition);
            using var connection = database.ConnectionFactory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT business_key FROM discord_deliveries;";
            Assert.Equal("discord-bridge:game:42", command.ExecuteScalar());
            command.CommandText = "SELECT source_message_id FROM discord_bridge_messages WHERE source = 'Game';";
            Assert.Equal("42", command.ExecuteScalar());
        }

        [Fact]
        public async Task Inbound_runtime_has_an_idempotent_start_and_a_bounded_stop_gate()
        {
            using var database = ConfiguredDatabase();
            Bind(database.Store, "discord-user-1", "player-1");
            var sender = new RecordingChatSender();
            var dispatcher = new RecordingCommandDispatcher();
            using var runtime = new DiscordInboundRuntime(
                new BridgeDiscordMessageToGameUseCase(
                    database.Store, sender, () => Now, () => Guid.NewGuid().ToString("N")),
                new HandleDiscordInteractionUseCase(database.Store, dispatcher, () => Now),
                new BridgeGameChatToDiscordUseCase(
                    database.Store,
                    () => Now,
                    () => Guid.NewGuid().ToString("N"),
                    () => Guid.NewGuid().ToString("N"),
                    "public"));

            Assert.Equal(
                DiscordHealthState.Unavailable,
                runtime.GetHealth().Inbound.State);
            runtime.ObserveLoadedGatewayBotTokenFingerprint("loaded-fingerprint");
            Assert.True(runtime.GetHealth().IsGatewayBotTokenLoaded("loaded-fingerprint"));
            Assert.DoesNotContain("loaded-fingerprint", runtime.GetHealth().ToString());
            Assert.True(runtime.Start());
            Assert.False(runtime.Start());
            Assert.Equal(DiscordHealthState.Healthy, runtime.GetHealth().Inbound.State);
            Assert.Null(runtime.GetHealth().Inbound.ErrorCode);
            Assert.Equal(
                DiscordInboundDisposition.Forwarded,
                (await runtime.HandleMessageAsync(
                    Message("runtime-message", "guild-1", "channel-public", "discord-user-1", false, false, "runtime"),
                    CancellationToken.None)).Disposition);
            Assert.True(await runtime.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
            Assert.True(await runtime.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
            Assert.Equal(
                "discord_inbound_runtime_not_running",
                runtime.GetHealth().Inbound.ErrorCode);
            Assert.Equal(
                DiscordInboundDisposition.NotRunning,
                (await runtime.HandleMessageAsync(
                    Message("after-stop", "guild-1", "channel-public", "discord-user-1", false, false, "after"),
                    CancellationToken.None)).Disposition);
        }

        private static DiscordMessageCreateEnvelope Message(
            string messageId,
            string guildId,
            string channelId,
            string subject,
            bool authorIsBot,
            bool isWebhook,
            string content) =>
            new DiscordMessageCreateEnvelope(
                messageId, guildId, channelId, subject, authorIsBot, isWebhook, content);

        private static ChatMessage GameMessage(
            long sequence,
            ChatSourceKind sourceKind,
            string sender,
            string content) =>
            new ChatMessage
            {
                Sequence = sequence,
                OccurredAtUtc = Now,
                EntityId = 1,
                CrossplatformId = "player-1",
                SenderName = sender,
                Channel = ChatChannel.Global,
                SourceKind = sourceKind,
                Message = content
            };

        private static TemporaryDatabase ConfiguredDatabase()
        {
            var database = new TemporaryDatabase();
            database.Store.SaveSettings(new DiscordIntegrationSettings(
                1,
                true,
                DiscordIntegrationMode.Bot,
                "app-1",
                "guild-1",
                "channel-default",
                true,
                true,
                false,
                null,
                Now), expectedVersion: 0);
            database.Store.SaveTarget(new DiscordTarget("public", "Bot", "channel-public", true));
            return database;
        }

        private static void SaveBindingCode(
            SqliteDiscordIntegrationStore store,
            string crossplatformId,
            string code)
        {
            store.SaveBindingCode(new DiscordBindingCode(
                Guid.NewGuid().ToString("N"),
                crossplatformId,
                code.Substring(0, 4),
                DiscordBindingCodeHash.Compute(code),
                Now.AddMinutes(10)));
        }

        private static void Bind(
            SqliteDiscordIntegrationStore store,
            string discordSubject,
            string crossplatformId)
        {
            const string code = "BIND-1234";
            SaveBindingCode(store, crossplatformId, code);
            Assert.NotNull(store.TryConsumeBindingCode(
                DiscordBindingCodeHash.Compute(code),
                discordSubject,
                Now));
        }

        private static int ScalarCount(TemporaryDatabase database, string table)
        {
            using var connection = database.ConnectionFactory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private static IReadOnlyList<string> TableColumns(
            TemporaryDatabase database,
            string table)
        {
            using var connection = database.ConnectionFactory.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(" + table + ");";
            using var reader = command.ExecuteReader();
            var columns = new List<string>();
            while (reader.Read()) columns.Add(reader.GetString(1));
            return columns;
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingChatSender : IChatMessageSender
        {
            public List<string> GlobalMessages { get; } = new List<string>();

            public Task<ChatSendResult> SendGlobalAsync(
                string message,
                CancellationToken cancellationToken)
            {
                GlobalMessages.Add(message);
                return Task.FromResult(ChatSendResult.Accepted());
            }

            public Task<ChatSendResult> SendPrivateAsync(
                string targetCrossplatformId,
                string message,
                CancellationToken cancellationToken) =>
                Task.FromResult(ChatSendResult.Failed(ChatSendStatus.TargetOffline));
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingCommandDispatcher : IDiscordInboundCommandDispatcher
        {
            public string? StatusResponseContent { get; set; }
            public List<DiscordServerStatusCommand> StatusCommands { get; } =
                new List<DiscordServerStatusCommand>();
            public List<DiscordOnlinePlayersCommand> PlayerCommands { get; } =
                new List<DiscordOnlinePlayersCommand>();

            public Task<DiscordCommandDispatchResult> DispatchAsync(
                DiscordServerStatusCommand command,
                CancellationToken cancellationToken)
            {
                StatusCommands.Add(command);
                return Task.FromResult(DiscordCommandDispatchResult.Succeeded(StatusResponseContent));
            }

            public Task<DiscordCommandDispatchResult> DispatchAsync(
                DiscordOnlinePlayersCommand command,
                CancellationToken cancellationToken)
            {
                PlayerCommands.Add(command);
                return Task.FromResult(DiscordCommandDispatchResult.Succeeded());
            }
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingInteractionResponseSender : IDiscordInteractionResponseSender
        {
            public List<DiscordInteractionResponse> Responses { get; } =
                new List<DiscordInteractionResponse>();

            public Task<DiscordInteractionResponseDisposition> SendEphemeralAsync(
                DiscordInteractionResponse response,
                CancellationToken cancellationToken)
            {
                Responses.Add(response);
                return Task.FromResult(DiscordInteractionResponseDisposition.Succeeded);
            }
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "Application")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-discord-inbound-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase()
            {
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
                Store = new SqliteDiscordIntegrationStore(ConnectionFactory);
            }

            public SqliteConnectionFactory ConnectionFactory { get; }
            public SqliteDiscordIntegrationStore Store { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
