using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class ServerEventSseSession : IDisposable
    {
        private const int MailboxCapacity = 256;
        private const int ReplayLimit = 5000;
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly IServerEventStream serverEvents;
        private readonly IPanelRuntimeStatus runtimeStatus;
        private IServerEventSubscription? subscription;
        private WelcomeEventData? welcome;
        private int reservationAttempted;
        private int writeStarted;
        private int disposed;

        public ServerEventSseSession(
            IServerEventStream serverEvents,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.serverEvents = serverEvents ?? throw new ArgumentNullException(nameof(serverEvents));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        }

        public bool TryReserve()
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new ObjectDisposedException(nameof(ServerEventSseSession));
            if (Interlocked.Exchange(ref reservationAttempted, 1) != 0)
                throw new InvalidOperationException("An SSE reservation can only be attempted once.");

            if (!serverEvents.TrySubscribe(MailboxCapacity, out var candidate) || candidate == null)
                return false;

            subscription = candidate;
            welcome = new WelcomeEventData(
                ProductInfo.Name,
                ProductInfo.Version,
                runtimeStatus.State.ToString().ToLowerInvariant(),
                runtimeStatus.GameReadiness.ToString().ToLowerInvariant(),
                DateTime.UtcNow);

            if (Volatile.Read(ref disposed) == 0) return true;
            Interlocked.Exchange(ref subscription, null)?.Dispose();
            throw new ObjectDisposedException(nameof(ServerEventSseSession));
        }

        public async Task WriteAsync(
            Stream output,
            long? afterSequence,
            CancellationToken cancellationToken)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (Interlocked.Exchange(ref writeStarted, 1) != 0)
                throw new InvalidOperationException("An SSE session can only write one response.");

            var activeSubscription = subscription;
            var welcomeSnapshot = welcome;
            if (activeSubscription == null || welcomeSnapshot == null)
                throw new InvalidOperationException("The SSE subscription must be reserved before writing.");

            try
            {
                await WriteNamedEventAsync(
                    output,
                    "welcome",
                    welcomeSnapshot,
                    cancellationToken).ConfigureAwait(false);

                var replay = serverEvents.ReadAfter(
                    afterSequence,
                    ReplayLimit,
                    out var hasGap);
                var requestedSequence = afterSequence ?? 0L;
                var lastSentSequence = hasGap
                    ? replay.Count > 0
                        ? replay[0].Sequence - 1L
                        : 0L
                    : requestedSequence;
                if (hasGap)
                {
                    await WriteGapAsync(
                        output,
                        requestedSequence,
                        cancellationToken).ConfigureAwait(false);
                }

                foreach (var serverEvent in replay)
                {
                    if (serverEvent.Sequence <= lastSentSequence) continue;
                    await WriteServerEventAsync(
                        output,
                        serverEvent,
                        cancellationToken).ConfigureAwait(false);
                    lastSentSequence = serverEvent.Sequence;
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    ServerEvent? serverEvent;
                    using (var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken))
                    {
                        heartbeat.CancelAfter(HeartbeatInterval);
                        try
                        {
                            serverEvent = await activeSubscription
                                .ReadAsync(heartbeat.Token)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                            when (!cancellationToken.IsCancellationRequested)
                        {
                            await WriteTextAsync(
                                output,
                                ": keep-alive\n\n",
                                cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                    }

                    if (serverEvent == null)
                    {
                        if (activeSubscription.IsOverflowed)
                        {
                            await WriteGapAsync(
                                output,
                                lastSentSequence,
                                cancellationToken).ConfigureAwait(false);
                        }
                        return;
                    }

                    if (serverEvent.Sequence <= lastSentSequence) continue;
                    await WriteServerEventAsync(
                        output,
                        serverEvent,
                        cancellationToken).ConfigureAwait(false);
                    lastSentSequence = serverEvent.Sequence;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                Dispose();
                try { output.Close(); } catch { }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Interlocked.Exchange(ref subscription, null)?.Dispose();
        }

        private static Task WriteServerEventAsync(
            Stream output,
            ServerEvent serverEvent,
            CancellationToken cancellationToken) =>
            WriteTextAsync(
                output,
                "id: " + serverEvent.Sequence.ToString(CultureInfo.InvariantCulture) + "\n" +
                "event: " + serverEvent.EventName + "\n" +
                "data: " + JsonConvert.SerializeObject(serverEvent.Data, JsonSettings) + "\n\n",
                cancellationToken);

        private static Task WriteGapAsync(
            Stream output,
            long afterSequence,
            CancellationToken cancellationToken) =>
            WriteNamedEventAsync(
                output,
                "gap",
                new { afterSequence },
                cancellationToken);

        private static Task WriteNamedEventAsync(
            Stream output,
            string eventName,
            object payload,
            CancellationToken cancellationToken) =>
            WriteTextAsync(
                output,
                "event: " + eventName + "\n" +
                "data: " + JsonConvert.SerializeObject(payload, JsonSettings) + "\n\n",
                cancellationToken);

        private static async Task WriteTextAsync(
            Stream output,
            string value,
            CancellationToken cancellationToken)
        {
            var bytes = Utf8.GetBytes(value);
            await output.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private sealed class WelcomeEventData
        {
            public WelcomeEventData(
                string product,
                string version,
                string hostState,
                string gameReadiness,
                DateTime connectedAtUtc)
            {
                Product = product;
                Version = version;
                HostState = hostState;
                GameReadiness = gameReadiness;
                ConnectedAtUtc = connectedAtUtc;
            }

            public string Product { get; }
            public string Version { get; }
            public string HostState { get; }
            public string GameReadiness { get; }
            public DateTime ConnectedAtUtc { get; }
        }
    }
}
