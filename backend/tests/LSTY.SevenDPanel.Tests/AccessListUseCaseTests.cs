using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class AccessListUseCaseTests
    {
        [Fact]
        public async Task Conflict_is_returned_without_retrying_and_is_audited_once()
        {
            var port = new StubPlayerAccessControl(AccessListMutationResult.Conflict("changed"));
            var audit = new RecordingRecentActivityWriter();
            var useCases = new AccessListUseCases(port, audit, () => FixedNow);

            var result = await useCases.UpsertBanAsync(
                "owner",
                new BanRequest("EOS_1", "Player", FixedNow.AddDays(1), "reason"),
                CancellationToken.None);

            Assert.Equal(AccessListMutationStatus.Conflict, result.Status);
            Assert.Equal(1, port.UpsertBanCallCount);
            var entry = Assert.Single(audit.AccessListChanges);
            Assert.Equal(("owner", "ban", "upsert", "EOS_1", "conflict"), entry);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Blank_player_id_is_rejected_before_the_native_port(string playerId)
        {
            var port = new StubPlayerAccessControl(AccessListMutationResult.Succeeded());
            var useCases = new AccessListUseCases(port, new RecordingRecentActivityWriter());

            await Assert.ThrowsAsync<ArgumentException>(() => useCases.UpsertWhitelistAsync(
                "owner",
                new WhitelistRequest(playerId, "Player"),
                CancellationToken.None));

            Assert.Equal(0, port.UpsertWhitelistCallCount);
        }

        [Fact]
        public async Task Ban_expiration_must_be_in_the_future()
        {
            var port = new StubPlayerAccessControl(AccessListMutationResult.Succeeded());
            var useCases = new AccessListUseCases(port, new RecordingRecentActivityWriter(), () => FixedNow);

            await Assert.ThrowsAsync<ArgumentException>(() => useCases.UpsertBanAsync(
                "owner",
                new BanRequest("EOS_1", "Player", FixedNow, "reason"),
                CancellationToken.None));

            Assert.Equal(0, port.UpsertBanCallCount);
        }

        [Fact]
        public async Task Reason_longer_than_200_characters_is_rejected()
        {
            var port = new StubPlayerAccessControl(AccessListMutationResult.Succeeded());
            var useCases = new AccessListUseCases(port, new RecordingRecentActivityWriter(), () => FixedNow);

            await Assert.ThrowsAsync<ArgumentException>(() => useCases.UpsertBanAsync(
                "owner",
                new BanRequest("EOS_1", "Player", FixedNow.AddDays(1), new string('x', 201)),
                CancellationToken.None));

            Assert.Equal(0, port.UpsertBanCallCount);
        }

        private static readonly DateTimeOffset FixedNow =
            new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);

        private sealed class StubPlayerAccessControl : IPlayerAccessControl
        {
            private readonly AccessListMutationResult result;

            public StubPlayerAccessControl(AccessListMutationResult result) { this.result = result; }

            public int UpsertBanCallCount { get; private set; }
            public int UpsertWhitelistCallCount { get; private set; }

            public Task<IReadOnlyList<BanEntry>> GetBansAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<BanEntry>>(Array.Empty<BanEntry>());

            public Task<IReadOnlyList<WhitelistEntry>> GetWhitelistAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<WhitelistEntry>>(Array.Empty<WhitelistEntry>());

            public Task<AccessListMutationResult> UpsertBanAsync(BanRequest request, CancellationToken cancellationToken)
            {
                UpsertBanCallCount++;
                return Task.FromResult(result);
            }

            public Task<AccessListMutationResult> RemoveBanAsync(string playerId, CancellationToken cancellationToken) =>
                Task.FromResult(result);

            public Task<AccessListMutationResult> UpsertWhitelistAsync(WhitelistRequest request, CancellationToken cancellationToken)
            {
                UpsertWhitelistCallCount++;
                return Task.FromResult(result);
            }

            public Task<AccessListMutationResult> RemoveWhitelistAsync(string playerId, CancellationToken cancellationToken) =>
                Task.FromResult(result);
        }

        private sealed class RecordingRecentActivityWriter : IRecentActivityWriter, IServerGovernanceActivityWriter
        {
            public List<(string Actor, string List, string Action, string PlayerId, string Outcome)> AccessListChanges { get; } =
                new List<(string, string, string, string, string)>();

            public Task RecordAccessListChangedAsync(string actorSubject, string list, string action,
                string playerId, string outcome, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
            {
                AccessListChanges.Add((actorSubject, list, action, playerId, outcome));
                return Task.CompletedTask;
            }

            public Task RecordPanelLoginSucceededAsync(string actorSubject, string actorDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerJoinedAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerLeftAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordRestartScriptStartedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordShutdownRequestedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordServerOperationFailedAsync(string actorSubject, string operationCode, string failureCode, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
