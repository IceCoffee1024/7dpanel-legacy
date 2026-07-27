using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.GeoIp;
using MaxMind.Db;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace LSTY.SevenDPanel.Adapters.Local.GeoIp
{
    public sealed class LocalMmdbGeoIpProvider : IGeoIpProvider
    {
        private DatabaseReader? reader;
        private readonly GeoIpLookupFailure initializationFailure;

        public LocalMmdbGeoIpProvider(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("A GeoIP database path is required.", nameof(databasePath));

            try
            {
                reader = new DatabaseReader(Path.GetFullPath(databasePath));
                var buildEpoch = new DateTimeOffset(reader.Metadata.BuildDate.ToUniversalTime())
                    .ToUnixTimeSeconds()
                    .ToString(CultureInfo.InvariantCulture);
                Metadata = new GeoIpProviderMetadata(
                    GeoIpProviderNames.LocalMmdb,
                    false,
                    Digest(reader.Metadata.DatabaseType + "|" + buildEpoch),
                    buildEpoch);
                initializationFailure = GeoIpLookupFailure.None;
            }
            catch (InvalidDatabaseException)
            {
                initializationFailure = GeoIpLookupFailure.Database;
                Metadata = UnavailableMetadata();
            }
            catch (FileNotFoundException)
            {
                initializationFailure = GeoIpLookupFailure.File;
                Metadata = UnavailableMetadata();
            }
            catch (DirectoryNotFoundException)
            {
                initializationFailure = GeoIpLookupFailure.File;
                Metadata = UnavailableMetadata();
            }
            catch (IOException)
            {
                initializationFailure = GeoIpLookupFailure.Io;
                Metadata = UnavailableMetadata();
            }
            catch (UnauthorizedAccessException)
            {
                initializationFailure = GeoIpLookupFailure.Io;
                Metadata = UnavailableMetadata();
            }
        }

        public GeoIpProviderMetadata Metadata { get; }

        public Task<GeoIpLookupResult> LookupAsync(
            string canonicalIp,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!GeoIpAddressNormalizer.TryNormalize(canonicalIp, out var normalized))
                return Task.FromResult(GeoIpLookupResult.Invalid());
            if (normalized!.IsPrivate)
                return Task.FromResult(GeoIpLookupResult.Private());
            if (reader == null)
                return Task.FromResult(GeoIpLookupResult.Unavailable(
                    GeoIpProviderNames.LocalMmdb,
                    initializationFailure));

            try
            {
                if (!reader.TryCountry(normalized.Address, out var response))
                    return Task.FromResult(GeoIpLookupResult.Unknown(
                        GeoIpProviderNames.LocalMmdb,
                        Metadata.SourceVersion));
                var isoCode = response.Country?.IsoCode;
                if (string.IsNullOrWhiteSpace(isoCode))
                    return Task.FromResult(GeoIpLookupResult.Unknown(
                        GeoIpProviderNames.LocalMmdb,
                        Metadata.SourceVersion));
                return Task.FromResult(GeoIpLookupResult.Found(
                    isoCode!,
                    GeoIpProviderNames.LocalMmdb,
                    Metadata.SourceVersion));
            }
            catch (AddressNotFoundException)
            {
                return Task.FromResult(GeoIpLookupResult.Unknown(
                    GeoIpProviderNames.LocalMmdb,
                    Metadata.SourceVersion));
            }
            catch (InvalidDatabaseException)
            {
                return Task.FromResult(Unavailable(GeoIpLookupFailure.Database));
            }
            catch (FileNotFoundException)
            {
                return Task.FromResult(Unavailable(GeoIpLookupFailure.File));
            }
            catch (IOException)
            {
                return Task.FromResult(Unavailable(GeoIpLookupFailure.Io));
            }
            catch
            {
                return Task.FromResult(Unavailable(GeoIpLookupFailure.Unexpected));
            }
        }

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref reader, null);
            current?.Dispose();
        }

        private GeoIpLookupResult Unavailable(GeoIpLookupFailure failure) =>
            GeoIpLookupResult.Unavailable(
                GeoIpProviderNames.LocalMmdb,
                failure,
                Metadata.SourceVersion);

        private static GeoIpProviderMetadata UnavailableMetadata() =>
            new GeoIpProviderMetadata(GeoIpProviderNames.LocalMmdb, false, null, null);

        private static string Digest(string value)
        {
            using var algorithm = SHA256.Create();
            var hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
            return BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
