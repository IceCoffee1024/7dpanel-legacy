using System;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class GeoIpPolicyUpdateHttpRequest
    {
        public long? ExpectedVersion { get; set; }
        public bool? IsEnabled { get; set; }
        public string? Provider { get; set; }
        public string? FailureMode { get; set; }
        public bool? BypassAdmins { get; set; }
        public string? RejectionMessage { get; set; }
        public GeoIpNetworkRuleHttpRequest[]? NetworkRules { get; set; }
        public GeoIpCountryRuleHttpRequest[]? CountryRules { get; set; }
    }

    public sealed class GeoIpNetworkRuleHttpRequest
    {
        public string? RuleId { get; set; }
        public string? NetworkCidr { get; set; }
        public string? Effect { get; set; }
        public int? Ordinal { get; set; }
    }

    public sealed class GeoIpCountryRuleHttpRequest
    {
        public string? CountryCode { get; set; }
        public string? Effect { get; set; }
    }

    public sealed class GeoIpTestHttpRequest
    {
        public string? IpAddress { get; set; }
    }

    public sealed class GeoIpCredentialsUpdateHttpRequest
    {
        public GeoIpSecretUpdateHttpRequest? AccountId { get; set; }
        public GeoIpSecretUpdateHttpRequest? LicenseKey { get; set; }
    }

    public sealed class GeoIpSecretUpdateHttpRequest
    {
        public string? Operation { get; set; }
        public string? Value { get; set; }
    }

    public sealed class GeoIpPolicySummaryHttpResponse
    {
        public GeoIpPolicySummaryHttpResponse(
            long version,
            bool isEnabled,
            string provider,
            string failureMode,
            bool bypassAdmins,
            string rejectionMessage,
            GeoIpNetworkRuleHttpResponse[] networkRules,
            GeoIpCountryRuleHttpResponse[] countryRules,
            GeoIpCacheHealthHttpResponse cacheHealth,
            GeoIpProviderHttpResponse[] providers,
            GeoIpDecisionHttpResponse[] recentDecisions)
        {
            Version = version;
            IsEnabled = isEnabled;
            Provider = provider;
            FailureMode = failureMode;
            BypassAdmins = bypassAdmins;
            RejectionMessage = rejectionMessage;
            NetworkRules = networkRules;
            CountryRules = countryRules;
            CacheHealth = cacheHealth;
            Providers = providers;
            RecentDecisions = recentDecisions;
        }

        public long Version { get; }
        public bool IsEnabled { get; }
        public string Provider { get; }
        public string FailureMode { get; }
        public bool BypassAdmins { get; }
        public string RejectionMessage { get; }
        public GeoIpNetworkRuleHttpResponse[] NetworkRules { get; }
        public GeoIpCountryRuleHttpResponse[] CountryRules { get; }
        public GeoIpCacheHealthHttpResponse CacheHealth { get; }
        public GeoIpProviderHttpResponse[] Providers { get; }
        public GeoIpDecisionHttpResponse[] RecentDecisions { get; }
    }

    public sealed class GeoIpNetworkRuleHttpResponse
    {
        public GeoIpNetworkRuleHttpResponse(
            string ruleId,
            string networkCidr,
            string effect,
            int ordinal)
        {
            RuleId = ruleId;
            NetworkCidr = networkCidr;
            Effect = effect;
            Ordinal = ordinal;
        }

        public string RuleId { get; }
        public string NetworkCidr { get; }
        public string Effect { get; }
        public int Ordinal { get; }
    }

    public sealed class GeoIpCountryRuleHttpResponse
    {
        public GeoIpCountryRuleHttpResponse(string countryCode, string effect)
        {
            CountryCode = countryCode;
            Effect = effect;
        }

        public string CountryCode { get; }
        public string Effect { get; }
    }

    public sealed class GeoIpCacheHealthHttpResponse
    {
        public GeoIpCacheHealthHttpResponse(
            int queueDepth,
            long rejectedRefreshCount,
            DateTimeOffset? lastCompletedAtUtc,
            string? lastLookupStatus,
            string severity,
            string statusCode)
        {
            QueueDepth = queueDepth;
            RejectedRefreshCount = rejectedRefreshCount;
            LastCompletedAtUtc = lastCompletedAtUtc;
            LastLookupStatus = lastLookupStatus;
            Severity = severity;
            StatusCode = statusCode;
        }

        public int QueueDepth { get; }
        public long RejectedRefreshCount { get; }
        public DateTimeOffset? LastCompletedAtUtc { get; }
        public string? LastLookupStatus { get; }
        public string Severity { get; }
        public string StatusCode { get; }
    }

    public sealed class GeoIpProviderHttpResponse
    {
        public GeoIpProviderHttpResponse(
            string provider,
            bool isExternal,
            string? dataVersion,
            string? buildEpoch)
        {
            Provider = provider;
            IsExternal = isExternal;
            DataVersion = dataVersion;
            BuildEpoch = buildEpoch;
        }

        public string Provider { get; }
        public bool IsExternal { get; }
        public string? DataVersion { get; }
        public string? BuildEpoch { get; }
    }

    public sealed class GeoIpDecisionHttpResponse
    {
        public GeoIpDecisionHttpResponse(
            DateTimeOffset occurredAtUtc,
            string maskedIp,
            string decision,
            string reasonCode,
            string lookupStatus)
        {
            OccurredAtUtc = occurredAtUtc;
            MaskedIp = maskedIp;
            Decision = decision;
            ReasonCode = reasonCode;
            LookupStatus = lookupStatus;
        }

        public DateTimeOffset OccurredAtUtc { get; }
        public string MaskedIp { get; }
        public string Decision { get; }
        public string ReasonCode { get; }
        public string LookupStatus { get; }
    }

    public sealed class GeoIpPolicyUpdateHttpResponse
    {
        public GeoIpPolicyUpdateHttpResponse(long version, string state)
        {
            Version = version;
            State = state;
        }

        public long Version { get; }
        public string State { get; }
    }

    public sealed class GeoIpCredentialsUpdateHttpResponse
    {
        public GeoIpCredentialsUpdateHttpResponse(
            GeoIpCredentialHttpResponse accountId,
            GeoIpCredentialHttpResponse licenseKey)
        {
            AccountId = accountId;
            LicenseKey = licenseKey;
        }

        public GeoIpCredentialHttpResponse AccountId { get; }
        public GeoIpCredentialHttpResponse LicenseKey { get; }
    }

    public sealed class GeoIpCredentialHttpResponse
    {
        public GeoIpCredentialHttpResponse(
            bool isSet,
            string? fingerprint,
            DateTimeOffset? updatedAtUtc)
        {
            IsSet = isSet;
            Fingerprint = fingerprint;
            UpdatedAtUtc = updatedAtUtc;
        }

        public bool IsSet { get; }
        public string? Fingerprint { get; }
        public DateTimeOffset? UpdatedAtUtc { get; }
    }

    public sealed class GeoIpTestHttpResponse
    {
        public GeoIpTestHttpResponse(bool accepted, string maskedIp, string state)
        {
            Accepted = accepted;
            MaskedIp = maskedIp;
            State = state;
        }

        public bool Accepted { get; }
        public string MaskedIp { get; }
        public string State { get; }
    }

    public sealed class GeoIpDiagnosticsHttpResponse
    {
        public GeoIpDiagnosticsHttpResponse(
            bool isEnabled,
            string failureMode,
            string provider,
            string severity,
            string statusCode,
            int queueDepth,
            long rejectedRefreshCount,
            DateTimeOffset? lastCompletedAtUtc,
            string? lastLookupStatus,
            GeoIpProviderHttpResponse[] providers)
        {
            IsEnabled = isEnabled;
            FailureMode = failureMode;
            Provider = provider;
            Severity = severity;
            StatusCode = statusCode;
            QueueDepth = queueDepth;
            RejectedRefreshCount = rejectedRefreshCount;
            LastCompletedAtUtc = lastCompletedAtUtc;
            LastLookupStatus = lastLookupStatus;
            Providers = providers;
        }

        public bool IsEnabled { get; }
        public string FailureMode { get; }
        public string Provider { get; }
        public string Severity { get; }
        public string StatusCode { get; }
        public int QueueDepth { get; }
        public long RejectedRefreshCount { get; }
        public DateTimeOffset? LastCompletedAtUtc { get; }
        public string? LastLookupStatus { get; }
        public GeoIpProviderHttpResponse[] Providers { get; }
    }
}
