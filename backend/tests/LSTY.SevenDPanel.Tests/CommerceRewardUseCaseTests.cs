using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Rewards;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Rewards;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Rewards;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class CommerceRewardUseCaseTests
    {
        [Fact]
        public void Product_contract_rejects_negative_finite_stock()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShopProductDraft(
                "product-1",
                "Starter",
                string.Empty,
                true,
                25,
                -1,
                null,
                "package-1",
                0));
        }

        [Fact]
        public void Redeem_code_codec_accepts_only_sixteen_uppercase_ascii_characters()
        {
            Assert.Equal("ABCDEFGHJKLMNPQR", RedeemCodeCodec.Normalize("ABCD-EFGH-JKLM-NPQR"));
            Assert.Equal("****-****-****-NPQR", RedeemCodeCodec.Mask("ABCDEFGHJKLMNPQR"));
            Assert.Throws<ArgumentException>(() => RedeemCodeCodec.Normalize("abcd-efgh-jklm-npqr"));
            Assert.Throws<ArgumentException>(() => RedeemCodeCodec.Normalize("ＡBCD-EFGH-JKLM-NPQR"));
        }

        [Fact]
        public async Task Completed_purchase_captures_only_after_the_grant_completes()
        {
            using var database = new RewardTestDatabase();
            var fixture = CreateFixture(database, RewardDeliveryStatus.Succeeded);
            fixture.Economy.GetOrCreatePlayerAccount("EOS-player", "open-player", 100, Now);
            fixture.Commerce.SaveProduct(Product(stock: 1), Now);

            var result = await PurchaseUseCase(fixture).ExecuteAsync(
                Purchase("purchase-completed"),
                CancellationToken.None);

            Assert.Equal(PurchaseRequestStatus.Completed, result.Status);
            Assert.Equal(PurchaseState.Completed, result.Purchase!.State);
            Assert.NotNull(result.Purchase.GrantOperationId);
            Assert.Equal(0, fixture.Commerce.GetProduct("product-1").StockRemaining);
            var account = fixture.Economy.QueryAccounts(new AccountKeysetQuery(
                20, false, "EOS-player", null, null, null)).Accounts.Single();
            Assert.Equal(75, account.PostedBalance);
            Assert.Equal(0, account.ReservedDebit);
        }

        [Fact]
        public async Task Unknown_purchase_grant_keeps_funds_and_stock_reserved_without_redispatch()
        {
            using var database = new RewardTestDatabase();
            var fixture = CreateFixture(database, RewardDeliveryStatus.ResultUnknown);
            fixture.Economy.GetOrCreatePlayerAccount("EOS-player", "open-player", 100, Now);
            fixture.Commerce.SaveProduct(Product(stock: 1), Now);
            var useCase = PurchaseUseCase(fixture);

            var first = await useCase.ExecuteAsync(Purchase("purchase-unknown"), CancellationToken.None);
            var replay = await useCase.ExecuteAsync(Purchase("purchase-unknown"), CancellationToken.None);

            Assert.Equal(PurchaseRequestStatus.PendingReconciliation, first.Status);
            Assert.Equal(PurchaseRequestStatus.PendingReconciliation, replay.Status);
            Assert.Equal(1, fixture.Delivery.Calls);
            Assert.Equal(0, fixture.Commerce.GetProduct("product-1").StockRemaining);
            var account = fixture.Economy.QueryAccounts(new AccountKeysetQuery(
                20, false, "EOS-player", null, null, null)).Accounts.Single();
            Assert.Equal(100, account.PostedBalance);
            Assert.Equal(25, account.ReservedDebit);
        }

        [Fact]
        public async Task Frozen_account_is_rejected_before_reward_dispatch()
        {
            using var database = new RewardTestDatabase();
            var fixture = CreateFixture(database, RewardDeliveryStatus.Succeeded);
            var account = fixture.Economy.GetOrCreatePlayerAccount(
                "EOS-player", "open-player", 100, Now);
            fixture.Economy.SetFrozen(account.AccountId, true, account.RowVersion, Now.AddSeconds(1));
            fixture.Commerce.SaveProduct(Product(stock: null), Now);

            var result = await PurchaseUseCase(fixture).ExecuteAsync(
                Purchase("purchase-frozen"),
                CancellationToken.None);

            Assert.Equal(PurchaseRequestStatus.AccountFrozen, result.Status);
            Assert.Null(result.Purchase);
            Assert.Equal(0, fixture.Delivery.Calls);
        }

        [Fact]
        public async Task Generated_redeem_code_is_stored_only_as_digest_and_replay_is_authoritative()
        {
            using var database = new RewardTestDatabase();
            var fixture = CreateFixture(database, RewardDeliveryStatus.Succeeded);
            var create = new CreateRedeemCodeUseCase(
                fixture.Commerce,
                () => "ABCD-EFGH-JKLM-NPQR",
                () => "code-1",
                () => Now);
            var generated = create.Execute(new CreateRedeemCodeCommand(
                "starter-package", true, null, null, 10, 1));
            var redeem = new RedeemCodeUseCase(
                fixture.Commerce,
                fixture.Grant,
                () => "redeem-attempt-1",
                () => Now);
            var command = new RedeemCodeCommand(
                generated.PlaintextCode,
                "EOS-player",
                42,
                "world-1",
                "redeem-correlation");

            var first = await redeem.ExecuteAsync(command, CancellationToken.None);
            var replay = await redeem.ExecuteAsync(command, CancellationToken.None);

            Assert.Equal(RedeemRequestStatus.Succeeded, first.Status);
            Assert.Equal(first.Attempt!.AttemptId, replay.Attempt!.AttemptId);
            Assert.Equal(1, fixture.Delivery.Calls);
            Assert.Equal("****-****-****-NPQR", generated.Definition.MaskedCode);
            Assert.DoesNotContain(
                typeof(RedeemCodeSnapshot).GetProperties(),
                property => property.Name.IndexOf("digest", StringComparison.OrdinalIgnoreCase) >= 0);
            using var connection = database.ConnectionFactory.Open();
            var stored = connection.QuerySingle<dynamic>(
                "SELECT normalized_code_digest, masked_prefix FROM redeem_codes WHERE code_id = 'code-1';");
            Assert.Equal(64, ((string)stored.normalized_code_digest).Length);
            Assert.Equal("NPQR", (string)stored.masked_prefix);
            Assert.DoesNotContain("ABCDEFGHJKLMNPQR", (string)stored.normalized_code_digest);
        }

        [Fact]
        public async Task Achievement_progress_is_monotonic_and_replayed_observations_grant_once()
        {
            using var database = new RewardTestDatabase();
            var fixture = CreateFixture(database, RewardDeliveryStatus.Succeeded);
            fixture.Commerce.SaveAchievement(new AchievementDefinitionDraft(
                "level-10", "Level 10", string.Empty, AchievementStatistic.Level,
                10, "starter-package", true, 0), Now);
            var useCase = new ObserveAchievementUseCase(fixture.Commerce, fixture.Grant);

            await useCase.ExecuteAsync(Observation("event-1", 9), CancellationToken.None);
            await useCase.ExecuteAsync(Observation("event-2", 10), CancellationToken.None);
            await useCase.ExecuteAsync(Observation("event-1", 9), CancellationToken.None);

            var progress = fixture.Commerce.GetAchievementProgress("level-10", "EOS-player");
            Assert.Equal(10, progress.CurrentValue);
            Assert.Equal("achievement:level-10:10", progress.EligibilityKey);
            Assert.Equal(1, fixture.Delivery.Calls);
        }

        [Fact]
        public async Task Online_reward_subtracts_paused_gaps_and_marks_incomplete_rules()
        {
            using var database = new RewardTestDatabase();
            var fixture = CreateFixture(database, RewardDeliveryStatus.Succeeded);
            fixture.Commerce.SaveOnlineRewardRule(new OnlineRewardRuleDraft(
                "paused-45", "Paused 45", TimeSpan.FromMinutes(45), null,
                EvidenceGapPolicy.Paused, "starter-package", true, 0), Now);
            fixture.Commerce.SaveOnlineRewardRule(new OnlineRewardRuleDraft(
                "paused-30", "Paused 30", TimeSpan.FromMinutes(30), null,
                EvidenceGapPolicy.Paused, "starter-package", true, 1), Now);
            fixture.Commerce.SaveOnlineRewardRule(new OnlineRewardRuleDraft(
                "incomplete", "Incomplete", TimeSpan.FromMinutes(1), null,
                EvidenceGapPolicy.Incomplete, "starter-package", true, 2), Now);
            InsertSessionAndGap(database, Utc(10), Utc(11), Utc(10, 20), Utc(10, 50));

            var result = await new EvaluateOnlineRewardsUseCase(
                fixture.Commerce,
                fixture.Grant).ExecuteAsync(
                    new EvaluateOnlineRewardsCommand(
                        "EOS-player", 42, "world-1", Utc(11), "online-evaluation"),
                    CancellationToken.None);

            Assert.Contains(result, value => value.RuleId == "paused-30" &&
                value.State == RewardEligibilityState.Granted);
            Assert.DoesNotContain(result, value => value.RuleId == "paused-45");
            Assert.Contains(
                fixture.Commerce.ListEligibilities("OnlineReward", "incomplete", "EOS-player"),
                value => value.State == RewardEligibilityState.Incomplete);
            Assert.Equal(1, fixture.Delivery.Calls);
        }

        [Fact]
        public void Reward_evidence_runtime_forwards_typed_evidence_synchronously_and_unsubscribes()
        {
            Action<PlayerSnapshot>? scalarHandler = null;
            Action<PlayerSession>? sessionHandler = null;
            PlayerSnapshot? queuedScalar = null;
            PlayerSession? queuedSession = null;
            var scalarSubscription = new RecordingDisposable();
            var sessionSubscription = new RecordingDisposable();
            var inner = new RecordingModRuntime();
            using var runtime = new RewardEvidenceRuntime(
                handler =>
                {
                    scalarHandler = handler;
                    return scalarSubscription;
                },
                handler =>
                {
                    sessionHandler = handler;
                    return sessionSubscription;
                },
                snapshot => queuedScalar = snapshot,
                session => queuedSession = session,
                inner);

            runtime.Start();
            var snapshot = Snapshot();
            var session = new PlayerSession(
                1,
                "EOS-player",
                "server-1",
                "world-1",
                Now,
                Now.AddMinutes(5),
                "Disconnected",
                null,
                PlayerProfileSectionState.Available);

            scalarHandler!(snapshot);
            sessionHandler!(session);

            Assert.Same(snapshot, queuedScalar);
            Assert.Same(session, queuedSession);
            Assert.True(inner.Started);

            runtime.Stop();

            Assert.True(scalarSubscription.Disposed);
            Assert.True(sessionSubscription.Disposed);
            Assert.True(inner.Stopped);
        }

        [Fact]
        public void Reward_evidence_runtime_maps_only_a_confirmed_matching_identity_to_stable_commands()
        {
            Action<PlayerSnapshot>? scalarHandler = null;
            Action<PlayerSession>? sessionHandler = null;
            var achievements = new List<ObserveAchievementCommand>();
            var onlineEvaluations = new List<EvaluateOnlineRewardsCommand>();
            var inner = new RecordingModRuntime();
            using var runtime = new RewardEvidenceRuntime(
                handler =>
                {
                    scalarHandler = handler;
                    return new RecordingDisposable();
                },
                handler =>
                {
                    sessionHandler = handler;
                    return new RecordingDisposable();
                },
                (command, _) =>
                {
                    achievements.Add(command);
                    return Task.CompletedTask;
                },
                (command, _) =>
                {
                    onlineEvaluations.Add(command);
                    return Task.CompletedTask;
                },
                inner,
                _ => { });

            runtime.Start();
            scalarHandler!(Snapshot(includeCrossplatformIdentity: false));
            scalarHandler!(Snapshot());
            sessionHandler!(new PlayerSession(
                1,
                "another-player",
                "server-1",
                "world-1",
                Now.AddMinutes(-1),
                null,
                null,
                null,
                PlayerProfileSectionState.Available));
            Assert.Empty(achievements);
            Assert.Empty(onlineEvaluations);

            var matchingSession = new PlayerSession(
                2,
                "EOS-player",
                "server-1",
                "world-1",
                Now.AddMinutes(-1),
                null,
                null,
                null,
                PlayerProfileSectionState.Available);
            sessionHandler(matchingSession);
            scalarHandler(Snapshot());
            runtime.Stop();

            Assert.Equal(8, achievements.Count);
            Assert.Equal(
                new[]
                {
                    AchievementStatistic.Level,
                    AchievementStatistic.ZombieKills,
                    AchievementStatistic.PlayerKills,
                    AchievementStatistic.Deaths
                },
                achievements.Take(4).Select(command => command.Statistic));
            Assert.Equal(
                new long[] { 10, 20, 1, 2 },
                achievements.Take(4).Select(command => command.Value));
            Assert.All(achievements, command =>
            {
                Assert.Equal("EOS-player", command.CrossplatformId);
                Assert.Equal(42, command.ExpectedEntityId);
                Assert.Equal("world-1", command.ExpectedWorldId);
            });
            Assert.Equal(
                achievements.Take(4).Select(command => command.EvidenceId),
                achievements.Skip(4).Select(command => command.EvidenceId));
            Assert.Equal(
                achievements.Take(4).Select(command => command.CorrelationId),
                achievements.Skip(4).Select(command => command.CorrelationId));

            Assert.Equal(2, onlineEvaluations.Count);
            Assert.All(onlineEvaluations, command =>
            {
                Assert.Equal("EOS-player", command.CrossplatformId);
                Assert.Equal(42, command.ExpectedEntityId);
                Assert.Equal("world-1", command.ExpectedWorldId);
                Assert.Equal(Now, command.EvidenceToUtc);
            });
            Assert.Equal(onlineEvaluations[0].CorrelationId, onlineEvaluations[1].CorrelationId);
        }

        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        private static ShopProductDraft Product(long? stock) => new ShopProductDraft(
            "product-1", "Starter", string.Empty, true, 25, stock, 1,
            "starter-package", 0);

        private static PurchaseProductCommand Purchase(string idempotencyKey) =>
            new PurchaseProductCommand(
                "product-1", "EOS-player", 42, "world-1", 1,
                idempotencyKey, idempotencyKey + "-correlation");

        private static PurchaseProductUseCase PurchaseUseCase(Fixture fixture) =>
            new PurchaseProductUseCase(
                fixture.Commerce,
                fixture.Grant,
                () => "purchase-1",
                () => "purchase-reservation-1",
                () => "purchase-capture-1",
                () => Now);

        private static ObserveAchievementCommand Observation(string evidenceId, long value) =>
            new ObserveAchievementCommand(
                evidenceId,
                "EOS-player",
                42,
                "world-1",
                AchievementStatistic.Level,
                value,
                evidenceId + "-correlation",
                Now);

        private static PlayerSnapshot Snapshot(bool includeCrossplatformIdentity = true) => new PlayerSnapshot(
            42,
            "Player",
            new PlayerPlatformIdentity("EOS-player", "EOS"),
            includeCrossplatformIdentity
                ? new PlayerPlatformIdentity("EOS-player", "EOS")
                : null,
            PlayerDeviceType.Windows,
            null,
            10,
            null,
            null,
            0,
            new PlayerPosition(0, 0, 0),
            false,
            100,
            100,
            10,
            0,
            20,
            1,
            2,
            60,
            0,
            0,
            10,
            5,
            Now);

        private static Fixture CreateFixture(
            RewardTestDatabase database,
            RewardDeliveryStatus status)
        {
            var rewardStore = new SqliteRewardStore(database.ConnectionFactory);
            var economy = new SqliteEconomyLedgerStore(database.ConnectionFactory);
            var catalog = RewardTestCatalog.Available();
            new SaveRewardPackageUseCase(rewardStore, catalog).Execute(new RewardPackageDraft(
                "starter-package",
                "Starter Package",
                string.Empty,
                true,
                0,
                new[]
                {
                    RewardPackageEntryDraft.Item(
                        "starter-item", "medicalBandage", GameResourceKind.Item,
                        1, null, null, "catalog-v1")
                }));
            var delivery = new RecordingRewardDeliveryPort(command => status switch
            {
                RewardDeliveryStatus.Succeeded => RewardDeliveryResult.Succeeded(
                    command.Entries.Select(entry => RewardDeliveryEntryResult.Succeeded(
                        entry.OperationEntryId, "delivery-" + entry.OperationEntryId))),
                RewardDeliveryStatus.Failed => RewardDeliveryResult.Failed(
                    Array.Empty<RewardDeliveryEntryResult>(), "reward_pre_dispatch_failed"),
                _ => RewardDeliveryResult.ResultUnknown(
                    command.Entries.Select(entry => RewardDeliveryEntryResult.ResultUnknown(
                        entry.OperationEntryId, null, "ResultUnknown")))
            });
            var grant = new GrantRewardUseCase(rewardStore, delivery, economy, catalog);
            return new Fixture(
                new SqliteCommerceStore(database.ConnectionFactory),
                economy,
                grant,
                delivery);
        }

        private static void InsertSessionAndGap(
            RewardTestDatabase database,
            DateTimeOffset started,
            DateTimeOffset ended,
            DateTimeOffset gapStarted,
            DateTimeOffset gapEnded)
        {
            using var connection = database.ConnectionFactory.Open();
            connection.Execute(
                @"INSERT INTO player_sessions (
                      id, crossplatform_id, server_id, world_id, started_at_utc,
                      ended_at_utc, end_reason, last_x, last_y, last_z, completeness)
                  VALUES (9001, 'EOS-player', 'server-1', 'world-1', @Started,
                      @Ended, 'Disconnected', NULL, NULL, NULL, 'Available');
                  INSERT INTO player_history_gaps (
                      gap_id, crossplatform_id, started_utc, completed_utc,
                      dropped_count, reason, recorded_utc)
                  VALUES ('gap-1', 'EOS-player', @GapStarted, @GapEnded,
                      1, 'queue_full', @GapEnded);",
                new
                {
                    Started = started.ToUnixTimeMilliseconds(),
                    Ended = ended.ToUnixTimeMilliseconds(),
                    GapStarted = gapStarted.ToUnixTimeMilliseconds(),
                    GapEnded = gapEnded.ToUnixTimeMilliseconds()
                });
        }

        private static DateTimeOffset Utc(int hour, int minute = 0) =>
            new DateTimeOffset(2026, 7, 27, hour, minute, 0, TimeSpan.Zero);

        private sealed class RecordingRewardDeliveryPort : IRewardDeliveryPort
        {
            private readonly Func<RewardDeliveryCommand, RewardDeliveryResult> deliver;

            public RecordingRewardDeliveryPort(Func<RewardDeliveryCommand, RewardDeliveryResult> deliver) =>
                this.deliver = deliver;

            public int Calls { get; private set; }

            public Task<RewardDeliveryResult> DeliverAsync(
                RewardDeliveryCommand command,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                return Task.FromResult(deliver(command));
            }
        }

        private sealed class Fixture
        {
            public Fixture(
                SqliteCommerceStore commerce,
                SqliteEconomyLedgerStore economy,
                GrantRewardUseCase grant,
                RecordingRewardDeliveryPort delivery)
            {
                Commerce = commerce;
                Economy = economy;
                Grant = grant;
                Delivery = delivery;
            }

            public SqliteCommerceStore Commerce { get; }
            public SqliteEconomyLedgerStore Economy { get; }
            public GrantRewardUseCase Grant { get; }
            public RecordingRewardDeliveryPort Delivery { get; }
        }

        private sealed class RecordingDisposable : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }

        private sealed class RecordingModRuntime : IModRuntime
        {
            public bool Started { get; private set; }
            public bool Stopped { get; private set; }

            public void Start() => Started = true;

            public void MarkGameReady()
            {
            }

            public void Stop() => Stopped = true;
        }
    }
}
