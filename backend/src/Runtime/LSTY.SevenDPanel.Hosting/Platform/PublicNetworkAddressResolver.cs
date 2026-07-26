using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Hosting.Platform
{
    public sealed class PublicNetworkAddressResolver
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(20);
        private readonly PanelOverviewOptions options;
        private readonly HttpClient client;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan timeout;
        private readonly object sync = new object();
        private HostPublicNetwork? cached;
        private DateTimeOffset cachedAtUtc;
        private Task<HostPublicNetwork>? inFlight;

        public PublicNetworkAddressResolver(
            PanelOverviewOptions options,
            HttpMessageHandler? handler = null,
            Func<DateTimeOffset>? utcNow = null,
            TimeSpan? timeout = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            client = handler == null ? new HttpClient() : new HttpClient(handler, false);
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            this.timeout = timeout ?? DefaultTimeout;
            if (this.timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        public async Task<HostPublicNetwork> ResolveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options.Ipv4 != null || options.Ipv6 != null)
                return new HostPublicNetwork(AvailabilityState.Available, options.Ipv4, options.Ipv6);
            if (!options.AutoDetectEnabled || string.IsNullOrEmpty(options.DetectionEndpoint))
                return new HostPublicNetwork(AvailabilityState.Unavailable, null, null);
            if (!Uri.TryCreate(options.DetectionEndpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
                return new HostPublicNetwork(AvailabilityState.Unavailable, null, null);

            Task<HostPublicNetwork> request;
            TaskCompletionSource<HostPublicNetwork>? completion = null;
            lock (sync)
            {
                if (cached != null && utcNow() - cachedAtUtc < CacheLifetime) return cached;
                if (inFlight != null)
                {
                    request = inFlight;
                }
                else
                {
                    completion = new TaskCompletionSource<HostPublicNetwork>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    inFlight = completion.Task;
                    request = inFlight;
                }
            }

            if (completion != null) _ = CompleteAutoDetectionAsync(endpoint, completion);
            return await AwaitWithCallerCancellationAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private async Task CompleteAutoDetectionAsync(
            Uri endpoint,
            TaskCompletionSource<HostPublicNetwork> completion)
        {
            try
            {
                using (var timeoutSource = new CancellationTokenSource())
                {
                    timeoutSource.CancelAfter(timeout);
                    using (var response = await client.GetAsync(endpoint, timeoutSource.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        var address = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
                        if (!IPAddress.TryParse(address, out var parsed))
                        {
                            CompleteRequest(completion, new HostPublicNetwork(AvailabilityState.Unavailable, null, null));
                            return;
                        }
                        var resolved = parsed.AddressFamily == AddressFamily.InterNetwork
                            ? new HostPublicNetwork(AvailabilityState.Available, parsed.ToString(), null)
                            : parsed.AddressFamily == AddressFamily.InterNetworkV6
                                ? new HostPublicNetwork(AvailabilityState.Available, null, parsed.ToString())
                                : new HostPublicNetwork(AvailabilityState.Unavailable, null, null);
                        CompleteRequest(completion, resolved);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                CompleteRequest(completion, new HostPublicNetwork(AvailabilityState.Unavailable, null, null));
            }
            catch (Exception)
            {
                CompleteRequest(completion, new HostPublicNetwork(AvailabilityState.Unavailable, null, null));
            }
        }

        private void CompleteRequest(
            TaskCompletionSource<HostPublicNetwork> completion,
            HostPublicNetwork result)
        {
            lock (sync)
            {
                if (result.Availability == AvailabilityState.Available)
                {
                    cached = result;
                    cachedAtUtc = utcNow();
                }
                if (ReferenceEquals(inFlight, completion.Task)) inFlight = null;
            }
            completion.TrySetResult(result);
        }

        private static async Task<HostPublicNetwork> AwaitWithCallerCancellationAsync(
            Task<HostPublicNetwork> request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cancellation = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancellation.TrySetResult(true)))
            {
                var completed = await Task.WhenAny(request, cancellation.Task).ConfigureAwait(false);
                if (!ReferenceEquals(completed, request)) cancellationToken.ThrowIfCancellationRequested();
            }
            return await request.ConfigureAwait(false);
        }
    }
}
