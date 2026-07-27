using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Dapper;
using LSTY.SevenDPanel.Application.GeoIp;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteGeoIpAccessPolicyStore : IGeoIpAccessPolicyStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteGeoIpAccessPolicyStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public GeoIpAccessPolicySettings? GetSettings()
        {
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<SettingsRow>(
                @"SELECT version, enabled, provider, failure_mode,
                         bypass_admins, rejection_message
                  FROM geoip_settings WHERE singleton_id = 1;");
            if (row == null) return null;
            return new GeoIpAccessPolicySettings(
                row.version,
                row.enabled != 0,
                row.provider,
                ParseFailureMode(row.failure_mode),
                row.bypass_admins != 0,
                row.rejection_message);
        }

        public void SaveSettings(GeoIpAccessPolicySettings settings, long expectedVersion)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (expectedVersion < 0 || settings.Version != expectedVersion + 1)
                throw new ArgumentOutOfRangeException(nameof(expectedVersion));
            RequireText(settings.Provider, nameof(settings));
            RequireText(settings.RejectionMessage, nameof(settings));
            using var connection = connectionFactory.Open();
            var parameters = new
            {
                settings.Version,
                Enabled = settings.IsEnabled ? 1 : 0,
                settings.Provider,
                FailureMode = settings.FailureMode.ToString(),
                BypassAdmins = settings.BypassAdmins ? 1 : 0,
                settings.RejectionMessage,
                ExpectedVersion = expectedVersion
            };
            var affected = expectedVersion == 0
                ? connection.Execute(
                    @"INSERT INTO geoip_settings (
                          singleton_id, version, enabled, provider, failure_mode,
                          bypass_admins, rejection_message)
                      VALUES (
                          1, @Version, @Enabled, @Provider, @FailureMode,
                          @BypassAdmins, @RejectionMessage)
                      ON CONFLICT(singleton_id) DO NOTHING;",
                    parameters)
                : connection.Execute(
                    @"UPDATE geoip_settings
                      SET version = @Version,
                          enabled = @Enabled,
                          provider = @Provider,
                          failure_mode = @FailureMode,
                          bypass_admins = @BypassAdmins,
                          rejection_message = @RejectionMessage
                      WHERE singleton_id = 1 AND version = @ExpectedVersion;",
                    parameters);
            if (affected != 1) throw new GeoIpAccessPolicyVersionConflictException();
        }

        public void SetSecret(GeoIpSecretValue secret)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));
            ApplySecretChanges(new[] { new GeoIpSecretMutation(secret.SecretKey, secret) });
        }

        public void ApplySecretChanges(IReadOnlyList<GeoIpSecretMutation> changes)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            var copied = changes.ToArray();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var change in copied)
            {
                if (change == null) throw new ArgumentException("Changes cannot contain null.", nameof(changes));
                RequireApprovedSecretKey(change.SecretKey, nameof(changes));
                if (!keys.Add(change.SecretKey))
                    throw new ArgumentException("Each secret can only change once.", nameof(changes));
                if (change.Replacement == null) continue;
                if (!string.Equals(
                        change.SecretKey,
                        change.Replacement.SecretKey,
                        StringComparison.Ordinal))
                    throw new ArgumentException("The secret replacement key is invalid.", nameof(changes));
                RequireApprovedSecretKey(change.Replacement.SecretKey, nameof(changes));
                RequireText(change.Replacement.SecretValue, nameof(changes));
                RequireText(change.Replacement.Fingerprint, nameof(changes));
                RequireUtc(change.Replacement.UpdatedAtUtc, nameof(changes));
            }

            using var connection = connectionFactory.Open();
            ExecuteImmediate(connection, () =>
            {
                foreach (var change in copied)
                {
                    if (change.Replacement == null)
                    {
                        connection.Execute(
                            "DELETE FROM geoip_secrets WHERE secret_key = @SecretKey;",
                            new { change.SecretKey });
                        continue;
                    }

                    var replacement = change.Replacement;
                    connection.Execute(
                        @"INSERT INTO geoip_secrets (
                              secret_key, secret_value, fingerprint, updated_utc)
                          VALUES (@SecretKey, @SecretValue, @Fingerprint, @UpdatedUtc)
                          ON CONFLICT(secret_key) DO UPDATE SET
                              secret_value = excluded.secret_value,
                              fingerprint = excluded.fingerprint,
                              updated_utc = excluded.updated_utc;",
                        new
                        {
                            replacement.SecretKey,
                            replacement.SecretValue,
                            replacement.Fingerprint,
                            UpdatedUtc = Milliseconds(replacement.UpdatedAtUtc)
                        });
                }
            });
        }

        public GeoIpSecretValue? GetSecret(string secretKey)
        {
            RequireText(secretKey, nameof(secretKey));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<SecretRow>(
                @"SELECT secret_key, secret_value, fingerprint, updated_utc
                  FROM geoip_secrets WHERE secret_key = @SecretKey;",
                new { SecretKey = secretKey });
            return row == null
                ? null
                : new GeoIpSecretValue(
                    row.secret_key,
                    row.secret_value,
                    row.fingerprint,
                    Utc(row.updated_utc));
        }

        public IReadOnlyList<GeoIpSecretMetadata> ListSecretMetadata()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<SecretMetadataRow>(
                    @"SELECT secret_key, fingerprint, updated_utc
                      FROM geoip_secrets ORDER BY secret_key;")
                .Select(row => new GeoIpSecretMetadata(
                    row.secret_key,
                    row.fingerprint,
                    Utc(row.updated_utc)))
                .ToArray();
        }

        public void ReplaceNetworkRules(IReadOnlyList<GeoIpNetworkRule> rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var copied = rules.ToArray();
            using var connection = connectionFactory.Open();
            ExecuteImmediate(connection, () =>
            {
                connection.Execute("DELETE FROM geoip_network_rules;");
                foreach (var rule in copied)
                {
                    if (rule == null) throw new ArgumentException("Rules cannot contain null.", nameof(rules));
                    RequireText(rule.RuleId, nameof(rules));
                    RequireText(rule.NetworkCidr, nameof(rules));
                    RequireText(rule.Effect, nameof(rules));
                    connection.Execute(
                        @"INSERT INTO geoip_network_rules (
                              rule_id, network_cidr, effect, ordinal)
                          VALUES (@RuleId, @NetworkCidr, @Effect, @Ordinal);",
                        rule);
                }
            });
        }

        public IReadOnlyList<GeoIpNetworkRule> ListNetworkRules()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<NetworkRuleRow>(
                    @"SELECT rule_id, network_cidr, effect, ordinal
                      FROM geoip_network_rules ORDER BY ordinal, rule_id;")
                .Select(row => new GeoIpNetworkRule(
                    row.rule_id,
                    row.network_cidr,
                    row.effect,
                    row.ordinal))
                .ToArray();
        }

        public void ReplaceCountryRules(IReadOnlyList<GeoIpCountryRule> rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var copied = rules.ToArray();
            using var connection = connectionFactory.Open();
            ExecuteImmediate(connection, () =>
            {
                connection.Execute("DELETE FROM geoip_country_rules;");
                foreach (var rule in copied)
                {
                    if (rule == null) throw new ArgumentException("Rules cannot contain null.", nameof(rules));
                    RequireText(rule.CountryCode, nameof(rules));
                    RequireText(rule.Effect, nameof(rules));
                    connection.Execute(
                        @"INSERT INTO geoip_country_rules (country_code, effect)
                          VALUES (@CountryCode, @Effect);",
                        new
                        {
                            CountryCode = rule.CountryCode.ToUpperInvariant(),
                            rule.Effect
                        });
                }
            });
        }

        public IReadOnlyList<GeoIpCountryRule> ListCountryRules()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<CountryRuleRow>(
                    @"SELECT country_code, effect
                      FROM geoip_country_rules ORDER BY country_code;")
                .Select(row => new GeoIpCountryRule(row.country_code, row.effect))
                .ToArray();
        }

        public void UpsertCache(GeoIpCacheEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            RequireText(entry.LookupStatus, nameof(entry));
            RequireText(entry.Source, nameof(entry));
            RequireUtc(entry.QueriedAtUtc, nameof(entry));
            RequireUtc(entry.ExpiresAtUtc, nameof(entry));
            if (entry.ExpiresAtUtc < entry.QueriedAtUtc)
                throw new ArgumentException("Cache expiry precedes its query time.", nameof(entry));
            var canonicalIp = Canonicalize(entry.CanonicalIp);
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO geoip_cache (
                      canonical_ip, lookup_status, country_code, source,
                      source_version, queried_utc, expires_utc)
                  VALUES (
                      @CanonicalIp, @LookupStatus, @CountryCode, @Source,
                      @SourceVersion, @QueriedUtc, @ExpiresUtc)
                  ON CONFLICT(canonical_ip) DO UPDATE SET
                      lookup_status = excluded.lookup_status,
                      country_code = excluded.country_code,
                      source = excluded.source,
                      source_version = excluded.source_version,
                      queried_utc = excluded.queried_utc,
                      expires_utc = excluded.expires_utc;",
                new
                {
                    CanonicalIp = canonicalIp,
                    entry.LookupStatus,
                    CountryCode = entry.CountryCode?.ToUpperInvariant(),
                    entry.Source,
                    entry.SourceVersion,
                    QueriedUtc = Milliseconds(entry.QueriedAtUtc),
                    ExpiresUtc = Milliseconds(entry.ExpiresAtUtc)
                });
        }

        public GeoIpCacheEntry? FindCache(string ipAddress)
        {
            var canonicalIp = Canonicalize(ipAddress);
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<CacheRow>(
                @"SELECT canonical_ip, lookup_status, country_code, source,
                         source_version, queried_utc, expires_utc
                  FROM geoip_cache WHERE canonical_ip = @CanonicalIp;",
                new { CanonicalIp = canonicalIp });
            return row == null ? null : Map(row);
        }

        public void RecordDecision(GeoIpDecision decision)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            RequireText(decision.DecisionId, nameof(decision));
            RequireText(decision.MaskedIp, nameof(decision));
            RequireText(decision.Decision, nameof(decision));
            RequireText(decision.ReasonCode, nameof(decision));
            RequireText(decision.LookupStatus, nameof(decision));
            RequireUtc(decision.OccurredAtUtc, nameof(decision));
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO geoip_decisions (
                      decision_id, occurred_utc, masked_ip, crossplatform_id,
                      decision, reason_code, lookup_status)
                  VALUES (
                      @DecisionId, @OccurredUtc, @MaskedIp, @CrossplatformId,
                      @Decision, @ReasonCode, @LookupStatus);",
                new
                {
                    decision.DecisionId,
                    OccurredUtc = Milliseconds(decision.OccurredAtUtc),
                    decision.MaskedIp,
                    decision.CrossplatformId,
                    decision.Decision,
                    decision.ReasonCode,
                    decision.LookupStatus
                });
        }

        public GeoIpDecisionPage QueryDecisions(GeoIpDecisionQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.PageSize <= 0 || query.PageSize > 200)
                throw new ArgumentOutOfRangeException(nameof(query));
            if (query.Keyset != null) RequireUtc(query.Keyset.OccurredAtUtc, nameof(query));
            using var connection = connectionFactory.Open();
            var rows = connection.Query<DecisionRow>(
                    @"SELECT decision_id, occurred_utc, masked_ip, crossplatform_id,
                             decision, reason_code, lookup_status
                      FROM geoip_decisions
                      WHERE @HasKeyset = 0
                         OR occurred_utc < @CursorUtc
                         OR (occurred_utc = @CursorUtc AND decision_id < @CursorId)
                      ORDER BY occurred_utc DESC, decision_id DESC
                      LIMIT @Take;",
                    new
                    {
                        HasKeyset = query.Keyset == null ? 0 : 1,
                        CursorUtc = query.Keyset == null
                            ? 0
                            : Milliseconds(query.Keyset.OccurredAtUtc),
                        CursorId = query.Keyset?.DecisionId ?? string.Empty,
                        Take = query.PageSize + 1
                    })
                .ToArray();
            var hasNext = rows.Length > query.PageSize;
            var pageRows = rows.Take(query.PageSize).ToArray();
            var decisions = pageRows.Select(Map).ToArray();
            var next = hasNext
                ? new GeoIpDecisionKeyset(
                    Utc(pageRows[pageRows.Length - 1].occurred_utc),
                    pageRows[pageRows.Length - 1].decision_id)
                : null;
            return new GeoIpDecisionPage(decisions, next);
        }

        private static string Canonicalize(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var parsed))
                throw new FormatException("The IP address is invalid.");
            if (parsed.IsIPv4MappedToIPv6) parsed = parsed.MapToIPv4();
            return parsed.ToString().ToLowerInvariant();
        }

        private static GeoIpFailureMode ParseFailureMode(string value) =>
            Enum.TryParse<GeoIpFailureMode>(value, out var parsed)
                ? parsed
                : throw new InvalidOperationException("geoip_failure_mode_invalid");

        private static GeoIpCacheEntry Map(CacheRow row) => new(
            row.canonical_ip,
            row.lookup_status,
            row.country_code,
            row.source,
            row.source_version,
            Utc(row.queried_utc),
            Utc(row.expires_utc));

        private static GeoIpDecision Map(DecisionRow row) => new(
            row.decision_id,
            Utc(row.occurred_utc),
            row.masked_ip,
            row.crossplatform_id,
            row.decision,
            row.reason_code,
            row.lookup_status);

        private static T ExecuteImmediate<T>(SqliteConnection connection, Func<T> action)
        {
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var result = action();
                connection.Execute("COMMIT;");
                return result;
            }
            catch
            {
                connection.Execute("ROLLBACK;");
                throw;
            }
        }

        private static void ExecuteImmediate(SqliteConnection connection, Action action) =>
            ExecuteImmediate(connection, () =>
            {
                action();
                return true;
            });

        private static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A value is required.", parameterName);
        }

        private static void RequireApprovedSecretKey(string secretKey, string parameterName)
        {
            RequireText(secretKey, parameterName);
            if (!string.Equals(secretKey, GeoIpSecretKeys.MaxMindAccountId, StringComparison.Ordinal) &&
                !string.Equals(secretKey, GeoIpSecretKeys.MaxMindLicenseKey, StringComparison.Ordinal))
                throw new ArgumentException("The GeoIP secret key is not approved.", parameterName);
        }

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }

        private static long Milliseconds(DateTimeOffset value)
        {
            RequireUtc(value, nameof(value));
            return value.ToUnixTimeMilliseconds();
        }

        private static DateTimeOffset Utc(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);

        private sealed class SettingsRow
        {
            public long version { get; set; }
            public long enabled { get; set; }
            public string provider { get; set; } = string.Empty;
            public string failure_mode { get; set; } = string.Empty;
            public long bypass_admins { get; set; }
            public string rejection_message { get; set; } = string.Empty;
        }

        private sealed class SecretRow
        {
            public string secret_key { get; set; } = string.Empty;
            public string secret_value { get; set; } = string.Empty;
            public string fingerprint { get; set; } = string.Empty;
            public long updated_utc { get; set; }
        }

        private sealed class SecretMetadataRow
        {
            public string secret_key { get; set; } = string.Empty;
            public string fingerprint { get; set; } = string.Empty;
            public long updated_utc { get; set; }
        }

        private sealed class NetworkRuleRow
        {
            public string rule_id { get; set; } = string.Empty;
            public string network_cidr { get; set; } = string.Empty;
            public string effect { get; set; } = string.Empty;
            public int ordinal { get; set; }
        }

        private sealed class CountryRuleRow
        {
            public string country_code { get; set; } = string.Empty;
            public string effect { get; set; } = string.Empty;
        }

        private sealed class CacheRow
        {
            public string canonical_ip { get; set; } = string.Empty;
            public string lookup_status { get; set; } = string.Empty;
            public string? country_code { get; set; }
            public string source { get; set; } = string.Empty;
            public string? source_version { get; set; }
            public long queried_utc { get; set; }
            public long expires_utc { get; set; }
        }

        private sealed class DecisionRow
        {
            public string decision_id { get; set; } = string.Empty;
            public long occurred_utc { get; set; }
            public string masked_ip { get; set; } = string.Empty;
            public string? crossplatform_id { get; set; }
            public string decision { get; set; } = string.Empty;
            public string reason_code { get; set; } = string.Empty;
            public string lookup_status { get; set; } = string.Empty;
        }
    }
}
