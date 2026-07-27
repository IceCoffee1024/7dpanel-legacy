using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.GeoIp;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/access-policies/geoip")]
    public sealed class GeoIpAccessPoliciesController : ApiController
    {
        private const string GeoIpPath = "/api/v1/access-policies/geoip";
        private const int RecentDecisionLimit = 25;
        private readonly IGeoIpAccessPolicyStore store;
        private readonly IGeoIpRefreshQueue refreshQueue;
        private readonly GetGeoIpDiagnosticsUseCase diagnosticsUseCase;
        private readonly UpdateGeoIpCredentialsUseCase credentialsUseCase;

        public GeoIpAccessPoliciesController(
            IGeoIpAccessPolicyStore store,
            IGeoIpRefreshQueue refreshQueue,
            GetGeoIpDiagnosticsUseCase diagnosticsUseCase,
            UpdateGeoIpCredentialsUseCase? credentialsUseCase = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.refreshQueue = refreshQueue ?? throw new ArgumentNullException(nameof(refreshQueue));
            this.diagnosticsUseCase = diagnosticsUseCase ??
                throw new ArgumentNullException(nameof(diagnosticsUseCase));
            this.credentialsUseCase = credentialsUseCase ??
                new UpdateGeoIpCredentialsUseCase(this.store);
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(GeoIpPolicySummaryHttpResponse))]
        public HttpResponseMessage Get()
        {
            try
            {
                var settings = store.GetSettings() ?? GeoIpAccessPolicySettings.CreateDefault();
                var networkRules = MapNetworkRules(store.ListNetworkRules());
                var countryRules = MapCountryRules(store.ListCountryRules());
                var decisions = MapDecisions(
                    store.QueryDecisions(new GeoIpDecisionQuery(RecentDecisionLimit)).Decisions);
                var diagnostics = diagnosticsUseCase.Execute();
                var providers = MapProviders(diagnostics.Providers);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new GeoIpPolicySummaryHttpResponse(
                        settings.Version,
                        settings.IsEnabled,
                        SafeProvider(settings.Provider),
                        SafeFailureMode(settings.FailureMode),
                        settings.BypassAdmins,
                        SafeRejectionMessage(settings.RejectionMessage),
                        networkRules,
                        countryRules,
                        new GeoIpCacheHealthHttpResponse(
                            diagnostics.QueueDepth,
                            diagnostics.RejectedRefreshCount,
                            diagnostics.LastCompletedAtUtc,
                            diagnostics.LastLookupStatus?.ToString(),
                            diagnostics.Severity.ToString(),
                            SafeStatusCode(diagnostics.StatusCode)),
                        providers,
                        decisions));
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "geoip_read_unavailable",
                    "GeoIP policy information is temporarily unavailable.");
            }
        }

        [HttpPut]
        [Route("")]
        [ResponseType(typeof(GeoIpPolicyUpdateHttpResponse))]
        public HttpResponseMessage Put(GeoIpPolicyUpdateHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!TryBuildUpdate(request, out var settings, out var networkRules, out var countryRules))
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_geoip_policy",
                    "The GeoIP policy contains an invalid value.");
            }

            try
            {
                store.SaveSettings(settings!, request.ExpectedVersion!.Value);
                store.ReplaceNetworkRules(networkRules!);
                store.ReplaceCountryRules(countryRules!);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new GeoIpPolicyUpdateHttpResponse(settings!.Version, "updated"));
            }
            catch (GeoIpAccessPolicyVersionConflictException)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "geoip_settings_version_conflict",
                    "The GeoIP policy changed before the update completed.");
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "geoip_update_unavailable",
                    "The GeoIP policy could not be updated.");
            }
        }

        [HttpPut]
        [Route("credentials")]
        [ResponseType(typeof(GeoIpCredentialsUpdateHttpResponse))]
        public HttpResponseMessage PutCredentials(GeoIpCredentialsUpdateHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!TryBuildCredentialsUpdate(request, out var update))
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_geoip_credentials",
                    "The GeoIP credentials update contains an invalid value.");
            }

            try
            {
                var state = credentialsUseCase.Execute(
                    new GeoIpCredentialsActor(
                        User?.Identity?.Name ?? string.Empty,
                        User?.IsInRole("Owner") == true),
                    update!);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new GeoIpCredentialsUpdateHttpResponse(
                        MapCredential(state.AccountId),
                        MapCredential(state.LicenseKey)));
            }
            catch (GeoIpOwnerRequiredException)
            {
                return Problem(
                    HttpStatusCode.Forbidden,
                    "geoip_owner_required",
                    "Owner access is required to update GeoIP credentials.");
            }
            catch (ArgumentException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_geoip_credentials",
                    "The GeoIP credentials update contains an invalid value.");
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "geoip_credentials_unavailable",
                    "The GeoIP credentials could not be updated.");
            }
        }

        [HttpPost]
        [Route("test")]
        [ResponseType(typeof(GeoIpTestHttpResponse))]
        public HttpResponseMessage Test(GeoIpTestHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!GeoIpAddressNormalizer.TryNormalize(request.IpAddress, out var normalized) ||
                normalized!.IsPrivate)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_geoip_test_address",
                    "A valid public IP address is required.");
            }

            GeoIpAccessPolicySettings settings;
            try
            {
                settings = store.GetSettings() ?? GeoIpAccessPolicySettings.CreateDefault();
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "geoip_refresh_unavailable",
                    "The GeoIP refresh queue is unavailable.");
            }

            if (!settings.IsEnabled)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "geoip_policy_disabled",
                    "The GeoIP policy is disabled.");
            }
            if (!GeoIpProviderNames.IsApproved(settings.Provider))
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "geoip_refresh_unavailable",
                    "The GeoIP refresh queue is unavailable.");
            }

            bool accepted;
            try
            {
                accepted = refreshQueue.TryWrite(new GeoIpRefreshRequest(
                    settings.Provider,
                    normalized.CanonicalIp,
                    settings.Version,
                    DateTimeOffset.UtcNow));
            }
            catch
            {
                accepted = false;
            }
            if (!accepted)
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "geoip_refresh_unavailable",
                    "The GeoIP refresh queue is unavailable.");
            }

            return Request.CreateResponse(
                HttpStatusCode.Accepted,
                new GeoIpTestHttpResponse(
                    true,
                    GeoIpAddressNormalizer.Mask(normalized.CanonicalIp),
                    "queued"));
        }

        [HttpGet]
        [Route("diagnostics")]
        [ResponseType(typeof(GeoIpDiagnosticsHttpResponse))]
        public HttpResponseMessage GetDiagnostics()
        {
            try
            {
                var diagnostics = diagnosticsUseCase.Execute();
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new GeoIpDiagnosticsHttpResponse(
                        diagnostics.IsEnabled,
                        SafeFailureMode(diagnostics.FailureMode),
                        SafeProvider(diagnostics.Provider),
                        diagnostics.Severity.ToString(),
                        SafeStatusCode(diagnostics.StatusCode),
                        diagnostics.QueueDepth,
                        diagnostics.RejectedRefreshCount,
                        diagnostics.LastCompletedAtUtc,
                        diagnostics.LastLookupStatus?.ToString(),
                        MapProviders(diagnostics.Providers)));
            }
            catch
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "geoip_diagnostics_unavailable",
                    "GeoIP diagnostics are temporarily unavailable.");
            }
        }

        private static bool TryBuildUpdate(
            GeoIpPolicyUpdateHttpRequest request,
            out GeoIpAccessPolicySettings? settings,
            out IReadOnlyList<GeoIpNetworkRule>? networkRules,
            out IReadOnlyList<GeoIpCountryRule>? countryRules)
        {
            settings = null;
            networkRules = null;
            countryRules = null;
            if (!request.ExpectedVersion.HasValue ||
                request.ExpectedVersion.Value < 0 ||
                request.ExpectedVersion.Value == long.MaxValue ||
                !request.IsEnabled.HasValue ||
                !request.BypassAdmins.HasValue ||
                !GeoIpProviderNames.IsApproved(request.Provider) ||
                !Enum.TryParse(request.FailureMode, true, out GeoIpFailureMode failureMode) ||
                !Enum.IsDefined(typeof(GeoIpFailureMode), failureMode) ||
                !TryNormalizeRejectionMessage(request.RejectionMessage, out var rejectionMessage) ||
                request.NetworkRules == null ||
                request.CountryRules == null ||
                !TryBuildNetworkRules(request.NetworkRules, out networkRules) ||
                !TryBuildCountryRules(request.CountryRules, out countryRules))
                return false;

            settings = new GeoIpAccessPolicySettings(
                request.ExpectedVersion.Value + 1,
                request.IsEnabled.Value,
                request.Provider!,
                failureMode,
                request.BypassAdmins.Value,
                rejectionMessage!);
            return true;
        }

        private static bool TryBuildCredentialsUpdate(
            GeoIpCredentialsUpdateHttpRequest request,
            out GeoIpCredentialsUpdate? update)
        {
            update = null;
            if (!TryBuildSecretUpdate(request.AccountId, out var accountId) ||
                !TryBuildSecretUpdate(request.LicenseKey, out var licenseKey))
                return false;
            update = new GeoIpCredentialsUpdate(accountId!, licenseKey!);
            return true;
        }

        private static bool TryBuildSecretUpdate(
            GeoIpSecretUpdateHttpRequest? request,
            out GeoIpSecretUpdate? update)
        {
            update = null;
            if (request == null ||
                !Enum.TryParse<GeoIpSecretUpdateOperation>(
                    request.Operation,
                    ignoreCase: true,
                    out var operation) ||
                !Enum.IsDefined(typeof(GeoIpSecretUpdateOperation), operation))
                return false;

            switch (operation)
            {
                case GeoIpSecretUpdateOperation.Keep:
                    if (request.Value != null) return false;
                    update = GeoIpSecretUpdate.Keep();
                    return true;
                case GeoIpSecretUpdateOperation.Clear:
                    if (request.Value != null) return false;
                    update = GeoIpSecretUpdate.Clear();
                    return true;
                case GeoIpSecretUpdateOperation.Replace:
                    if (string.IsNullOrWhiteSpace(request.Value)) return false;
                    update = GeoIpSecretUpdate.Replace(request.Value!);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryBuildNetworkRules(
            IEnumerable<GeoIpNetworkRuleHttpRequest?> requests,
            out IReadOnlyList<GeoIpNetworkRule>? rules)
        {
            var result = new List<GeoIpNetworkRule>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var networks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var request in requests)
            {
                if (request == null ||
                    !IsSafeToken(request.RuleId) ||
                    !request.Ordinal.HasValue ||
                    request.Ordinal.Value < 0 ||
                    !TryNormalizeEffect(request.Effect, out var effect))
                {
                    rules = null;
                    return false;
                }

                string network;
                try { network = GeoIpNetwork.Parse(request.NetworkCidr!).ToString(); }
                catch
                {
                    rules = null;
                    return false;
                }
                var ruleId = request.RuleId!.Trim();
                if (!ids.Add(ruleId) || !networks.Add(network))
                {
                    rules = null;
                    return false;
                }
                result.Add(new GeoIpNetworkRule(
                    ruleId,
                    network,
                    effect!,
                    request.Ordinal.Value));
            }
            rules = result;
            return true;
        }

        private static bool TryBuildCountryRules(
            IEnumerable<GeoIpCountryRuleHttpRequest?> requests,
            out IReadOnlyList<GeoIpCountryRule>? rules)
        {
            var result = new List<GeoIpCountryRule>();
            var countries = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in requests)
            {
                if (request == null ||
                    !TryNormalizeCountry(request.CountryCode, out var country) ||
                    !TryNormalizeEffect(request.Effect, out var effect) ||
                    !countries.Add(country!))
                {
                    rules = null;
                    return false;
                }
                result.Add(new GeoIpCountryRule(country!, effect!));
            }
            rules = result;
            return true;
        }

        private static GeoIpNetworkRuleHttpResponse[] MapNetworkRules(
            IReadOnlyList<GeoIpNetworkRule>? rules)
        {
            if (rules == null) return Array.Empty<GeoIpNetworkRuleHttpResponse>();
            var result = new List<GeoIpNetworkRuleHttpResponse>();
            foreach (var rule in rules)
            {
                if (rule == null ||
                    !IsSafeToken(rule.RuleId) ||
                    !TryNormalizeEffect(rule.Effect, out var effect))
                    continue;
                try
                {
                    result.Add(new GeoIpNetworkRuleHttpResponse(
                        rule.RuleId.Trim(),
                        GeoIpNetwork.Parse(rule.NetworkCidr).ToString(),
                        effect!,
                        rule.Ordinal));
                }
                catch
                {
                }
            }
            return result.ToArray();
        }

        private static GeoIpCountryRuleHttpResponse[] MapCountryRules(
            IReadOnlyList<GeoIpCountryRule>? rules)
        {
            if (rules == null) return Array.Empty<GeoIpCountryRuleHttpResponse>();
            var result = new List<GeoIpCountryRuleHttpResponse>();
            foreach (var rule in rules)
            {
                if (rule == null ||
                    !TryNormalizeCountry(rule.CountryCode, out var country) ||
                    !TryNormalizeEffect(rule.Effect, out var effect))
                    continue;
                result.Add(new GeoIpCountryRuleHttpResponse(country!, effect!));
            }
            return result.ToArray();
        }

        private static GeoIpDecisionHttpResponse[] MapDecisions(
            IReadOnlyList<GeoIpDecision>? decisions)
        {
            if (decisions == null) return Array.Empty<GeoIpDecisionHttpResponse>();
            return decisions.Select(decision => new GeoIpDecisionHttpResponse(
                    decision.OccurredAtUtc,
                    SafeMaskedIp(decision.MaskedIp),
                    string.Equals(decision.Decision, "Allow", StringComparison.OrdinalIgnoreCase)
                        ? "Allow"
                        : string.Equals(decision.Decision, "Deny", StringComparison.OrdinalIgnoreCase)
                            ? "Deny"
                            : "Unknown",
                    IsSafeToken(decision.ReasonCode) ? decision.ReasonCode : "unknown",
                    Enum.TryParse(decision.LookupStatus, true, out GeoIpLookupStatus lookupStatus) &&
                    Enum.IsDefined(typeof(GeoIpLookupStatus), lookupStatus)
                        ? lookupStatus.ToString()
                        : GeoIpLookupStatus.Unavailable.ToString()))
                .ToArray();
        }

        private static GeoIpProviderHttpResponse[] MapProviders(
            IReadOnlyList<GeoIpProviderMetadata>? providers)
        {
            if (providers == null) return Array.Empty<GeoIpProviderHttpResponse>();
            return providers
                .Where(provider => provider != null && GeoIpProviderNames.IsApproved(provider.Provider))
                .Select(provider => new GeoIpProviderHttpResponse(
                    provider.Provider,
                    provider.IsExternal,
                    SafeOpaqueVersion(provider.SourceVersion),
                    SafeOpaqueVersion(provider.BuildEpoch)))
                .ToArray();
        }

        private static GeoIpCredentialHttpResponse MapCredential(GeoIpCredentialState state) =>
            new GeoIpCredentialHttpResponse(
                state.IsSet,
                state.Fingerprint,
                state.UpdatedAtUtc);

        private static bool TryNormalizeEffect(string? value, out string? effect)
        {
            effect = null;
            if (string.Equals(value?.Trim(), "Allow", StringComparison.OrdinalIgnoreCase))
                effect = "Allow";
            else if (string.Equals(value?.Trim(), "Deny", StringComparison.OrdinalIgnoreCase))
                effect = "Deny";
            return effect != null;
        }

        private static bool TryNormalizeCountry(string? value, out string? country)
        {
            country = value?.Trim().ToUpperInvariant();
            return country != null &&
                country.Length == 2 &&
                country.All(character => character >= 'A' && character <= 'Z');
        }

        private static bool TryNormalizeRejectionMessage(string? value, out string? message)
        {
            message = value?.Trim();
            return message != null &&
                !string.IsNullOrWhiteSpace(message) &&
                message.Length <= 256 &&
                !message.Any(char.IsControl);
        }

        private static bool IsSafeToken(string? value)
        {
            value = value?.Trim();
            return value != null &&
                !string.IsNullOrWhiteSpace(value) &&
                value.Length <= 64 &&
                value.All(character =>
                    character >= 'a' && character <= 'z' ||
                    character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9' ||
                    character == '_' || character == '-' || character == '.');
        }

        private static string? SafeOpaqueVersion(string? value) =>
            IsSafeToken(value) ? value!.Trim() : null;

        private static string SafeMaskedIp(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "invalid";
            if (value!.IndexOf('/') < 0)
                return GeoIpAddressNormalizer.Mask(value);
            try
            {
                var network = GeoIpNetwork.Parse(value);
                var expectedPrefix = network.NetworkAddress.GetAddressBytes().Length == 4 ? 24 : 48;
                return network.PrefixLength == expectedPrefix ? network.ToString() : "invalid";
            }
            catch
            {
                return "invalid";
            }
        }

        private static string SafeProvider(string? provider) =>
            GeoIpProviderNames.IsApproved(provider) ? provider! : "Unavailable";

        private static string SafeFailureMode(GeoIpFailureMode failureMode) =>
            Enum.IsDefined(typeof(GeoIpFailureMode), failureMode)
                ? failureMode.ToString()
                : GeoIpFailureMode.FailOpen.ToString();

        private static string SafeRejectionMessage(string? message) =>
            TryNormalizeRejectionMessage(message, out var normalized)
                ? normalized!
                : GeoIpPolicyDecision.DefaultRejectionMessage;

        private static string SafeStatusCode(string? value) =>
            IsSafeToken(value) ? value!.Trim() : "unavailable";

        private HttpResponseMessage Problem(
            HttpStatusCode status,
            string code,
            string detail) =>
            ApiProblemDetailsFactory.CreateResponse(
                Request,
                status,
                code,
                detail,
                GeoIpPath);
    }
}
