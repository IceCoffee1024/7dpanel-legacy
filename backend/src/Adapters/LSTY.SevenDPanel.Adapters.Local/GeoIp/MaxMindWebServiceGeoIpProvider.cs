using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.GeoIp;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace LSTY.SevenDPanel.Adapters.Local.GeoIp
{
    public sealed class MaxMindWebServiceGeoIpProvider : IGeoIpProvider
    {
        private readonly object sync = new object();
        private readonly IGeoIpAccessPolicyStore store;
        private readonly Func<HttpMessageHandler>? httpMessageHandlerFactory;
        private WebServiceClient? client;
        private string? credentialVersion;
        private bool disposed;

        public MaxMindWebServiceGeoIpProvider(
            IGeoIpAccessPolicyStore store,
            Func<HttpMessageHandler>? httpMessageHandlerFactory = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.httpMessageHandlerFactory = httpMessageHandlerFactory;
            Metadata = new GeoIpProviderMetadata(
                GeoIpProviderNames.MaxMindWebService,
                true,
                null,
                null);
        }

        public GeoIpProviderMetadata Metadata { get; private set; }

        public async Task<GeoIpLookupResult> LookupAsync(
            string canonicalIp,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!GeoIpAddressNormalizer.TryNormalize(canonicalIp, out var normalized))
                return GeoIpLookupResult.Invalid();
            if (normalized!.IsPrivate) return GeoIpLookupResult.Private();

            WebServiceClient current;
            try
            {
                current = GetOrCreateClient();
            }
            catch (GeoIpCredentialsUnavailableException)
            {
                return Unavailable(GeoIpLookupFailure.Credentials);
            }

            try
            {
                var response = await current.CountryAsync(normalized.Address).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var isoCode = response.Country?.IsoCode;
                return string.IsNullOrWhiteSpace(isoCode)
                    ? GeoIpLookupResult.Unknown(
                        GeoIpProviderNames.MaxMindWebService,
                        Metadata.SourceVersion)
                    : GeoIpLookupResult.Found(
                        isoCode!,
                        GeoIpProviderNames.MaxMindWebService,
                        Metadata.SourceVersion);
            }
            catch (AddressNotFoundException)
            {
                return GeoIpLookupResult.Unknown(
                    GeoIpProviderNames.MaxMindWebService,
                    Metadata.SourceVersion);
            }
            catch (AuthenticationException)
            {
                return Unavailable(GeoIpLookupFailure.Credentials);
            }
            catch (PermissionRequiredException)
            {
                return Unavailable(GeoIpLookupFailure.Permission);
            }
            catch (OutOfQueriesException)
            {
                return Unavailable(GeoIpLookupFailure.Quota);
            }
            catch (HttpException)
            {
                return Unavailable(GeoIpLookupFailure.Http);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return Unavailable(GeoIpLookupFailure.Http);
            }
            catch
            {
                return Unavailable(GeoIpLookupFailure.Unexpected);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                client?.Dispose();
                client = null;
            }
        }

        private WebServiceClient GetOrCreateClient()
        {
            var account = store.GetSecret(GeoIpSecretKeys.MaxMindAccountId);
            var license = store.GetSecret(GeoIpSecretKeys.MaxMindLicenseKey);
            if (account == null ||
                license == null ||
                !int.TryParse(account.SecretValue, NumberStyles.None, CultureInfo.InvariantCulture, out var accountId) ||
                accountId <= 0 ||
                string.IsNullOrWhiteSpace(license.SecretValue))
                throw new GeoIpCredentialsUnavailableException();

            var version = account.Fingerprint + ":" + license.Fingerprint;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(MaxMindWebServiceGeoIpProvider));
                if (client != null && string.Equals(version, credentialVersion, StringComparison.Ordinal))
                    return client;
                client?.Dispose();
                var handler = httpMessageHandlerFactory?.Invoke();
                try
                {
                    client = new WebServiceClient(
                        accountId,
                        license.SecretValue,
                        httpMessageHandler: handler);
                }
                catch
                {
                    handler?.Dispose();
                    throw;
                }
                credentialVersion = version;
                Metadata = new GeoIpProviderMetadata(
                    GeoIpProviderNames.MaxMindWebService,
                    true,
                    null,
                    null);
                return client;
            }
        }

        private GeoIpLookupResult Unavailable(GeoIpLookupFailure failure) =>
            GeoIpLookupResult.Unavailable(
                GeoIpProviderNames.MaxMindWebService,
                failure,
                Metadata.SourceVersion);

        private sealed class GeoIpCredentialsUnavailableException : Exception
        {
        }
    }
}
