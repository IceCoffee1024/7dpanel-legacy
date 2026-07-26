using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.AccessLists;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysPlayerAccessControlTests
    {
        [Fact]
        public async Task Ban_entries_are_copied_inside_the_dispatcher()
        {
            var dispatched = false;
            var source = new List<BanEntry> { new BanEntry("EOS_1", "Player", DateTimeOffset.UtcNow.AddDays(1), "reason") };
            var adapter = new SevenDaysPlayerAccessControl(
                (name, action, timeout, token) =>
                {
                    dispatched = true;
                    Assert.Equal("7DPanel.AccessLists.GetBans", name);
                    return Task.FromResult(action());
                },
                new DelegateNativeAccessLists(
                    getBans: () =>
                    {
                        Assert.True(dispatched);
                        return source;
                    }));

            var result = await adapter.GetBansAsync(CancellationToken.None);
            source.Clear();

            Assert.Single(result);
            Assert.Equal("EOS_1", result[0].PlayerId);
        }

        [Fact]
        public async Task Dispatcher_timeout_maps_mutation_to_game_not_ready_without_retry()
        {
            var dispatchCalls = 0;
            var nativeCalls = 0;
            var adapter = new SevenDaysPlayerAccessControl(
                (_, _, _, _) =>
                {
                    dispatchCalls++;
                    return Task.FromException<object>(new TimeoutException());
                },
                new DelegateNativeAccessLists(upsertWhitelist: _ =>
                {
                    nativeCalls++;
                    return AccessListMutationResult.Succeeded();
                }));

            var result = await adapter.UpsertWhitelistAsync(
                new WhitelistRequest("EOS_1", "Player"),
                CancellationToken.None);

            Assert.Equal(AccessListMutationStatus.GameNotReady, result.Status);
            Assert.Equal(1, dispatchCalls);
            Assert.Equal(0, nativeCalls);
        }

        [Fact]
        public async Task Native_remove_result_is_preserved()
        {
            var expected = AccessListMutationResult.NotFound("missing");
            var adapter = new SevenDaysPlayerAccessControl(
                (_, action, _, _) => Task.FromResult(action()),
                new DelegateNativeAccessLists(removeBan: _ => expected));

            var result = await adapter.RemoveBanAsync("EOS_1", CancellationToken.None);

            Assert.Same(expected, result);
        }

        private sealed class DelegateNativeAccessLists : INativePlayerAccessLists
        {
            private readonly Func<IReadOnlyList<BanEntry>> getBans;
            private readonly Func<WhitelistRequest, AccessListMutationResult> upsertWhitelist;
            private readonly Func<string, AccessListMutationResult> removeBan;

            public DelegateNativeAccessLists(
                Func<IReadOnlyList<BanEntry>>? getBans = null,
                Func<WhitelistRequest, AccessListMutationResult>? upsertWhitelist = null,
                Func<string, AccessListMutationResult>? removeBan = null)
            {
                this.getBans = getBans ?? (() => Array.Empty<BanEntry>());
                this.upsertWhitelist = upsertWhitelist ?? (_ => AccessListMutationResult.Succeeded());
                this.removeBan = removeBan ?? (_ => AccessListMutationResult.Succeeded());
            }

            public IReadOnlyList<BanEntry> GetBans() => getBans();
            public IReadOnlyList<WhitelistEntry> GetWhitelist() => Array.Empty<WhitelistEntry>();
            public AccessListMutationResult UpsertBan(BanRequest request) => AccessListMutationResult.Succeeded();
            public AccessListMutationResult RemoveBan(string playerId) => removeBan(playerId);
            public AccessListMutationResult UpsertWhitelist(WhitelistRequest request) => upsertWhitelist(request);
            public AccessListMutationResult RemoveWhitelist(string playerId) => AccessListMutationResult.Succeeded();
        }
    }
}
