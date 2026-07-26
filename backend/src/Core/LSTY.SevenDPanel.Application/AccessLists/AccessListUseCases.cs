using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class AccessListUseCases
    {
        private const int MaximumDisplayNameLength = 80;
        private const int MaximumReasonLength = 200;

        private readonly IPlayerAccessControl accessControl;
        private readonly IRecentActivityWriter activityWriter;
        private readonly Func<DateTimeOffset> utcNow;

        public AccessListUseCases(
            IPlayerAccessControl accessControl,
            IRecentActivityWriter activityWriter)
            : this(accessControl, activityWriter, () => DateTimeOffset.UtcNow)
        {
        }

        internal AccessListUseCases(
            IPlayerAccessControl accessControl,
            IRecentActivityWriter activityWriter,
            Func<DateTimeOffset> utcNow)
        {
            this.accessControl = accessControl ?? throw new ArgumentNullException(nameof(accessControl));
            this.activityWriter = activityWriter ?? throw new ArgumentNullException(nameof(activityWriter));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public Task<IReadOnlyList<BanEntry>> GetBansAsync(CancellationToken cancellationToken) =>
            accessControl.GetBansAsync(cancellationToken);

        public Task<IReadOnlyList<WhitelistEntry>> GetWhitelistAsync(CancellationToken cancellationToken) =>
            accessControl.GetWhitelistAsync(cancellationToken);

        public async Task<AccessListMutationResult> UpsertBanAsync(
            string actorSubject,
            BanRequest request,
            CancellationToken cancellationToken)
        {
            ValidateActor(actorSubject);
            if (request == null) throw new ArgumentNullException(nameof(request));
            var now = utcNow();
            var normalized = new BanRequest(
                ValidatePlayerId(request.PlayerId),
                NormalizeDisplayName(request.DisplayName),
                ValidateBanExpiration(request.BannedUntilUtc, now),
                NormalizeReason(request.Reason));
            var result = await accessControl.UpsertBanAsync(normalized, cancellationToken).ConfigureAwait(false);
            await RecordAsync(actorSubject, "ban", "upsert", normalized.PlayerId, result, now, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }

        public async Task<AccessListMutationResult> RemoveBanAsync(
            string actorSubject,
            string playerId,
            CancellationToken cancellationToken)
        {
            ValidateActor(actorSubject);
            var normalizedPlayerId = ValidatePlayerId(playerId);
            var now = utcNow();
            var result = await accessControl.RemoveBanAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
            await RecordAsync(actorSubject, "ban", "remove", normalizedPlayerId, result, now, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }

        public async Task<AccessListMutationResult> UpsertWhitelistAsync(
            string actorSubject,
            WhitelistRequest request,
            CancellationToken cancellationToken)
        {
            ValidateActor(actorSubject);
            if (request == null) throw new ArgumentNullException(nameof(request));
            var normalized = new WhitelistRequest(
                ValidatePlayerId(request.PlayerId),
                NormalizeDisplayName(request.DisplayName));
            var now = utcNow();
            var result = await accessControl.UpsertWhitelistAsync(normalized, cancellationToken).ConfigureAwait(false);
            await RecordAsync(actorSubject, "whitelist", "upsert", normalized.PlayerId, result, now, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }

        public async Task<AccessListMutationResult> RemoveWhitelistAsync(
            string actorSubject,
            string playerId,
            CancellationToken cancellationToken)
        {
            ValidateActor(actorSubject);
            var normalizedPlayerId = ValidatePlayerId(playerId);
            var now = utcNow();
            var result = await accessControl.RemoveWhitelistAsync(normalizedPlayerId, cancellationToken).ConfigureAwait(false);
            await RecordAsync(actorSubject, "whitelist", "remove", normalizedPlayerId, result, now, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }

        private Task RecordAsync(
            string actorSubject,
            string list,
            string action,
            string playerId,
            AccessListMutationResult result,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken) =>
            activityWriter.RecordAccessListChangedAsync(
                actorSubject,
                list,
                action,
                playerId,
                result.Status.ToString().ToLowerInvariant(),
                occurredAtUtc,
                cancellationToken);

        private static void ValidateActor(string actorSubject)
        {
            if (string.IsNullOrWhiteSpace(actorSubject))
                throw new ArgumentException("An actor subject is required.", nameof(actorSubject));
        }

        private static string ValidatePlayerId(string playerId)
        {
            var normalized = playerId?.Trim();
            if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 160)
                throw new ArgumentException("A valid player id is required.", nameof(playerId));
            return normalized;
        }

        private static string NormalizeDisplayName(string displayName)
        {
            var normalized = displayName?.Trim() ?? string.Empty;
            if (normalized.Length > MaximumDisplayNameLength)
                throw new ArgumentException("The display name is too long.", nameof(displayName));
            return normalized;
        }

        private static DateTimeOffset? ValidateBanExpiration(DateTimeOffset? expiration, DateTimeOffset now)
        {
            if (expiration.HasValue && expiration.Value <= now)
                throw new ArgumentException("The ban expiration must be in the future.", nameof(expiration));
            return expiration?.ToUniversalTime();
        }

        private static string? NormalizeReason(string? reason)
        {
            var normalized = reason?.Trim();
            if (normalized != null && normalized.Length > MaximumReasonLength)
                throw new ArgumentException("The ban reason is too long.", nameof(reason));
            return string.IsNullOrEmpty(normalized) ? null : normalized;
        }
    }
}
