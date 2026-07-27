using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysGrantItemGatewayTests
    {
        private static readonly DateTimeOffset ObservedAt =
            new DateTimeOffset(2026, 7, 27, 1, 2, 3, TimeSpan.Zero);
        private static readonly DateTimeOffset StartedAt =
            new DateTimeOffset(2026, 7, 27, 2, 3, 4, TimeSpan.Zero);

        [Fact]
        public async Task Revalidation_start_and_approved_bag_commit_run_inside_dispatcher()
        {
            var insideDispatcher = false;
            var commits = 0;
            var catalog = Catalog(() => Assert.True(insideDispatcher));
            var context = Context(
                Target(),
                approvedCapacity: 10,
                commit: () =>
                {
                    Assert.True(insideDispatcher);
                    commits++;
                    return 5;
                });
            var gateway = Gateway(
                catalog,
                captureContext: _ =>
                {
                    Assert.True(insideDispatcher);
                    return context;
                },
                dispatcher: (name, action, timeout, token) =>
                {
                    Assert.Equal("7DPanel.Players.GrantItem", name);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    insideDispatcher = true;
                    try { return Task.FromResult(action()); }
                    finally { insideDispatcher = false; }
                });

            var result = await gateway.GrantAsync(
                Command(),
                startedAt =>
                {
                    Assert.True(insideDispatcher);
                    Assert.Equal(StartedAt, startedAt);
                    return true;
                },
                CancellationToken.None);

            Assert.Equal(GrantItemGatewayStatus.Succeeded, result.Status);
            Assert.Equal(5, result.ActualQuantity);
            Assert.Equal(1, commits);
        }

        [Theory]
        [InlineData("offline")]
        [InlineData("combined-id")]
        [InlineData("entity-id")]
        [InlineData("world-id")]
        [InlineData("observed-stamp")]
        public async Task Offline_or_replaced_target_is_rejected_before_commit(string change)
        {
            var starts = 0;
            var commits = 0;
            GrantItemRuntimeContext? context = change == "offline"
                ? null
                : Context(ChangedTarget(change), 10, () => ++commits);
            var gateway = Gateway(Catalog(), _ => context);

            var result = await gateway.GrantAsync(
                Command(),
                _ =>
                {
                    starts++;
                    return true;
                },
                CancellationToken.None);

            Assert.Equal(GrantItemGatewayStatus.Rejected, result.Status);
            Assert.Equal(0, commits);
            Assert.Equal(0, starts);
            Assert.Equal(
                change == "offline"
                    ? GrantItemFailureCodes.PlayerNotOnline
                    : GrantItemFailureCodes.TargetChanged,
                result.FailureCode);
        }

        [Theory]
        [InlineData("catalog-version")]
        [InlineData("resource-id")]
        [InlineData("internal-name")]
        [InlineData("visibility")]
        public async Task Catalog_change_is_rejected_before_commit(string change)
        {
            var starts = 0;
            var commits = 0;
            var gateway = Gateway(
                ChangedCatalog(change),
                _ => Context(Target(), 10, () => ++commits));

            var result = await gateway.GrantAsync(
                Command(),
                _ =>
                {
                    starts++;
                    return true;
                },
                CancellationToken.None);

            Assert.Equal(GrantItemGatewayStatus.Rejected, result.Status);
            Assert.Equal(GrantItemFailureCodes.CatalogChanged, result.FailureCode);
            Assert.Equal(0, starts);
            Assert.Equal(0, commits);
        }

        [Fact]
        public async Task Unsupported_game_version_is_rejected_before_start_and_commit()
        {
            var starts = 0;
            var commits = 0;
            var gateway = Gateway(
                Catalog(),
                _ => Context(Target(), 10, () => ++commits, versionSupported: false));

            var result = await gateway.GrantAsync(
                Command(),
                _ =>
                {
                    starts++;
                    return true;
                },
                CancellationToken.None);

            Assert.Equal(GrantItemGatewayStatus.Rejected, result.Status);
            Assert.Equal(GrantItemFailureCodes.VersionUnsupported, result.FailureCode);
            Assert.Equal(0, starts);
            Assert.Equal(0, commits);
        }

        [Fact]
        public async Task Insufficient_approved_bag_capacity_is_rejected_without_partial_commit()
        {
            var starts = 0;
            var commits = 0;
            var gateway = Gateway(
                Catalog(),
                _ => Context(Target(), 4, () => ++commits));

            var result = await gateway.GrantAsync(
                Command(quantity: 5),
                _ =>
                {
                    starts++;
                    return true;
                },
                CancellationToken.None);

            Assert.Equal(GrantItemGatewayStatus.Rejected, result.Status);
            Assert.Equal(GrantItemFailureCodes.InsufficientSpace, result.FailureCode);
            Assert.Equal(0, starts);
            Assert.Equal(0, commits);
        }

        [Fact]
        public async Task Cancellation_while_still_queued_is_cancelled_without_side_effect()
        {
            var commits = 0;
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var gateway = Gateway(
                Catalog(),
                _ => Context(Target(), 10, () => ++commits),
                dispatcher: (_, _, _, token) => Task.FromCanceled<GrantItemGatewayResult>(token));

            var result = await gateway.GrantAsync(
                Command(),
                _ => true,
                cancellation.Token);

            Assert.Equal(GrantItemGatewayStatus.Cancelled, result.Status);
            Assert.Equal(0, commits);
        }

        [Fact]
        public async Task Connection_or_callback_interruption_after_start_is_result_unknown()
        {
            var gateway = Gateway(
                Catalog(),
                _ => Context(
                    Target(),
                    10,
                    () => throw new InvalidOperationException("connection interrupted")));

            var result = await gateway.GrantAsync(
                Command(),
                _ => true,
                CancellationToken.None);

            Assert.Equal(GrantItemGatewayStatus.ResultUnknown, result.Status);
            Assert.Equal(GrantItemFailureCodes.ResultUnknown, result.FailureCode);
            Assert.Null(result.ActualQuantity);
        }

        [Fact]
        public async Task Native_callback_must_report_the_full_quantity_or_result_is_unknown()
        {
            var gateway = Gateway(
                Catalog(),
                _ => Context(Target(), 10, () => 4));

            var result = await gateway.GrantAsync(
                Command(quantity: 5),
                _ => true,
                CancellationToken.None);

            Assert.Equal(GrantItemGatewayStatus.ResultUnknown, result.Status);
            Assert.Null(result.ActualQuantity);
        }

        [Fact]
        public async Task Start_compare_and_set_failure_prevents_the_approved_bag_commit()
        {
            var commits = 0;
            var gateway = Gateway(
                Catalog(),
                _ => Context(Target(), 10, () => ++commits));

            var result = await gateway.GrantAsync(
                Command(),
                _ => false,
                CancellationToken.None);

            Assert.Equal(GrantItemGatewayStatus.Rejected, result.Status);
            Assert.Equal(GrantItemFailureCodes.OperationStartConflict, result.FailureCode);
            Assert.Equal(0, commits);
        }

        private static SevenDaysGrantItemGateway Gateway(
            IGameResourceCatalog catalog,
            Func<GrantItemCommand, GrantItemRuntimeContext?> captureContext,
            Func<
                string,
                Func<GrantItemGatewayResult>,
                TimeSpan,
                CancellationToken,
                Task<GrantItemGatewayResult>>? dispatcher = null)
        {
            return new SevenDaysGrantItemGateway(
                catalog,
                dispatcher ?? ((_, action, _, _) => Task.FromResult(action())),
                captureContext,
                (_, _) => Task.FromResult(Snapshot()),
                () => StartedAt);
        }

        private static GrantItemInventorySnapshot Snapshot() =>
            new GrantItemInventorySnapshot(
                StartedAt,
                "V 3.0 (b4)",
                "catalog-v1",
                CatalogResolutionState.Resolved,
                "inventory-fingerprint",
                new[]
                {
                    new InventoryItemScalar(
                        "bag",
                        0,
                        "resourceIron",
                        5,
                        null,
                        null,
                        Array.Empty<string>())
                });

        private static GrantItemRuntimeContext Context(
            PlayerTargetStamp target,
            int approvedCapacity,
            Func<int> commit,
            bool versionSupported = true) =>
            new GrantItemRuntimeContext(target, versionSupported, approvedCapacity, commit);

        private static GrantItemCommand Command(int quantity = 5) =>
            new GrantItemCommand(
                "operation-1",
                Target(),
                "catalog-v1",
                "resource-iron",
                17,
                "resourceIron",
                GameResourceKind.Item,
                GameResourceVisibility.Public,
                true,
                quantity,
                null,
                10,
                false,
                "V 3.0 (b4)");

        private static PlayerTargetStamp Target() =>
            new PlayerTargetStamp("EOS_123", 7, ObservedAt, "world-1");

        private static PlayerTargetStamp ChangedTarget(string change) =>
            new PlayerTargetStamp(
                change == "combined-id" ? "EOS_replacement" : "EOS_123",
                change == "entity-id" ? 8 : 7,
                change == "observed-stamp" ? ObservedAt.AddSeconds(1) : ObservedAt,
                change == "world-id" ? "world-2" : "world-1");

        private static IGameResourceCatalog ChangedCatalog(string change)
        {
            var version = change == "catalog-version" ? "catalog-v2" : "catalog-v1";
            var resourceId = change == "resource-id" ? "resource-replacement" : "resource-iron";
            var internalName = change == "internal-name" ? "resourceReplacement" : "resourceIron";
            var visibility = change == "visibility"
                ? GameResourceVisibility.Hidden
                : GameResourceVisibility.Public;
            return Catalog(
                null,
                version,
                resourceId,
                internalName,
                visibility);
        }

        private static StubCatalog Catalog(
            Action? onRead = null,
            string catalogVersion = "catalog-v1",
            string resourceId = "resource-iron",
            string internalName = "resourceIron",
            GameResourceVisibility visibility = GameResourceVisibility.Public)
        {
            var entry = new GameResourceCatalogEntry(
                resourceId,
                17,
                internalName,
                null,
                null,
                GameResourceKind.Item,
                visibility,
                10,
                false,
                GameResourceIconStatus.Missing,
                null);
            var snapshot = new GameResourceCatalogSnapshot(
                catalogVersion,
                "V 3.0 (b4)",
                ObservedAt,
                new[] { entry },
                Array.Empty<string>());
            return new StubCatalog(GameResourceCatalogReadResult.Available(snapshot), onRead);
        }

        private sealed class StubCatalog : IGameResourceCatalog
        {
            private readonly GameResourceCatalogReadResult result;
            private readonly Action? onRead;

            public StubCatalog(GameResourceCatalogReadResult result, Action? onRead)
            {
                this.result = result;
                this.onRead = onRead;
            }

            public GameResourceCatalogReadResult Read()
            {
                onRead?.Invoke();
                return result;
            }

            public Task<GameResourceIconReadResult> ReadIconAsync(
                string catalogVersion,
                string resourceId,
                CancellationToken cancellationToken) =>
                Task.FromResult(GameResourceIconReadResult.Missing());
        }
    }
}
