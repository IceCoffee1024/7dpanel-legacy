using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.GeoIp
{
    public enum GeoIpLookupStatus
    {
        Found,
        Unknown,
        Private,
        Invalid,
        Unavailable
    }

    public enum GeoIpLookupFailure
    {
        None,
        Credentials,
        Permission,
        Quota,
        Http,
        Database,
        File,
        Io,
        Unexpected
    }

    public enum GeoIpDiagnosticSeverity
    {
        Information,
        Warning,
        Error
    }

    public static class GeoIpProviderNames
    {
        public const string LocalMmdb = "LocalMmdb";
        public const string MaxMindWebService = "MaxMindWebService";

        public static bool IsApproved(string? provider) =>
            string.Equals(provider, LocalMmdb, StringComparison.Ordinal) ||
            string.Equals(provider, MaxMindWebService, StringComparison.Ordinal);
    }

    public static class GeoIpSecretKeys
    {
        public const string MaxMindAccountId = "maxmind.account-id";
        public const string MaxMindLicenseKey = "maxmind.license-key";
    }

    public sealed record GeoIpProviderMetadata(
        string Provider,
        bool IsExternal,
        string? SourceVersion,
        string? BuildEpoch);

    public sealed record GeoIpLookupResult(
        GeoIpLookupStatus Status,
        string? CountryCode,
        string Source,
        string? SourceVersion,
        GeoIpLookupFailure Failure)
    {
        public static GeoIpLookupResult Found(
            string countryCode,
            string source,
            string? sourceVersion) =>
            new GeoIpLookupResult(
                GeoIpLookupStatus.Found,
                NormalizeCountry(countryCode),
                RequireSource(source),
                NormalizeOptional(sourceVersion),
                GeoIpLookupFailure.None);

        public static GeoIpLookupResult Unknown(string source, string? sourceVersion) =>
            new GeoIpLookupResult(
                GeoIpLookupStatus.Unknown,
                null,
                RequireSource(source),
                NormalizeOptional(sourceVersion),
                GeoIpLookupFailure.None);

        public static GeoIpLookupResult Private() =>
            new GeoIpLookupResult(
                GeoIpLookupStatus.Private,
                null,
                "Input",
                null,
                GeoIpLookupFailure.None);

        public static GeoIpLookupResult Invalid() =>
            new GeoIpLookupResult(
                GeoIpLookupStatus.Invalid,
                null,
                "Input",
                null,
                GeoIpLookupFailure.None);

        public static GeoIpLookupResult Unavailable(
            string source,
            GeoIpLookupFailure failure,
            string? sourceVersion = null) =>
            new GeoIpLookupResult(
                GeoIpLookupStatus.Unavailable,
                null,
                RequireSource(source),
                NormalizeOptional(sourceVersion),
                failure == GeoIpLookupFailure.None ? GeoIpLookupFailure.Unexpected : failure);

        public static GeoIpLookupResult FromCache(GeoIpCacheEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (!Enum.TryParse<GeoIpLookupStatus>(entry.LookupStatus, true, out var status))
                return Unavailable("Cache", GeoIpLookupFailure.Unexpected);
            if (status == GeoIpLookupStatus.Found && string.IsNullOrWhiteSpace(entry.CountryCode))
                status = GeoIpLookupStatus.Unknown;
            return new GeoIpLookupResult(
                status,
                status == GeoIpLookupStatus.Found
                    ? NormalizeCountry(entry.CountryCode!)
                    : null,
                RequireSource(entry.Source),
                NormalizeOptional(entry.SourceVersion),
                status == GeoIpLookupStatus.Unavailable
                    ? GeoIpLookupFailure.Unexpected
                    : GeoIpLookupFailure.None);
        }

        private static string NormalizeCountry(string countryCode)
        {
            if (string.IsNullOrWhiteSpace(countryCode))
                throw new ArgumentException("A country code is required.", nameof(countryCode));
            return countryCode.Trim().ToUpperInvariant();
        }

        private static string RequireSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("A lookup source is required.", nameof(source));
            return source.Trim();
        }

        private static string? NormalizeOptional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    public interface IGeoIpProvider : IDisposable
    {
        GeoIpProviderMetadata Metadata { get; }

        Task<GeoIpLookupResult> LookupAsync(
            string canonicalIp,
            CancellationToken cancellationToken);
    }

    public sealed record GeoIpRefreshRequest(
        string Provider,
        string CanonicalIp,
        long SettingsVersion,
        DateTimeOffset RequestedAtUtc);

    public interface IGeoIpRefreshQueue
    {
        bool TryWrite(GeoIpRefreshRequest request);
    }

    public sealed record GeoIpRefreshDiagnostics(
        bool IsAccepting,
        int QueueDepth,
        long RejectedCount,
        DateTimeOffset? LastCompletedAtUtc,
        GeoIpLookupStatus? LastLookupStatus,
        IReadOnlyList<GeoIpProviderMetadata> Providers);

    public interface IGeoIpRefreshDiagnostics
    {
        GeoIpRefreshDiagnostics GetDiagnostics();
    }

    public sealed record GeoIpJoinAttempt(
        string IpAddress,
        string? CrossplatformId,
        bool IsConfirmedNativeAdministrator);

    public sealed record GeoIpPolicyDecision(
        bool IsAllowed,
        string ReasonCode,
        GeoIpLookupStatus LookupStatus,
        string? RejectionMessage,
        bool WasCacheHit = false,
        bool RefreshEnqueued = false)
    {
        public const string DefaultRejectionMessage = "Connection denied by server policy.";

        public GeoIpPolicyDecision WithCacheState(bool wasCacheHit, bool refreshEnqueued) =>
            this with { WasCacheHit = wasCacheHit, RefreshEnqueued = refreshEnqueued };
    }

    public sealed record GeoIpDiagnosticsSnapshot(
        bool IsEnabled,
        GeoIpFailureMode FailureMode,
        string Provider,
        GeoIpDiagnosticSeverity Severity,
        string StatusCode,
        int QueueDepth,
        long RejectedRefreshCount,
        DateTimeOffset? LastCompletedAtUtc,
        GeoIpLookupStatus? LastLookupStatus,
        IReadOnlyList<GeoIpProviderMetadata> Providers);

    public sealed record GeoIpNormalizedAddress(
        IPAddress Address,
        string CanonicalIp,
        bool IsPrivate);

    public static class GeoIpAddressNormalizer
    {
        public static bool TryNormalize(string? value, out GeoIpNormalizedAddress? normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(value) ||
                !IPAddress.TryParse(value!.Trim(), out var address))
                return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            normalized = new GeoIpNormalizedAddress(
                address,
                address.ToString().ToLowerInvariant(),
                IsPrivate(address));
            return true;
        }

        public static string Mask(string? value)
        {
            if (!TryNormalize(value, out var normalized)) return "invalid";
            if (normalized!.Address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = normalized.Address.GetAddressBytes();
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.{1}.{2}.0/24",
                    bytes[0],
                    bytes[1],
                    bytes[2]);
            }

            var network = GeoIpNetwork.Create(normalized.Address, 48);
            return network.NetworkAddress.ToString().ToLowerInvariant() + "/48";
        }

        private static bool IsPrivate(IPAddress address)
        {
            if (IPAddress.IsLoopback(address)) return true;
            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return bytes[0] == 0 ||
                    bytes[0] == 10 ||
                    bytes[0] == 127 ||
                    (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                    (bytes[0] == 169 && bytes[1] == 254) ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    bytes[0] >= 224;
            }

            return address.Equals(IPAddress.IPv6None) ||
                address.Equals(IPAddress.IPv6Loopback) ||
                (bytes[0] & 0xfe) == 0xfc ||
                (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) ||
                bytes[0] == 0xff;
        }
    }

    public sealed class GeoIpNetwork
    {
        private readonly byte[] networkBytes;

        private GeoIpNetwork(IPAddress networkAddress, int prefixLength)
        {
            NetworkAddress = networkAddress;
            PrefixLength = prefixLength;
            networkBytes = networkAddress.GetAddressBytes();
        }

        public IPAddress NetworkAddress { get; }
        public int PrefixLength { get; }

        public static GeoIpNetwork Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException("The network is invalid.");
            var parts = value.Trim().Split('/');
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var address))
                throw new FormatException("The network is invalid.");
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            var maximum = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            var prefixLength = maximum;
            if (parts.Length == 2 &&
                (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefixLength) ||
                 prefixLength < 0 ||
                 prefixLength > maximum))
                throw new FormatException("The network prefix is invalid.");
            return Create(address, prefixLength);
        }

        public static GeoIpNetwork Create(IPAddress address, int prefixLength)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            var bytes = address.GetAddressBytes();
            var maximum = bytes.Length * 8;
            if (prefixLength < 0 || prefixLength > maximum)
                throw new ArgumentOutOfRangeException(nameof(prefixLength));
            var fullBytes = prefixLength / 8;
            var remainingBits = prefixLength % 8;
            if (remainingBits > 0)
                bytes[fullBytes] &= (byte)(0xff << (8 - remainingBits));
            var clearFrom = fullBytes + (remainingBits > 0 ? 1 : 0);
            for (var index = clearFrom; index < bytes.Length; index++) bytes[index] = 0;
            return new GeoIpNetwork(new IPAddress(bytes), prefixLength);
        }

        public bool Contains(IPAddress address)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            var candidate = address.GetAddressBytes();
            if (candidate.Length != networkBytes.Length) return false;
            var fullBytes = PrefixLength / 8;
            for (var index = 0; index < fullBytes; index++)
                if (candidate[index] != networkBytes[index]) return false;
            var remainingBits = PrefixLength % 8;
            if (remainingBits == 0) return true;
            var mask = (byte)(0xff << (8 - remainingBits));
            return (candidate[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
        }

        public override string ToString() =>
            NetworkAddress.ToString().ToLowerInvariant() + "/" +
            PrefixLength.ToString(CultureInfo.InvariantCulture);
    }
}
