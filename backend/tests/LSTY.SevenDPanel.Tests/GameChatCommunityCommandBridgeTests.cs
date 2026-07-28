using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Rewards;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Community;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class GameChatCommunityCommandBridgeTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 5, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Daily_command_uses_the_fixed_daily_rule_id()
        {
            var create = typeof(CommunityGameCommandConsumerSet).GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(create);
            Assert.DoesNotContain(create!.GetParameters(), parameter =>
                string.Equals(parameter.Name, "dailyCommandRuleId", StringComparison.Ordinal));

            using var fixture = new DailyFixture("daily");

            var consumers = Assert.IsAssignableFrom<IReadOnlyList<ICommunityGameCommandConsumer>>(
                create.Invoke(null, CreateArguments(create, fixture)));
            var result = consumers.Single(consumer => consumer.Command == CommunityGameCommandId.Daily)
                .Execute(new CommunityGameCommandContext("EOS-A", "Alice", Array.Empty<string>()));

            Assert.Equal(CommunityCommandConsumerStatus.Succeeded, result.Status);
            Assert.Equal(1, fixture.Delivery.Calls);
        }

        [Fact]
        public void Incoming_global_daily_chat_invokes_the_community_daily_application_port()
        {
            using var fixture = new DailyFixture("daily");
            var catalog = new GameChatCommandCatalog(
                CommunityGameChatCommandHandlerSet.Create(new CommunityGameCommandRouter(
                    CreateConsumers(fixture))));
            var state = new ChatRuntimeState(new ChatSettingsStore(), new ColoredChatStore());
            state.Load();
            var liveWindow = new ServerEventLiveWindow(8);
            var historyWriter = new ChatHistoryWriteService(new HistoryStore());
            var coordinator = new SevenDaysChatMessageCoordinator(
                state,
                new ColoredChatRenderer(),
                liveWindow,
                new ServerEventHub(liveWindow),
                historyWriter,
                _ => { },
                catalog,
                new SevenDaysGameChatCommandReplySender());
            var result = HandleIncomingGlobalChat(coordinator, "EOS-A", "/daily", "Alice");

            Assert.Equal("StopHandlersAndVanilla", result.ToString());
            Assert.Equal(1, fixture.Delivery.Calls);
            historyWriter.Dispose();
        }

        private static IReadOnlyList<ICommunityGameCommandConsumer> CreateConsumers(
            DailyFixture fixture)
        {
            var create = typeof(CommunityGameCommandConsumerSet).GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static)!;
            return Assert.IsAssignableFrom<IReadOnlyList<ICommunityGameCommandConsumer>>(
                create.Invoke(null, CreateArguments(create, fixture)));
        }

        private static object?[] CreateArguments(
            MethodInfo create,
            DailyFixture fixture) =>
            create.GetParameters().Select(parameter => CreateArgument(
                parameter,
                fixture)).ToArray();

        private static object? CreateArgument(
            ParameterInfo parameter,
            DailyFixture fixture)
        {
            switch (parameter.Name)
            {
                case "openAccount": return Unused<OpenPlayerAccountUseCase>();
                case "transferBalance": return Unused<TransferBalanceUseCase>();
                case "queryAccounts": return Unused<QueryEconomyAccountsUseCase>();
                case "browseShop": return Unused<BrowseShopUseCase>();
                case "purchaseProduct": return Unused<PurchaseProductUseCase>();
                case "redeemCode": return Unused<RedeemCodeUseCase>();
                case "dailyRewards": return fixture.Claims;
                case "homes": return Unused<HomeUseCases>();
                case "cities": return Unused<CityUseCases>();
                case "teleports": return Unused<TeleportUseCases>();
                case "teleportFriendRequests": return Unused<TeleportFriendRequestUseCases>();
                case "players": return fixture.Players;
                case "voteKick": return new UnusedCommandConsumer(CommunityGameCommandId.VoteKick);
                case "voteRestart": return new UnusedCommandConsumer(CommunityGameCommandId.VoteRestart);
                case "isEnabled": return new Func<CommunityGameCommandId, bool>(_ => true);
                case "utcClock": return new Func<DateTimeOffset>(() => Now);
                case "idFactory": return new Func<string>(() => "test-command");
                default: throw new InvalidOperationException(
                    "Unexpected Create parameter: " + parameter.Name);
            }
        }

        private static T Unused<T>() where T : class =>
            (T)FormatterServices.GetUninitializedObject(typeof(T));

        private static object HandleIncomingGlobalChat(
            SevenDaysChatMessageCoordinator coordinator,
            string crossplatformId,
            string message,
            string displayName)
        {
            var dataType = GameType("ModEvents+SChatMessageData");
            var chatType = GameType("EChatType");
            var data = Activator.CreateInstance(
                dataType,
                Client(crossplatformId),
                Enum.Parse(chatType, "Global"),
                7,
                message,
                displayName,
                new List<int>());
            var handle = typeof(SevenDaysChatMessageCoordinator).GetMethod("Handle")!;

            return handle.Invoke(coordinator, new[] { data })!;
        }

        private static object Client(string crossplatformId)
        {
            var clientType = GameType("ClientInfo");
            var client = FormatterServices.GetUninitializedObject(clientType);
            var identifierType = GameType("Platform.Local.UserIdentifierLocal");
            var identifier = Activator.CreateInstance(identifierType, crossplatformId);
            clientType.GetField("CrossplatformId")!.SetValue(client, identifier);
            return client;
        }

        private static Type GameType(string name)
        {
            LoadGameAssembly();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(name, false);
                if (type != null) return type;
            }

            throw new InvalidOperationException("The Seven Days game type was not loaded: " + name);
        }

        private static void LoadGameAssembly()
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                    string.Equals(assembly.GetName().Name, "Assembly-CSharp", StringComparison.Ordinal)))
                return;

            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                var assemblyPath = Path.Combine(
                    directory.FullName,
                    "7dtd-reference",
                    "v3.0.1-b4",
                    "runtime",
                    "7DaysToDieServer_Data",
                    "Managed",
                    "Assembly-CSharp.dll");
                if (File.Exists(assemblyPath))
                {
                    Assembly.LoadFrom(assemblyPath);
                    return;
                }
            }
        }

        private sealed class DailyFixture : IDisposable
        {
            private readonly RewardTestDatabase database = new RewardTestDatabase();

            public DailyFixture(string ruleId)
            {
                var rewards = new SqliteRewardStore(database.ConnectionFactory);
                var commerce = new SqliteCommerceStore(database.ConnectionFactory);
                var economy = new SqliteEconomyLedgerStore(database.ConnectionFactory);
                var catalog = RewardTestCatalog.Available();
                new SaveRewardPackageUseCase(rewards, catalog).Execute(new RewardPackageDraft(
                    "daily-package",
                    "Daily",
                    string.Empty,
                    true,
                    0,
                    new[] { RewardPackageEntryDraft.Currency("daily-currency", 5) }));
                new SaveDailyRewardPolicyUseCase(commerce, rewards).Execute(
                    new DailyRewardPolicyDraft(ruleId, "daily-package", true, null));
                Delivery = new RecordingDelivery();
                Claims = new ClaimDailyRewardUseCase(
                    commerce,
                    new GrantRewardUseCase(rewards, Delivery, economy, catalog),
                    commerce,
                    () => Now,
                    () => "daily-claim");
                Players = new FixedPlayers(new CommunityPlayerCommandSnapshot(
                    "Alice",
                    new TeleportPlayerSnapshot(
                        "EOS-A",
                        7,
                        new WorldPosition("world-1", 1, 65, 2, 0),
                        true,
                        true,
                        true,
                        false,
                        false,
                        new WorldBounds(-1000, 1000, -1000, 1000)),
                    TimeSpan.Zero));
            }

            public ClaimDailyRewardUseCase Claims { get; }
            public RecordingDelivery Delivery { get; }
            public FixedPlayers Players { get; }

            public void Dispose() => database.Dispose();
        }

        private sealed class RecordingDelivery : IRewardDeliveryPort
        {
            private int calls;

            public int Calls => Volatile.Read(ref calls);

            public Task<RewardDeliveryResult> DeliverAsync(
                RewardDeliveryCommand command,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref calls);
                return Task.FromResult(RewardDeliveryResult.Succeeded(
                    Array.Empty<RewardDeliveryEntryResult>()));
            }
        }

        private sealed class FixedPlayers : ICommunityPlayerCommandSnapshotProvider
        {
            private readonly CommunityPlayerCommandSnapshot player;

            public FixedPlayers(CommunityPlayerCommandSnapshot player) => this.player = player;

            public CommunityPlayerCommandSnapshot? FindOnlineByCrossplatformId(string crossplatformId) =>
                string.Equals(player.CrossplatformId, crossplatformId, StringComparison.Ordinal)
                    ? player
                    : null;

            public CommunityPlayerCommandSnapshot? ResolveOnline(string selector) =>
                string.Equals(player.CrossplatformId, selector, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(player.DisplayName, selector, StringComparison.OrdinalIgnoreCase)
                    ? player
                    : null;

            public IReadOnlyList<CommunityPlayerCommandSnapshot> CaptureOnline() => new[] { player };
        }

        private sealed class UnusedCommandConsumer : ICommunityGameCommandConsumer
        {
            public UnusedCommandConsumer(CommunityGameCommandId command) => Command = command;

            public CommunityGameCommandId Command { get; }
            public bool IsEnabled => true;
            public CommunityCommandConsumerResult Execute(CommunityGameCommandContext context) =>
                CommunityCommandConsumerResult.Succeeded();
        }

        private sealed class ChatSettingsStore : IChatSettingsStore
        {
            private static readonly ChatSettings Settings = new ChatSettings
            {
                IsEnabled = true,
                CommandPrefixes = new[] { "/" },
                ExcludeCommandsFromHistory = true,
                HistoryRetentionDays = 30
            };

            public ChatSettings Get() => Settings;
            public ChatSettings Save(ChatSettings settings) => settings;
            public ChatSettings Reset() => Settings;
        }

        private sealed class ColoredChatStore : IColoredChatStore
        {
            private static readonly ColoredChatSettings Settings = new ColoredChatSettings
            {
                IsEnabled = false,
                PlayerColorTagPermission = PlayerColorTagPermission.None
            };

            public ColoredChatSettings GetSettings() => Settings;
            public ColoredChatSettings SaveSettings(ColoredChatSettings settings) => settings;
            public ColoredChatSettings ResetSettings() => Settings;
            public ColoredChatProfilePage GetProfiles(ColoredChatProfileQuery query) =>
                throw new NotSupportedException();
            public IReadOnlyList<ColoredChatProfile> GetAllProfiles() =>
                Array.Empty<ColoredChatProfile>();
            public bool TryCreateProfile(ColoredChatProfile profile) => false;
            public bool TryUpdateProfile(ColoredChatProfile profile) => false;
            public bool TryDeleteProfile(string crossplatformId) => false;
        }

        private sealed class HistoryStore : IChatHistoryStore
        {
            public void Append(ChatMessage message) { }
            public void AppendGap(ChatHistoryGap gap) { }
            public ChatHistoryPage GetHistory(ChatHistoryQuery query) =>
                throw new NotSupportedException();
            public int DeleteBefore(DateTimeOffset cutoffUtc, int maximumDeletes) => 0;
        }

    }
}
