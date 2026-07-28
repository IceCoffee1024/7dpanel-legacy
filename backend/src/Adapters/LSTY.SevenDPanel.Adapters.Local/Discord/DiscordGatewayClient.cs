using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Discord;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Local.Discord
{
    public enum DiscordGatewayDirective
    {
        None,
        Reconnect
    }

    public enum DiscordGatewayReconnectReason
    {
        None,
        ConnectionClosed,
        GatewayReconnect,
        InvalidSessionResumable,
        InvalidSessionNonResumable,
        HeartbeatNotAcknowledged,
        ConnectionFailure
    }

    public sealed class DiscordGatewayDiagnostic
    {
        public DiscordGatewayDiagnostic(
            string code,
            string failureType,
            int attempt,
            TimeSpan reconnectDelay)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            FailureType = failureType ?? throw new ArgumentNullException(nameof(failureType));
            Attempt = attempt;
            ReconnectDelay = reconnectDelay;
        }

        public string Code { get; }
        public string FailureType { get; }
        public int Attempt { get; }
        public TimeSpan ReconnectDelay { get; }

        public override string ToString() =>
            $"DiscordGatewayDiagnostic {{ Code = {Code}, FailureType = {FailureType}, Attempt = {Attempt}, ReconnectDelay = {ReconnectDelay} }}";
    }

    public sealed class DiscordGatewayProcessResult
    {
        public DiscordGatewayProcessResult(
            DiscordGatewayDirective directive,
            string? outboundPayload,
            TimeSpan? heartbeatInterval,
            DiscordGatewayReconnectReason reconnectReason = DiscordGatewayReconnectReason.None,
            bool sessionEstablished = false)
        {
            Directive = directive;
            OutboundPayload = outboundPayload;
            HeartbeatInterval = heartbeatInterval;
            ReconnectReason = reconnectReason;
            SessionEstablished = sessionEstablished;
        }

        public DiscordGatewayDirective Directive { get; }
        public string? OutboundPayload { get; }
        public TimeSpan? HeartbeatInterval { get; }
        public DiscordGatewayReconnectReason ReconnectReason { get; }
        public bool SessionEstablished { get; }
    }

    public sealed class DiscordGatewayOptions
    {
        private static readonly Uri DefaultGatewayUri =
            new Uri("wss://gateway.discord.gg/");

        public DiscordGatewayOptions(
            string token,
            string guildId,
            IEnumerable<string> channelIds,
            Uri? gatewayUri = null,
            TimeSpan? reconnectDelay = null)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("discord_gateway_token_required", nameof(token));
            if (string.IsNullOrWhiteSpace(guildId))
                throw new ArgumentException("discord_gateway_guild_required", nameof(guildId));
            if (channelIds == null) throw new ArgumentNullException(nameof(channelIds));

            var normalizedChannels = new HashSet<string>(
                channelIds
                    .Where(channelId => !string.IsNullOrWhiteSpace(channelId))
                    .Select(channelId => channelId.Trim()),
                StringComparer.Ordinal);
            if (normalizedChannels.Count == 0)
                throw new ArgumentException("discord_gateway_channels_required", nameof(channelIds));

            var endpoint = gatewayUri ?? DefaultGatewayUri;
            if (!endpoint.IsAbsoluteUri ||
                !string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(endpoint.UserInfo))
                throw new ArgumentException("discord_gateway_uri_invalid", nameof(gatewayUri));

            var retry = reconnectDelay ?? TimeSpan.FromSeconds(1);
            if (retry < TimeSpan.Zero || retry > TimeSpan.FromMinutes(5))
                throw new ArgumentOutOfRangeException(nameof(reconnectDelay));

            Token = token.Trim();
            GuildId = guildId.Trim();
            ChannelIds = normalizedChannels;
            GatewayUri = AddGatewayQuery(endpoint);
            ReconnectDelay = retry;
        }

        internal string Token { get; }
        internal HashSet<string> ChannelIds { get; }
        public string GuildId { get; }
        public Uri GatewayUri { get; }
        public TimeSpan ReconnectDelay { get; }

        internal static Uri AddGatewayQuery(Uri endpoint)
        {
            var builder = new UriBuilder(endpoint)
            {
                Query = "v=10&encoding=json"
            };
            return builder.Uri;
        }

        public override string ToString() =>
            $"DiscordGatewayOptions {{ Token = [REDACTED], GuildId = {GuildId}, ChannelCount = {ChannelIds.Count}, GatewayHost = {GatewayUri.Host}, ReconnectDelay = {ReconnectDelay} }}";
    }

    public sealed class DiscordGatewayV10Session
    {
        public const int RequiredIntents = (1 << 9) | (1 << 15);

        private readonly DiscordGatewayOptions options;
        private readonly IDiscordInboundTransportSink sink;
        private bool heartbeatOutstanding;
        private string? resumeGatewayUrl;

        public DiscordGatewayV10Session(
            DiscordGatewayOptions options,
            IDiscordInboundTransportSink sink)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public long? LastSequence { get; private set; }
        public string? SessionId { get; private set; }
        public Uri ConnectionUri => CanResume &&
            Uri.TryCreate(resumeGatewayUrl, UriKind.Absolute, out var resumeUri)
                ? DiscordGatewayOptions.AddGatewayQuery(resumeUri)
                : options.GatewayUri;

        private bool CanResume =>
            LastSequence.HasValue &&
            !string.IsNullOrWhiteSpace(SessionId) &&
            !string.IsNullOrWhiteSpace(resumeGatewayUrl);

        public async Task<DiscordGatewayProcessResult> ProcessAsync(
            string payload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = ParseObject(payload);
            var opcode = RequiredInt32(envelope, "op");
            switch (opcode)
            {
                case 0:
                    UpdateSequence(envelope);
                    var sessionEstablished = await HandleDispatchAsync(envelope, cancellationToken)
                        .ConfigureAwait(false);
                    return new DiscordGatewayProcessResult(
                        DiscordGatewayDirective.None,
                        null,
                        null,
                        DiscordGatewayReconnectReason.None,
                        sessionEstablished);
                case 1:
                    return new DiscordGatewayProcessResult(
                        DiscordGatewayDirective.None,
                        CreateHeartbeatPayload(failIfOutstanding: false),
                        null);
                case 7:
                    return Reconnect(DiscordGatewayReconnectReason.GatewayReconnect);
                case 9:
                    var resumable = RequiredBoolean(envelope, "d");
                    if (!resumable) ClearSession();
                    return Reconnect(
                        resumable
                            ? DiscordGatewayReconnectReason.InvalidSessionResumable
                            : DiscordGatewayReconnectReason.InvalidSessionNonResumable);
                case 10:
                    heartbeatOutstanding = false;
                    var hello = RequiredObject(envelope, "d");
                    var milliseconds = RequiredInt32(hello, "heartbeat_interval");
                    if (milliseconds <= 0)
                        throw new FormatException("discord_gateway_heartbeat_interval_invalid");
                    return new DiscordGatewayProcessResult(
                        DiscordGatewayDirective.None,
                        CreateHandshakePayload(),
                        TimeSpan.FromMilliseconds(milliseconds));
                case 11:
                    heartbeatOutstanding = false;
                    return None();
                default:
                    return None();
            }
        }

        public string? TryCreateHeartbeatPayload() =>
            CreateHeartbeatPayload(failIfOutstanding: true);

        private string CreateHandshakePayload()
        {
            if (CanResume)
            {
                return JsonConvert.SerializeObject(new
                {
                    op = 6,
                    d = new
                    {
                        token = options.Token,
                        session_id = SessionId,
                        seq = LastSequence!.Value
                    }
                });
            }

            return JsonConvert.SerializeObject(new
            {
                op = 2,
                d = new
                {
                    token = options.Token,
                    intents = RequiredIntents,
                    properties = new Dictionary<string, string>
                    {
                        ["$os"] = Environment.OSVersion.Platform.ToString().ToLowerInvariant(),
                        ["$browser"] = "7dpanel",
                        ["$device"] = "7dpanel"
                    }
                }
            });
        }

        private string? CreateHeartbeatPayload(bool failIfOutstanding)
        {
            if (failIfOutstanding && heartbeatOutstanding) return null;
            heartbeatOutstanding = true;
            return JsonConvert.SerializeObject(new
            {
                op = 1,
                d = LastSequence.HasValue ? (object)LastSequence.Value : null
            });
        }

        private async Task<bool> HandleDispatchAsync(
            JObject envelope,
            CancellationToken cancellationToken)
        {
            var eventName = OptionalString(envelope, "t");
            var data = OptionalObject(envelope, "d");
            if (data == null || string.IsNullOrEmpty(eventName)) return false;

            if (string.Equals(eventName, "READY", StringComparison.Ordinal))
            {
                var sessionId = OptionalString(data, "session_id");
                var resumeUrl = OptionalString(data, "resume_gateway_url");
                if (!string.IsNullOrWhiteSpace(sessionId) &&
                    Uri.TryCreate(resumeUrl, UriKind.Absolute, out var resumeUri) &&
                    string.Equals(resumeUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
                {
                    SessionId = sessionId;
                    resumeGatewayUrl = resumeUri.AbsoluteUri;
                    return true;
                }
                return false;
            }

            if (string.Equals(eventName, "RESUMED", StringComparison.Ordinal)) return true;

            if (!string.Equals(eventName, "MESSAGE_CREATE", StringComparison.Ordinal)) return false;
            var message = TryMapMessage(data);
            if (message == null) return false;
            await sink.HandleMessageAsync(message, cancellationToken).ConfigureAwait(false);
            return false;
        }

        private DiscordMessageCreateEnvelope? TryMapMessage(JObject data)
        {
            var messageId = OptionalString(data, "id");
            var guildId = OptionalString(data, "guild_id");
            var channelId = OptionalString(data, "channel_id");
            var content = OptionalString(data, "content");
            var author = OptionalObject(data, "author");
            var authorId = author == null ? null : OptionalString(author, "id");
            var authorIsBot = author != null && OptionalBoolean(author, "bot");
            var isWebhook = data["webhook_id"]?.Type != JTokenType.Null;

            if (!string.Equals(guildId, options.GuildId, StringComparison.Ordinal) ||
                channelId == null || !options.ChannelIds.Contains(channelId) ||
                authorIsBot || isWebhook ||
                string.IsNullOrWhiteSpace(messageId) ||
                string.IsNullOrWhiteSpace(authorId) ||
                content == null || content.Length < 1 || content.Length > 2000)
                return null;

            try
            {
                return new DiscordMessageCreateEnvelope(
                    messageId!,
                    guildId!,
                    channelId,
                    authorId!,
                    false,
                    false,
                    content);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private void UpdateSequence(JObject envelope)
        {
            var raw = envelope["s"];
            if (raw == null) return;
            try
            {
                var sequence = raw.Value<long>();
                if (sequence >= 0) LastSequence = sequence;
            }
            catch (FormatException)
            {
            }
            catch (InvalidCastException)
            {
            }
            catch (OverflowException)
            {
            }
        }

        private void ClearSession()
        {
            LastSequence = null;
            SessionId = null;
            resumeGatewayUrl = null;
            heartbeatOutstanding = false;
        }

        private JObject ParseObject(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload) || payload.Length > 64 * 1024)
                throw new FormatException("discord_gateway_payload_invalid");
            try
            {
                return JObject.Parse(payload);
            }
            catch (JsonException)
            {
                throw new FormatException("discord_gateway_payload_invalid");
            }
            catch (ArgumentException)
            {
                throw new FormatException("discord_gateway_payload_invalid");
            }
        }

        private static int RequiredInt32(JObject value, string key)
        {
            var raw = value[key];
            if (raw == null)
                throw new FormatException("discord_gateway_payload_invalid");
            try
            {
                return raw.Value<int>();
            }
            catch (Exception exception) when (
                exception is FormatException ||
                exception is InvalidCastException ||
                exception is OverflowException)
            {
                throw new FormatException("discord_gateway_payload_invalid");
            }
        }

        private static JObject RequiredObject(
            JObject value,
            string key) =>
            OptionalObject(value, key) ??
            throw new FormatException("discord_gateway_payload_invalid");

        private static JObject? OptionalObject(
            JObject value,
            string key) =>
            value[key] as JObject;

        private static string? OptionalString(
            JObject value,
            string key) =>
            value[key]?.Type == JTokenType.String ? value.Value<string>(key) : null;

        private static bool OptionalBoolean(
            JObject value,
            string key) =>
            value[key]?.Type == JTokenType.Boolean && value.Value<bool>(key);

        private static bool RequiredBoolean(
            JObject value,
            string key)
        {
            var raw = value[key];
            if (raw?.Type != JTokenType.Boolean)
                throw new FormatException("discord_gateway_payload_invalid");
            return raw.Value<bool>();
        }

        private static DiscordGatewayProcessResult None() =>
            new DiscordGatewayProcessResult(DiscordGatewayDirective.None, null, null);

        private static DiscordGatewayProcessResult Reconnect(
            DiscordGatewayReconnectReason reconnectReason) =>
            new DiscordGatewayProcessResult(
                DiscordGatewayDirective.Reconnect,
                null,
                null,
                reconnectReason);
    }

    public interface IDiscordGatewaySocket : IDisposable
    {
        Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);
        Task<string?> ReceiveTextAsync(CancellationToken cancellationToken);
        Task SendTextAsync(string payload, CancellationToken cancellationToken);
    }

    public interface IDiscordGatewaySocketFactory
    {
        IDiscordGatewaySocket Create();
    }

    public interface IDiscordGatewayDelay
    {
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public sealed class ClientWebSocketDiscordGatewaySocketFactory :
        IDiscordGatewaySocketFactory
    {
        private readonly IWebProxy? proxy;

        public ClientWebSocketDiscordGatewaySocketFactory(IWebProxy? proxy = null) =>
            this.proxy = proxy;

        public IDiscordGatewaySocket Create() =>
            new ClientWebSocketDiscordGatewaySocket(proxy);
    }

    public sealed class SystemDiscordGatewayDelay : IDiscordGatewayDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(delay, cancellationToken);
    }

    internal sealed class ClientWebSocketDiscordGatewaySocket : IDiscordGatewaySocket
    {
        private const int MaximumPayloadBytes = 64 * 1024;
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly SemaphoreSlim sendGate = new SemaphoreSlim(1, 1);
        private bool disposed;

        public ClientWebSocketDiscordGatewaySocket(IWebProxy? proxy)
        {
            if (proxy != null) socket.Options.Proxy = proxy;
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return socket.ConnectAsync(endpoint, cancellationToken);
        }

        public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var buffer = new byte[8 * 1024];
            using var payload = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new InvalidOperationException("discord_gateway_binary_payload_rejected");
                if (payload.Length + result.Count > MaximumPayloadBytes)
                    throw new InvalidOperationException("discord_gateway_payload_too_large");
                payload.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                return Encoding.UTF8.GetString(payload.ToArray());
            }
        }

        public async Task SendTextAsync(
            string payload,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var bytes = Encoding.UTF8.GetBytes(payload ?? throw new ArgumentNullException(nameof(payload)));
            await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                sendGate.Release();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            socket.Dispose();
            sendGate.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ClientWebSocketDiscordGatewaySocket));
        }
    }

    public sealed class DiscordGatewayClient : IDisposable
    {
        private static readonly object reconnectJitterSync = new object();
        private static readonly Random reconnectJitter = new Random();
        private static readonly TimeSpan MinimumReconnectDelay =
            TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan MaximumReconnectDelay =
            TimeSpan.FromMinutes(1);
        private readonly DiscordGatewayOptions options;
        private readonly DiscordGatewayV10Session session;
        private readonly IDiscordGatewaySocketFactory socketFactory;
        private readonly IDiscordGatewayDelay delay;
        private readonly IDiscordGatewayHealthSink? healthSink;
        private readonly object sync = new object();
        private CancellationTokenSource? runCancellation;
        private Task runTask = Task.CompletedTask;
        private IDiscordGatewaySocket? currentSocket;
        private bool disposed;

        public DiscordGatewayClient(
            DiscordGatewayOptions options,
            IDiscordInboundTransportSink sink)
            : this(
                options,
                sink,
                new ClientWebSocketDiscordGatewaySocketFactory(),
                new SystemDiscordGatewayDelay())
        {
        }

        public DiscordGatewayClient(
            DiscordGatewayOptions options,
            IDiscordInboundTransportSink sink,
            IDiscordGatewaySocketFactory socketFactory,
            IDiscordGatewayDelay delay)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            session = new DiscordGatewayV10Session(
                options,
                sink ?? throw new ArgumentNullException(nameof(sink)));
            healthSink = sink as IDiscordGatewayHealthSink;
            this.socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
            this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
        }

        public event Action<DiscordGatewayDiagnostic>? Diagnostic;

        public bool IsRunning
        {
            get
            {
                lock (sync) return runCancellation != null && !runTask.IsCompleted;
            }
        }

        public bool Start()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (runCancellation != null && !runTask.IsCompleted) return false;
                runCancellation?.Dispose();
                runCancellation = new CancellationTokenSource();
                runTask = RunAsync(runCancellation.Token);
                return true;
            }
        }

        public async Task<bool> StopAsync(
            TimeSpan drainTimeout,
            CancellationToken cancellationToken)
        {
            if (drainTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(drainTimeout));

            Task pending;
            lock (sync)
            {
                runCancellation?.Cancel();
                pending = runTask;
            }

            if (pending.IsCompleted)
            {
                await ObserveCompletionAsync(pending).ConfigureAwait(false);
                ObserveHealth(
                    DiscordHealthState.Unavailable,
                    "discord_gateway_not_running");
                return true;
            }

            var timeout = Task.Delay(drainTimeout, cancellationToken);
            var completed = await Task.WhenAny(pending, timeout).ConfigureAwait(false);
            if (completed == pending)
            {
                await ObserveCompletionAsync(pending).ConfigureAwait(false);
                ObserveHealth(
                    DiscordHealthState.Unavailable,
                    "discord_gateway_not_running");
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            DisposeCurrentSocket();
            ObserveHealth(
                DiscordHealthState.Unavailable,
                "discord_gateway_stop_timeout");
            return false;
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                runCancellation?.Cancel();
            }
            DisposeCurrentSocket();
            ObserveHealth(
                DiscordHealthState.Unavailable,
                "discord_gateway_not_running");
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var reconnectAttempt = 0;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ObserveHealth(DiscordHealthState.Connecting, null);
                    DiscordGatewayConnectionResult connectionResult;
                    Exception? connectionFailure = null;
                    try
                    {
                        using var socket = socketFactory.Create();
                        SetCurrentSocket(socket);
                        connectionResult = await RunConnectionAsync(socket, cancellationToken)
                            .ConfigureAwait(false);
                        if (connectionResult.SessionEstablished) reconnectAttempt = 0;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        connectionFailure = exception;
                        connectionResult = new DiscordGatewayConnectionResult(
                            DiscordGatewayReconnectReason.ConnectionFailure,
                            false);
                    }
                    finally
                    {
                        SetCurrentSocket(null);
                    }

                    if (cancellationToken.IsCancellationRequested) break;
                    var reconnectDelay = CreateReconnectDelay(
                        ++reconnectAttempt,
                        connectionResult.ReconnectReason);
                    if (connectionFailure != null)
                    {
                        ObserveHealth(
                            DiscordHealthState.Unavailable,
                            "discord_gateway_connection_failure");
                        PublishDiagnostic(new DiscordGatewayDiagnostic(
                            "connection_failure",
                            connectionFailure.GetType().Name,
                            reconnectAttempt,
                            reconnectDelay));
                    }
                    else
                    {
                        ObserveHealth(
                            DiscordHealthState.Degraded,
                            "discord_gateway_reconnecting");
                    }
                    await delay.DelayAsync(reconnectDelay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task<DiscordGatewayConnectionResult> RunConnectionAsync(
            IDiscordGatewaySocket socket,
            CancellationToken cancellationToken)
        {
            await socket.ConnectAsync(session.ConnectionUri, cancellationToken).ConfigureAwait(false);
            var helloPayload = await socket.ReceiveTextAsync(cancellationToken).ConfigureAwait(false);
            if (helloPayload == null)
                throw new InvalidOperationException("discord_gateway_hello_missing");
            var hello = await session.ProcessAsync(helloPayload, cancellationToken).ConfigureAwait(false);
            if (!hello.HeartbeatInterval.HasValue || string.IsNullOrEmpty(hello.OutboundPayload))
                throw new InvalidOperationException("discord_gateway_hello_invalid");
            await socket.SendTextAsync(hello.OutboundPayload!, cancellationToken).ConfigureAwait(false);

            using var connectionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var receive = ReceiveLoopAsync(socket, connectionCancellation.Token);
            var heartbeat = HeartbeatLoopAsync(
                socket,
                hello.HeartbeatInterval.Value,
                connectionCancellation.Token);
            var completed = await Task.WhenAny(receive, heartbeat).ConfigureAwait(false);
            connectionCancellation.Cancel();
            if (cancellationToken.IsCancellationRequested)
            {
                await ObserveConnectionEndAsync(receive).ConfigureAwait(false);
                await ObserveConnectionEndAsync(heartbeat).ConfigureAwait(false);
                return new DiscordGatewayConnectionResult(
                    DiscordGatewayReconnectReason.ConnectionClosed,
                    false);
            }
            if (completed == receive)
            {
                var result = await receive.ConfigureAwait(false);
                await ObserveConnectionEndAsync(heartbeat).ConfigureAwait(false);
                return result;
            }

            var heartbeatResult = await heartbeat.ConfigureAwait(false);
            await ObserveConnectionEndAsync(receive).ConfigureAwait(false);
            return new DiscordGatewayConnectionResult(heartbeatResult, false);
        }

        private async Task<DiscordGatewayConnectionResult> ReceiveLoopAsync(
            IDiscordGatewaySocket socket,
            CancellationToken cancellationToken)
        {
            var sessionEstablished = false;
            while (true)
            {
                var payload = await socket.ReceiveTextAsync(cancellationToken).ConfigureAwait(false);
                if (payload == null)
                {
                    return new DiscordGatewayConnectionResult(
                        DiscordGatewayReconnectReason.ConnectionClosed,
                        sessionEstablished);
                }
                var result = await session.ProcessAsync(payload, cancellationToken).ConfigureAwait(false);
                sessionEstablished |= result.SessionEstablished;
                if (result.SessionEstablished)
                    ObserveHealth(DiscordHealthState.Connected, null);
                if (!string.IsNullOrEmpty(result.OutboundPayload))
                {
                    await socket.SendTextAsync(result.OutboundPayload!, cancellationToken)
                        .ConfigureAwait(false);
                }
                if (result.Directive == DiscordGatewayDirective.Reconnect)
                {
                    return new DiscordGatewayConnectionResult(
                        result.ReconnectReason,
                        sessionEstablished);
                }
            }
        }

        private async Task<DiscordGatewayReconnectReason> HeartbeatLoopAsync(
            IDiscordGatewaySocket socket,
            TimeSpan heartbeatInterval,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                await delay.DelayAsync(heartbeatInterval, cancellationToken).ConfigureAwait(false);
                var heartbeat = session.TryCreateHeartbeatPayload();
                if (heartbeat == null)
                    return DiscordGatewayReconnectReason.HeartbeatNotAcknowledged;
                await socket.SendTextAsync(heartbeat, cancellationToken).ConfigureAwait(false);
            }
        }

        private TimeSpan CreateReconnectDelay(
            int attempt,
            DiscordGatewayReconnectReason reconnectReason)
        {
            if (reconnectReason == DiscordGatewayReconnectReason.HeartbeatNotAcknowledged)
                return TimeSpan.Zero;

            var baseMilliseconds = Math.Max(
                options.ReconnectDelay.TotalMilliseconds,
                MinimumReconnectDelay.TotalMilliseconds);
            var maximumMilliseconds = Math.Max(
                options.ReconnectDelay.TotalMilliseconds,
                MaximumReconnectDelay.TotalMilliseconds);
            var exponent = Math.Min(Math.Max(attempt - 1, 0), 8);
            var cappedMilliseconds = Math.Min(
                baseMilliseconds * Math.Pow(2, exponent),
                maximumMilliseconds);
            var minimumMilliseconds = Math.Max(
                MinimumReconnectDelay.TotalMilliseconds / 2,
                Math.Ceiling(cappedMilliseconds / 2));
            var roundedMaximumMilliseconds = Math.Ceiling(cappedMilliseconds);
            if (roundedMaximumMilliseconds <= minimumMilliseconds)
                return TimeSpan.FromMilliseconds(roundedMaximumMilliseconds);

            var span = (int)(roundedMaximumMilliseconds - minimumMilliseconds);
            double jitteredMilliseconds;
            lock (reconnectJitterSync)
            {
                jitteredMilliseconds = minimumMilliseconds + reconnectJitter.Next(1, span + 1);
            }
            return TimeSpan.FromMilliseconds(jitteredMilliseconds);
        }

        private void PublishDiagnostic(DiscordGatewayDiagnostic diagnostic)
        {
            var handlers = Diagnostic;
            if (handlers == null) return;
            foreach (Action<DiscordGatewayDiagnostic> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(diagnostic);
                }
                catch
                {
                }
            }
        }

        private void ObserveHealth(DiscordHealthState state, string? errorCode) =>
            healthSink?.ObserveGatewayHealth(state, errorCode, DateTimeOffset.UtcNow);

        private void SetCurrentSocket(IDiscordGatewaySocket? socket)
        {
            lock (sync) currentSocket = socket;
        }

        private void DisposeCurrentSocket()
        {
            IDiscordGatewaySocket? socket;
            lock (sync) socket = currentSocket;
            try
            {
                socket?.Dispose();
            }
            catch
            {
            }
        }

        private static async Task ObserveConnectionEndAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async Task ObserveCompletionAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(DiscordGatewayClient));
        }

        private sealed class DiscordGatewayConnectionResult
        {
            public DiscordGatewayConnectionResult(
                DiscordGatewayReconnectReason reconnectReason,
                bool sessionEstablished)
            {
                ReconnectReason = reconnectReason;
                SessionEstablished = sessionEstablished;
            }

            public DiscordGatewayReconnectReason ReconnectReason { get; }
            public bool SessionEstablished { get; }
        }
    }
}
