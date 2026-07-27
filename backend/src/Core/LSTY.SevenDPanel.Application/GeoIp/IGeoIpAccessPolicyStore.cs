using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application.GeoIp
{
    public enum GeoIpFailureMode
    {
        FailOpen,
        FailClosed
    }

    public sealed record GeoIpAccessPolicySettings(
        long Version,
        bool IsEnabled,
        string Provider,
        GeoIpFailureMode FailureMode,
        bool BypassAdmins,
        string RejectionMessage)
    {
        public static GeoIpAccessPolicySettings CreateDefault() =>
            new GeoIpAccessPolicySettings(
                0,
                false,
                GeoIpProviderNames.LocalMmdb,
                GeoIpFailureMode.FailOpen,
                true,
                GeoIpPolicyDecision.DefaultRejectionMessage);
    }

    public sealed record GeoIpSecretMetadata(
        string SecretKey,
        string Fingerprint,
        DateTimeOffset UpdatedAtUtc);

    public sealed record GeoIpSecretValue(
        string SecretKey,
        string SecretValue,
        string Fingerprint,
        DateTimeOffset UpdatedAtUtc);

    public sealed record GeoIpSecretMutation(
        string SecretKey,
        GeoIpSecretValue? Replacement);

    public sealed record GeoIpNetworkRule(
        string RuleId,
        string NetworkCidr,
        string Effect,
        int Ordinal);

    public sealed record GeoIpCountryRule(string CountryCode, string Effect);

    public sealed record GeoIpCacheEntry(
        string CanonicalIp,
        string LookupStatus,
        string? CountryCode,
        string Source,
        string? SourceVersion,
        DateTimeOffset QueriedAtUtc,
        DateTimeOffset ExpiresAtUtc);

    public sealed record GeoIpDecision(
        string DecisionId,
        DateTimeOffset OccurredAtUtc,
        string MaskedIp,
        string? CrossplatformId,
        string Decision,
        string ReasonCode,
        string LookupStatus);

    public sealed record GeoIpDecisionKeyset(
        DateTimeOffset OccurredAtUtc,
        string DecisionId);

    public sealed record GeoIpDecisionQuery(
        int PageSize,
        GeoIpDecisionKeyset? Keyset = null);

    public sealed record GeoIpDecisionPage(
        IReadOnlyList<GeoIpDecision> Decisions,
        GeoIpDecisionKeyset? NextKeyset);

    public interface IGeoIpAccessPolicyStore
    {
        GeoIpAccessPolicySettings? GetSettings();

        void SaveSettings(GeoIpAccessPolicySettings settings, long expectedVersion);

        void SetSecret(GeoIpSecretValue secret);

        void ApplySecretChanges(IReadOnlyList<GeoIpSecretMutation> changes);

        GeoIpSecretValue? GetSecret(string secretKey);

        IReadOnlyList<GeoIpSecretMetadata> ListSecretMetadata();

        void ReplaceNetworkRules(IReadOnlyList<GeoIpNetworkRule> rules);

        IReadOnlyList<GeoIpNetworkRule> ListNetworkRules();

        void ReplaceCountryRules(IReadOnlyList<GeoIpCountryRule> rules);

        IReadOnlyList<GeoIpCountryRule> ListCountryRules();

        void UpsertCache(GeoIpCacheEntry entry);

        GeoIpCacheEntry? FindCache(string ipAddress);

        void RecordDecision(GeoIpDecision decision);

        GeoIpDecisionPage QueryDecisions(GeoIpDecisionQuery query);
    }

    public sealed class GeoIpAccessPolicyVersionConflictException : InvalidOperationException
    {
        public GeoIpAccessPolicyVersionConflictException()
            : base("geoip_settings_version_conflict")
        {
        }
    }
}
