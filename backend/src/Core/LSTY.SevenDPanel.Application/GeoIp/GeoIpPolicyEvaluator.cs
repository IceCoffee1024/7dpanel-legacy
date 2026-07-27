using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application.GeoIp
{
    public sealed class GeoIpPolicyEvaluator
    {
        public GeoIpPolicyDecision Evaluate(
            GeoIpAccessPolicySettings settings,
            IReadOnlyList<GeoIpNetworkRule> networkRules,
            IReadOnlyList<GeoIpCountryRule> countryRules,
            string ipAddress,
            bool isConfirmedNativeAdministrator,
            GeoIpLookupResult lookup)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (networkRules == null) throw new ArgumentNullException(nameof(networkRules));
            if (countryRules == null) throw new ArgumentNullException(nameof(countryRules));
            if (lookup == null) throw new ArgumentNullException(nameof(lookup));

            if (!settings.IsEnabled)
                return Allow("disabled", lookup.Status);

            if (!GeoIpAddressNormalizer.TryNormalize(ipAddress, out var normalized))
                return ApplyFailureMode(settings, GeoIpLookupStatus.Invalid);
            if (normalized!.IsPrivate)
                return ApplyFailureMode(settings, GeoIpLookupStatus.Private);

            if (Matches(networkRules, normalized.Address, "Deny"))
                return Deny(settings, "network_deny", lookup.Status);

            if (settings.BypassAdmins && isConfirmedNativeAdministrator)
                return Allow("native_admin_bypass", lookup.Status);

            if (Matches(networkRules, normalized.Address, "Allow"))
                return Allow("network_allow", lookup.Status);

            if (lookup.Status == GeoIpLookupStatus.Found &&
                !string.IsNullOrWhiteSpace(lookup.CountryCode))
            {
                if (Matches(countryRules, lookup.CountryCode!, "Deny"))
                    return Deny(settings, "country_deny", lookup.Status);
                if (Matches(countryRules, lookup.CountryCode!, "Allow"))
                    return Allow("country_allow", lookup.Status);
            }

            return ApplyFailureMode(settings, lookup.Status);
        }

        private static bool Matches(
            IReadOnlyList<GeoIpNetworkRule> rules,
            System.Net.IPAddress address,
            string effect)
        {
            foreach (var rule in rules)
            {
                if (rule == null ||
                    !string.Equals(rule.Effect, effect, StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    if (GeoIpNetwork.Parse(rule.NetworkCidr).Contains(address)) return true;
                }
                catch (FormatException)
                {
                }
            }
            return false;
        }

        private static bool Matches(
            IReadOnlyList<GeoIpCountryRule> rules,
            string countryCode,
            string effect)
        {
            foreach (var rule in rules)
            {
                if (rule != null &&
                    string.Equals(rule.Effect, effect, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rule.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static GeoIpPolicyDecision ApplyFailureMode(
            GeoIpAccessPolicySettings settings,
            GeoIpLookupStatus status) =>
            settings.FailureMode == GeoIpFailureMode.FailOpen
                ? Allow("failure_mode_fail_open", status)
                : Deny(settings, "failure_mode_fail_closed", status);

        private static GeoIpPolicyDecision Allow(string reasonCode, GeoIpLookupStatus status) =>
            new GeoIpPolicyDecision(true, reasonCode, status, null);

        private static GeoIpPolicyDecision Deny(
            GeoIpAccessPolicySettings settings,
            string reasonCode,
            GeoIpLookupStatus status) =>
            new GeoIpPolicyDecision(
                false,
                reasonCode,
                status,
                string.IsNullOrWhiteSpace(settings.RejectionMessage)
                    ? GeoIpPolicyDecision.DefaultRejectionMessage
                    : settings.RejectionMessage.Trim());
    }
}
