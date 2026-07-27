using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LSTY.SevenDPanel.Application.GeoIp
{
    public sealed class EvaluateGeoIpJoinUseCase
    {
        private readonly IGeoIpAccessPolicyStore store;
        private readonly GeoIpPolicyEvaluator evaluator;
        private readonly IGeoIpRefreshQueue refreshQueue;
        private readonly Func<DateTimeOffset> utcClock;

        public EvaluateGeoIpJoinUseCase(
            IGeoIpAccessPolicyStore store,
            GeoIpPolicyEvaluator evaluator,
            IGeoIpRefreshQueue refreshQueue,
            Func<DateTimeOffset>? utcClock = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            this.refreshQueue = refreshQueue ?? throw new ArgumentNullException(nameof(refreshQueue));
            this.utcClock = utcClock ?? (() => DateTimeOffset.UtcNow);
        }

        public GeoIpPolicyDecision Execute(GeoIpJoinAttempt attempt)
        {
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            var now = RequireUtc(utcClock());
            var settings = GetSettings();
            if (!settings.IsEnabled)
            {
                var disabled = new GeoIpPolicyDecision(
                    true,
                    "disabled",
                    GeoIpLookupStatus.Unknown,
                    null);
                TryRecord(attempt, disabled, now);
                return disabled;
            }

            GeoIpLookupResult lookup;
            var cacheHit = false;
            var refreshEnqueued = false;
            if (!GeoIpAddressNormalizer.TryNormalize(attempt.IpAddress, out var normalized))
            {
                lookup = GeoIpLookupResult.Invalid();
            }
            else if (normalized!.IsPrivate)
            {
                lookup = GeoIpLookupResult.Private();
            }
            else
            {
                var cached = TryFindCache(normalized.CanonicalIp);
                if (cached != null && cached.ExpiresAtUtc > now)
                {
                    lookup = GeoIpLookupResult.FromCache(cached);
                    cacheHit = true;
                }
                else
                {
                    lookup = GeoIpLookupResult.Unavailable("Cache", GeoIpLookupFailure.Unexpected);
                    refreshEnqueued = refreshQueue.TryWrite(new GeoIpRefreshRequest(
                        settings.Provider,
                        normalized.CanonicalIp,
                        settings.Version,
                        now));
                }
            }

            var decision = evaluator.Evaluate(
                    settings,
                    TryListNetworkRules(),
                    TryListCountryRules(),
                    attempt.IpAddress,
                    attempt.IsConfirmedNativeAdministrator,
                    lookup)
                .WithCacheState(cacheHit, refreshEnqueued);
            TryRecord(attempt, decision, now);
            return decision;
        }

        private GeoIpAccessPolicySettings GetSettings()
        {
            try
            {
                var settings = store.GetSettings() ?? GeoIpAccessPolicySettings.CreateDefault();
                if (!GeoIpProviderNames.IsApproved(settings.Provider))
                    return settings with
                    {
                        Provider = GeoIpProviderNames.LocalMmdb,
                        FailureMode = GeoIpFailureMode.FailOpen
                    };
                return settings;
            }
            catch
            {
                return GeoIpAccessPolicySettings.CreateDefault();
            }
        }

        private GeoIpCacheEntry? TryFindCache(string canonicalIp)
        {
            try { return store.FindCache(canonicalIp); }
            catch { return null; }
        }

        private IReadOnlyList<GeoIpNetworkRule> TryListNetworkRules()
        {
            try { return store.ListNetworkRules(); }
            catch { return Array.Empty<GeoIpNetworkRule>(); }
        }

        private IReadOnlyList<GeoIpCountryRule> TryListCountryRules()
        {
            try { return store.ListCountryRules(); }
            catch { return Array.Empty<GeoIpCountryRule>(); }
        }

        private void TryRecord(
            GeoIpJoinAttempt attempt,
            GeoIpPolicyDecision decision,
            DateTimeOffset occurredAtUtc)
        {
            try
            {
                store.RecordDecision(new GeoIpDecision(
                    Guid.NewGuid().ToString("N"),
                    occurredAtUtc,
                    GeoIpAddressNormalizer.Mask(attempt.IpAddress),
                    string.IsNullOrWhiteSpace(attempt.CrossplatformId)
                        ? null
                        : attempt.CrossplatformId,
                    decision.IsAllowed ? "Allow" : "Deny",
                    decision.ReasonCode,
                    decision.LookupStatus.ToString()));
            }
            catch
            {
            }
        }

        private static DateTimeOffset RequireUtc(DateTimeOffset value) =>
            value.Offset == TimeSpan.Zero
                ? value
                : throw new InvalidOperationException("The GeoIP clock must return UTC.");
    }

    public sealed class GetGeoIpDiagnosticsUseCase
    {
        private readonly IGeoIpAccessPolicyStore store;
        private readonly IGeoIpRefreshDiagnostics refreshDiagnostics;

        public GetGeoIpDiagnosticsUseCase(
            IGeoIpAccessPolicyStore store,
            IGeoIpRefreshDiagnostics refreshDiagnostics)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.refreshDiagnostics = refreshDiagnostics ??
                throw new ArgumentNullException(nameof(refreshDiagnostics));
        }

        public GeoIpDiagnosticsSnapshot Execute()
        {
            GeoIpAccessPolicySettings settings;
            try { settings = store.GetSettings() ?? GeoIpAccessPolicySettings.CreateDefault(); }
            catch { settings = GeoIpAccessPolicySettings.CreateDefault(); }
            var refresh = refreshDiagnostics.GetDiagnostics();

            var severity = GeoIpDiagnosticSeverity.Information;
            var statusCode = settings.IsEnabled ? "ready" : "disabled";
            if (settings.IsEnabled && refresh.LastLookupStatus == GeoIpLookupStatus.Unavailable)
            {
                severity = GeoIpDiagnosticSeverity.Error;
                statusCode = "provider_unavailable";
            }
            else if (settings.FailureMode == GeoIpFailureMode.FailOpen)
            {
                severity = GeoIpDiagnosticSeverity.Warning;
                statusCode = "fail_open_active";
            }

            return new GeoIpDiagnosticsSnapshot(
                settings.IsEnabled,
                settings.FailureMode,
                settings.Provider,
                severity,
                statusCode,
                refresh.QueueDepth,
                refresh.RejectedCount,
                refresh.LastCompletedAtUtc,
                refresh.LastLookupStatus,
                refresh.Providers);
        }
    }

    public enum GeoIpSecretUpdateOperation
    {
        Keep,
        Replace,
        Clear
    }

    public sealed record GeoIpSecretUpdate(
        GeoIpSecretUpdateOperation Operation,
        string? Value)
    {
        public static GeoIpSecretUpdate Keep() =>
            new GeoIpSecretUpdate(GeoIpSecretUpdateOperation.Keep, null);

        public static GeoIpSecretUpdate Replace(string value) =>
            new GeoIpSecretUpdate(GeoIpSecretUpdateOperation.Replace, value);

        public static GeoIpSecretUpdate Clear() =>
            new GeoIpSecretUpdate(GeoIpSecretUpdateOperation.Clear, null);
    }

    public sealed record GeoIpCredentialsUpdate(
        GeoIpSecretUpdate AccountId,
        GeoIpSecretUpdate LicenseKey);

    public sealed class GeoIpCredentialsActor
    {
        public GeoIpCredentialsActor(string subject, bool isOwner)
        {
            Subject = subject;
            IsOwner = isOwner;
        }

        public string Subject { get; }
        public bool IsOwner { get; }
    }

    public sealed record GeoIpCredentialState(
        bool IsSet,
        string? Fingerprint,
        DateTimeOffset? UpdatedAtUtc);

    public sealed record GeoIpCredentialsState(
        GeoIpCredentialState AccountId,
        GeoIpCredentialState LicenseKey);

    public sealed class GeoIpOwnerRequiredException : InvalidOperationException
    {
        public GeoIpOwnerRequiredException()
            : base("geoip_owner_required")
        {
        }
    }

    public sealed class UpdateGeoIpCredentialsUseCase
    {
        private readonly IGeoIpAccessPolicyStore store;
        private readonly Func<DateTimeOffset> utcClock;

        public UpdateGeoIpCredentialsUseCase(
            IGeoIpAccessPolicyStore store,
            Func<DateTimeOffset>? utcClock = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcClock = utcClock ?? (() => DateTimeOffset.UtcNow);
        }

        public GeoIpCredentialsState Execute(
            GeoIpCredentialsActor actor,
            GeoIpCredentialsUpdate update)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (update == null) throw new ArgumentNullException(nameof(update));
            if (!actor.IsOwner) throw new GeoIpOwnerRequiredException();

            var now = RequireUtc(utcClock());
            var accountId = Resolve(
                GeoIpSecretKeys.MaxMindAccountId,
                update.AccountId,
                store.GetSecret(GeoIpSecretKeys.MaxMindAccountId),
                now,
                requirePositiveInteger: true,
                out var accountChange);
            var licenseKey = Resolve(
                GeoIpSecretKeys.MaxMindLicenseKey,
                update.LicenseKey,
                store.GetSecret(GeoIpSecretKeys.MaxMindLicenseKey),
                now,
                requirePositiveInteger: false,
                out var licenseChange);

            var changes = new List<GeoIpSecretMutation>(2);
            if (accountChange != null) changes.Add(accountChange);
            if (licenseChange != null) changes.Add(licenseChange);
            if (changes.Count > 0) store.ApplySecretChanges(changes);

            return new GeoIpCredentialsState(accountId, licenseKey);
        }

        private static GeoIpCredentialState Resolve(
            string secretKey,
            GeoIpSecretUpdate update,
            GeoIpSecretValue? current,
            DateTimeOffset now,
            bool requirePositiveInteger,
            out GeoIpSecretMutation? change)
        {
            if (update == null) throw new ArgumentException("A credential update is required.");
            switch (update.Operation)
            {
                case GeoIpSecretUpdateOperation.Keep:
                    change = null;
                    return State(current);
                case GeoIpSecretUpdateOperation.Clear:
                    change = new GeoIpSecretMutation(secretKey, null);
                    return new GeoIpCredentialState(false, null, null);
                case GeoIpSecretUpdateOperation.Replace:
                    var normalized = NormalizeReplacement(update.Value, requirePositiveInteger);
                    var replacement = new GeoIpSecretValue(
                        secretKey,
                        normalized,
                        Fingerprint(normalized),
                        now);
                    change = new GeoIpSecretMutation(secretKey, replacement);
                    return State(replacement);
                default:
                    throw new ArgumentException("The credential update operation is invalid.");
            }
        }

        private static GeoIpCredentialState State(GeoIpSecretValue? value) =>
            value == null
                ? new GeoIpCredentialState(false, null, null)
                : new GeoIpCredentialState(true, value.Fingerprint, value.UpdatedAtUtc);

        private static string NormalizeReplacement(string? value, bool requirePositiveInteger)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A credential value is required.");
            var normalized = value!.Trim();
            if (!requirePositiveInteger) return normalized;
            if (!int.TryParse(
                    normalized,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var accountId) ||
                accountId <= 0)
                throw new ArgumentException("The MaxMind account identifier is invalid.");
            return accountId.ToString(CultureInfo.InvariantCulture);
        }

        private static string Fingerprint(string value)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var valueByte in hash)
                builder.Append(valueByte.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static DateTimeOffset RequireUtc(DateTimeOffset value) =>
            value.Offset == TimeSpan.Zero
                ? value
                : throw new InvalidOperationException("The GeoIP clock must return UTC.");
    }
}
