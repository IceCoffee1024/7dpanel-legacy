using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.AccessLists
{
    public sealed class SevenDaysPlayerAccessControl : IPlayerAccessControl
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> dispatcher;
        private readonly INativePlayerAccessLists native;

        public SevenDaysPlayerAccessControl()
            : this(
                (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                new NativePlayerAccessLists())
        {
        }

        internal SevenDaysPlayerAccessControl(
            Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> dispatcher,
            INativePlayerAccessLists native)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.native = native ?? throw new ArgumentNullException(nameof(native));
        }

        public async Task<IReadOnlyList<BanEntry>> GetBansAsync(CancellationToken cancellationToken)
        {
            try
            {
                var value = await DispatchAsync("7DPanel.AccessLists.GetBans", () => native.GetBans(), cancellationToken)
                    .ConfigureAwait(false);
                return Array.AsReadOnly(value.ToArray());
            }
            catch (TimeoutException)
            {
                throw new AccessListGameNotReadyException();
            }
        }

        public async Task<IReadOnlyList<WhitelistEntry>> GetWhitelistAsync(CancellationToken cancellationToken)
        {
            try
            {
                var value = await DispatchAsync("7DPanel.AccessLists.GetWhitelist", () => native.GetWhitelist(), cancellationToken)
                    .ConfigureAwait(false);
                return Array.AsReadOnly(value.ToArray());
            }
            catch (TimeoutException)
            {
                throw new AccessListGameNotReadyException();
            }
        }

        public Task<AccessListMutationResult> UpsertBanAsync(BanRequest request, CancellationToken cancellationToken) =>
            MutateAsync("7DPanel.AccessLists.UpsertBan", () => native.UpsertBan(request), cancellationToken);

        public Task<AccessListMutationResult> RemoveBanAsync(string playerId, CancellationToken cancellationToken) =>
            MutateAsync("7DPanel.AccessLists.RemoveBan", () => native.RemoveBan(playerId), cancellationToken);

        public Task<AccessListMutationResult> UpsertWhitelistAsync(WhitelistRequest request, CancellationToken cancellationToken) =>
            MutateAsync("7DPanel.AccessLists.UpsertWhitelist", () => native.UpsertWhitelist(request), cancellationToken);

        public Task<AccessListMutationResult> RemoveWhitelistAsync(string playerId, CancellationToken cancellationToken) =>
            MutateAsync("7DPanel.AccessLists.RemoveWhitelist", () => native.RemoveWhitelist(playerId), cancellationToken);

        private async Task<T> DispatchAsync<T>(string name, Func<T> action, CancellationToken cancellationToken)
        {
            var result = await dispatcher(name, () => action()!, DispatchTimeout, cancellationToken)
                .ConfigureAwait(false);
            return (T)result;
        }

        private async Task<AccessListMutationResult> MutateAsync(
            string name,
            Func<AccessListMutationResult> action,
            CancellationToken cancellationToken)
        {
            try
            {
                return await DispatchAsync(name, action, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return AccessListMutationResult.GameNotReady();
            }
            catch (AccessListGameNotReadyException)
            {
                return AccessListMutationResult.GameNotReady();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return AccessListMutationResult.NativeRejected(exception.GetType().Name);
            }
        }
    }

    internal interface INativePlayerAccessLists
    {
        IReadOnlyList<BanEntry> GetBans();
        IReadOnlyList<WhitelistEntry> GetWhitelist();
        AccessListMutationResult UpsertBan(BanRequest request);
        AccessListMutationResult RemoveBan(string playerId);
        AccessListMutationResult UpsertWhitelist(WhitelistRequest request);
        AccessListMutationResult RemoveWhitelist(string playerId);
    }

    internal sealed class NativePlayerAccessLists : INativePlayerAccessLists
    {
        public IReadOnlyList<BanEntry> GetBans()
        {
            var blacklist = global::GameManager.Instance?.adminTools?.Blacklist;
            if (blacklist == null) throw new AccessListGameNotReadyException();

            return blacklist.GetBanned()
                .Select(entry => new BanEntry(
                    entry.UserIdentifier?.CombinedString ?? string.Empty,
                    entry.Name ?? string.Empty,
                    entry.BannedUntil == DateTime.MaxValue
                        ? (DateTimeOffset?)null
                        : new DateTimeOffset(entry.BannedUntil.ToUniversalTime()),
                    string.IsNullOrWhiteSpace(entry.BanReason) ? null : entry.BanReason))
                .ToArray();
        }

        public IReadOnlyList<WhitelistEntry> GetWhitelist()
        {
            var whitelist = global::GameManager.Instance?.adminTools?.Whitelist;
            if (whitelist == null) throw new AccessListGameNotReadyException();

            return whitelist.GetUsers()
                .Select(pair => new WhitelistEntry(
                    pair.Value.UserIdentifier?.CombinedString ?? string.Empty,
                    pair.Value.Name ?? string.Empty))
                .ToArray();
        }

        public AccessListMutationResult UpsertBan(BanRequest request)
        {
            var blacklist = global::GameManager.Instance?.adminTools?.Blacklist;
            if (blacklist == null) return AccessListMutationResult.GameNotReady();
            if (!TryParsePlayerId(request.PlayerId, out var identifier))
                return AccessListMutationResult.NativeRejected("invalid_player_id");

            var expiration = request.BannedUntilUtc?.UtcDateTime ?? DateTime.MaxValue;
            blacklist.AddBan(request.DisplayName, identifier!, expiration, request.Reason ?? string.Empty);
            return AccessListMutationResult.Succeeded();
        }

        public AccessListMutationResult RemoveBan(string playerId)
        {
            var blacklist = global::GameManager.Instance?.adminTools?.Blacklist;
            if (blacklist == null) return AccessListMutationResult.GameNotReady();
            if (!TryParsePlayerId(playerId, out var identifier))
                return AccessListMutationResult.NativeRejected("invalid_player_id");
            return blacklist.RemoveBan(identifier!)
                ? AccessListMutationResult.Succeeded()
                : AccessListMutationResult.NotFound();
        }

        public AccessListMutationResult UpsertWhitelist(WhitelistRequest request)
        {
            var whitelist = global::GameManager.Instance?.adminTools?.Whitelist;
            if (whitelist == null) return AccessListMutationResult.GameNotReady();
            if (!TryParsePlayerId(request.PlayerId, out var identifier))
                return AccessListMutationResult.NativeRejected("invalid_player_id");
            whitelist.AddUser(request.DisplayName, identifier!);
            return AccessListMutationResult.Succeeded();
        }

        public AccessListMutationResult RemoveWhitelist(string playerId)
        {
            var whitelist = global::GameManager.Instance?.adminTools?.Whitelist;
            if (whitelist == null) return AccessListMutationResult.GameNotReady();
            if (!TryParsePlayerId(playerId, out var identifier))
                return AccessListMutationResult.NativeRejected("invalid_player_id");
            return whitelist.RemoveUser(identifier!)
                ? AccessListMutationResult.Succeeded()
                : AccessListMutationResult.NotFound();
        }

        private static bool TryParsePlayerId(
            string playerId,
            out global::PlatformUserIdentifierAbs? identifier) =>
            global::PlatformUserIdentifierAbs.TryFromCombinedString(playerId, out identifier);
    }
}
