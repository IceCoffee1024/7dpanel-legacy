using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Community;

namespace LSTY.SevenDPanel.Tests
{
    internal sealed class VirtualCommunityChatCommandTestHarness : IDisposable
    {
        public const string AliceId = "VIRTUAL-EOS-ALICE";
        public const string BobId = "VIRTUAL-EOS-BOB";

        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero);

        private readonly RewardTestDatabase database = new RewardTestDatabase();
        private int nextId;

        public VirtualCommunityChatCommandTestHarness(long aliceOpeningBalance = 100)
        {
            var ledger = new SqliteEconomyLedgerStore(database.ConnectionFactory);
            ledger.GetOrCreatePlayerAccount(
                AliceId,
                "virtual-alice-opening",
                aliceOpeningBalance,
                Now);

            var consumers = CommunityGameCommandConsumerSet.Create(
                new OpenPlayerAccountUseCase(ledger),
                new TransferBalanceUseCase(ledger),
                new QueryEconomyAccountsUseCase(ledger),
                Unused<BrowseShopUseCase>(),
                Unused<PurchaseProductUseCase>(),
                Unused<RedeemCodeUseCase>(),
                Unused<ClaimDailyRewardUseCase>(),
                Unused<HomeUseCases>(),
                Unused<CityUseCases>(),
                Unused<TeleportUseCases>(),
                Unused<TeleportFriendRequestUseCases>(),
                new VirtualPlayers(Player(BobId, "VirtualBob", 2)),
                new UnusedVoteConsumer(CommunityGameCommandId.VoteKick),
                new UnusedVoteConsumer(CommunityGameCommandId.VoteRestart),
                _ => true,
                () => Now,
                NextId);
            Catalog = new GameChatCommandCatalog(
                CommunityGameChatCommandHandlerSet.Create(
                    new CommunityGameCommandRouter(consumers)));
        }

        public GameChatCommandCatalog Catalog { get; }

        public static GameChatCommandContext Context(
            string crossplatformId,
            string displayName,
            params string[] arguments) =>
            new GameChatCommandContext(crossplatformId, displayName, arguments);

        public void Dispose() => database.Dispose();

        private string NextId() =>
            "virtual-command-" + Interlocked.Increment(ref nextId);

        private static T Unused<T>() where T : class =>
            (T)FormatterServices.GetUninitializedObject(typeof(T));

        private static CommunityPlayerCommandSnapshot Player(
            string crossplatformId,
            string displayName,
            int entityId) =>
            new CommunityPlayerCommandSnapshot(
                displayName,
                new TeleportPlayerSnapshot(
                    crossplatformId,
                    entityId,
                    new WorldPosition("virtual-world", entityId, 70, entityId, 0),
                    true,
                    true,
                    true,
                    false,
                    false,
                    new WorldBounds(-1000, 1000, -1000, 1000)),
                TimeSpan.FromMinutes(10));

        private sealed class VirtualPlayers : ICommunityPlayerCommandSnapshotProvider
        {
            private readonly CommunityPlayerCommandSnapshot player;

            public VirtualPlayers(CommunityPlayerCommandSnapshot player) => this.player = player;

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

        private sealed class UnusedVoteConsumer : ICommunityGameCommandConsumer
        {
            public UnusedVoteConsumer(CommunityGameCommandId command) => Command = command;

            public CommunityGameCommandId Command { get; }
            public bool IsEnabled => true;
            public CommunityCommandConsumerResult Execute(CommunityGameCommandContext context) =>
                CommunityCommandConsumerResult.Succeeded();
        }
    }
}
