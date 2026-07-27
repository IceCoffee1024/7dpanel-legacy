using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Discord;
using LSTY.SevenDPanel.Application.Discord;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class DiscordTransportTests
    {
        private const string PrivateKeyHex =
            "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60";
        private const string PublicKeyHex =
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";
        private const string Rfc8032EmptySignatureHex =
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555" +
            "fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b";
        private const string Timestamp = "1785127200";
        private static readonly DateTimeOffset SignatureNow =
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(Timestamp));

        [Fact]
        public void Managed_ed25519_verifier_accepts_the_RFC8032_known_vector()
        {
            var verifier = new Ed25519SignatureVerifier(PublicKeyHex);

            Assert.True(verifier.Verify(Rfc8032EmptySignatureHex, Array.Empty<byte>()));
        }

        [Fact]
        public void Discord_verifier_signs_timestamp_ascii_plus_the_exact_raw_body()
        {
            var body = Encoding.UTF8.GetBytes("{\"type\":1}");
            var signature = Sign(Timestamp, body);
            var verifier = InteractionVerifier();

            Assert.True(verifier.Verify(signature, Timestamp, body));
            Assert.False(verifier.Verify(signature, Timestamp, Encoding.UTF8.GetBytes("{\"type\": 1}")));
            Assert.False(verifier.Verify(signature, "1785127201", body));
        }

        [Fact]
        public void Discord_verifier_fails_closed_for_missing_malformed_or_stale_headers()
        {
            var body = Encoding.UTF8.GetBytes("{\"type\":1}");
            var signature = Sign(Timestamp, body);
            var verifier = InteractionVerifier();

            Assert.False(verifier.Verify(null, Timestamp, body));
            Assert.False(verifier.Verify(signature, null, body));
            Assert.False(verifier.Verify("zz", Timestamp, body));
            Assert.False(verifier.Verify(signature.Substring(2), Timestamp, body));
            Assert.False(verifier.Verify(signature + "00", Timestamp, body));
            Assert.False(verifier.Verify(new string('0', 128), Timestamp, body));
            Assert.False(verifier.Verify(signature, "not-a-timestamp", body));
            Assert.False(verifier.Verify(signature, "1785127200\u00ff", body));

            var stale = new DiscordInteractionSignatureVerifier(
                PublicKeyHex,
                () => SignatureNow.AddMinutes(6),
                TimeSpan.FromMinutes(5));
            Assert.False(stale.Verify(signature, Timestamp, body));
        }

        [Fact]
        public async Task Gateway_session_tracks_hello_heartbeat_sequence_ready_resume_and_invalid_session()
        {
            var sink = new RecordingInboundSink();
            var session = new DiscordGatewayV10Session(Options(), sink);

            var hello = await session.ProcessAsync(
                "{\"op\":10,\"d\":{\"heartbeat_interval\":45000}}",
                CancellationToken.None);

            Assert.Equal(TimeSpan.FromSeconds(45), hello.HeartbeatInterval);
            Assert.Equal(2, Opcode(hello.OutboundPayload));
            Assert.Equal(DiscordGatewayV10Session.RequiredIntents, (int)Payload(hello.OutboundPayload)["d"]!["intents"]!);

            var firstHeartbeat = session.TryCreateHeartbeatPayload();
            Assert.Equal(1, Opcode(firstHeartbeat));
            Assert.Equal(JTokenType.Null, Payload(firstHeartbeat)["d"]!.Type);
            Assert.Null(session.TryCreateHeartbeatPayload());

            await session.ProcessAsync("{\"op\":11}", CancellationToken.None);
            await session.ProcessAsync(
                "{\"op\":0,\"s\":42,\"t\":\"READY\",\"d\":{\"session_id\":\"session-1\",\"resume_gateway_url\":\"wss://resume.discord.test\"}}",
                CancellationToken.None);

            Assert.Equal(42, session.LastSequence);
            Assert.Equal("session-1", session.SessionId);
            Assert.Equal("wss://resume.discord.test/?v=10&encoding=json", session.ConnectionUri.AbsoluteUri);
            var sequencedHeartbeat = session.TryCreateHeartbeatPayload();
            Assert.Equal(42, (long)Payload(sequencedHeartbeat)["d"]!);
            await session.ProcessAsync("{\"op\":11}", CancellationToken.None);

            var reconnect = await session.ProcessAsync("{\"op\":7}", CancellationToken.None);
            Assert.Equal(DiscordGatewayDirective.Reconnect, reconnect.Directive);
            var resumeHello = await session.ProcessAsync(
                "{\"op\":10,\"d\":{\"heartbeat_interval\":45000}}",
                CancellationToken.None);
            Assert.Equal(6, Opcode(resumeHello.OutboundPayload));
            Assert.Equal("session-1", (string?)Payload(resumeHello.OutboundPayload)["d"]!["session_id"]);
            Assert.Equal(42, (long)Payload(resumeHello.OutboundPayload)["d"]!["seq"]!);

            var resumableInvalid = await session.ProcessAsync(
                "{\"op\":9,\"d\":true}",
                CancellationToken.None);
            Assert.Equal(DiscordGatewayDirective.Reconnect, resumableInvalid.Directive);
            Assert.Equal(6, Opcode((await session.ProcessAsync(
                "{\"op\":10,\"d\":{\"heartbeat_interval\":45000}}",
                CancellationToken.None)).OutboundPayload));

            var freshInvalid = await session.ProcessAsync(
                "{\"op\":9,\"d\":false}",
                CancellationToken.None);
            Assert.Equal(DiscordGatewayDirective.Reconnect, freshInvalid.Directive);
            Assert.Equal(2, Opcode((await session.ProcessAsync(
                "{\"op\":10,\"d\":{\"heartbeat_interval\":45000}}",
                CancellationToken.None)).OutboundPayload));
        }

        [Fact]
        public async Task Gateway_session_routes_only_allowed_non_bot_guild_messages_with_content()
        {
            var sink = new RecordingInboundSink();
            var session = new DiscordGatewayV10Session(Options(), sink);

            await session.ProcessAsync(Message(1, "message-1", "guild-1", "channel-1", "user-1", false, false, "hello"), CancellationToken.None);
            await session.ProcessAsync(Message(2, "wrong-guild", "guild-2", "channel-1", "user-1", false, false, "ignored"), CancellationToken.None);
            await session.ProcessAsync(Message(3, "wrong-channel", "guild-1", "channel-2", "user-1", false, false, "ignored"), CancellationToken.None);
            await session.ProcessAsync(Message(4, "bot", "guild-1", "channel-1", "bot-1", true, false, "ignored"), CancellationToken.None);
            await session.ProcessAsync(Message(5, "webhook", "guild-1", "channel-1", "hook-1", false, true, "ignored"), CancellationToken.None);
            await session.ProcessAsync(Message(6, "empty", "guild-1", "channel-1", "user-1", false, false, string.Empty), CancellationToken.None);

            var message = Assert.Single(sink.Messages);
            Assert.Equal("message-1", message.MessageId);
            Assert.Equal("hello", message.Content);
            Assert.Equal(6, session.LastSequence);
        }

        [Fact]
        public async Task Gateway_client_reconnects_when_a_heartbeat_is_not_acknowledged()
        {
            var first = new ScriptedGatewaySocket();
            var second = new ScriptedGatewaySocket();
            var sockets = new QueueGatewaySocketFactory(first, second);
            var delay = new ControlledGatewayDelay();
            using var client = new DiscordGatewayClient(
                Options(TimeSpan.Zero),
                new RecordingInboundSink(),
                sockets,
                delay);

            Assert.True(client.Start());
            Assert.False(client.Start());
            first.Push("{\"op\":10,\"d\":{\"heartbeat_interval\":1000}}");
            await EventuallyAsync(() => first.SentPayloads.Any(payload => Opcode(payload) == 2));

            await delay.ReleaseAsync(TimeSpan.FromSeconds(1));
            await EventuallyAsync(() => first.SentPayloads.Any(payload => Opcode(payload) == 1));
            await delay.ReleaseAsync(TimeSpan.FromSeconds(1));
            await EventuallyAsync(() => sockets.CreatedCount == 2);

            second.Push("{\"op\":10,\"d\":{\"heartbeat_interval\":1000}}");
            await EventuallyAsync(() => second.SentPayloads.Any(payload => Opcode(payload) == 2));
            Assert.True(await client.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
        }

        [Fact]
        public async Task Gateway_client_delays_then_resumes_or_reidentifies_after_invalid_session()
        {
            var first = new ScriptedGatewaySocket();
            var resumed = new ScriptedGatewaySocket();
            var reidentified = new ScriptedGatewaySocket();
            var sockets = new QueueGatewaySocketFactory(first, resumed, reidentified);
            var delay = new ControlledGatewayDelay();
            using var client = new DiscordGatewayClient(
                Options(TimeSpan.Zero),
                new RecordingInboundSink(),
                sockets,
                delay);

            Assert.True(client.Start());
            first.Push("{\"op\":10,\"d\":{\"heartbeat_interval\":60000}}");
            await EventuallyAsync(() => first.SentPayloads.Any(payload => Opcode(payload) == 2));
            first.Push("{\"op\":0,\"s\":42,\"t\":\"READY\",\"d\":{\"session_id\":\"session-1\",\"resume_gateway_url\":\"wss://resume.discord.test\"}}");
            first.Push("{\"op\":9,\"d\":true}");

            var resumableDelay = await delay.ReleaseNextMatchingAsync(
                value => value <= TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1));
            Assert.NotNull(resumableDelay);
            Assert.InRange(
                resumableDelay!.Value,
                TimeSpan.FromMilliseconds(125),
                TimeSpan.FromMilliseconds(250));
            await EventuallyAsync(() => sockets.CreatedCount == 2);
            resumed.Push("{\"op\":10,\"d\":{\"heartbeat_interval\":60000}}");
            await EventuallyAsync(() => resumed.SentPayloads.Any(payload => Opcode(payload) == 6));

            resumed.Push("{\"op\":9,\"d\":false}");
            var reidentifyDelay = await delay.ReleaseNextMatchingAsync(
                value => value <= TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(1));
            Assert.NotNull(reidentifyDelay);
            Assert.InRange(
                reidentifyDelay!.Value,
                TimeSpan.FromMilliseconds(125),
                TimeSpan.FromMilliseconds(500));
            await EventuallyAsync(() => sockets.CreatedCount == 3);
            reidentified.Push("{\"op\":10,\"d\":{\"heartbeat_interval\":60000}}");
            await EventuallyAsync(() => reidentified.SentPayloads.Any(payload => Opcode(payload) == 2));

            Assert.True(await client.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
        }

        [Fact]
        public async Task Gateway_client_uses_bounded_jittered_backoff_and_reports_secret_safe_connection_failures()
        {
            var sockets = new QueueGatewaySocketFactory(
                new FailingGatewaySocket(),
                new FailingGatewaySocket());
            var delay = new ControlledGatewayDelay();
            var diagnostics = new List<object>();
            using var client = new DiscordGatewayClient(
                Options(TimeSpan.FromSeconds(1)),
                new RecordingInboundSink(),
                sockets,
                delay);
            var diagnosticEvent = typeof(DiscordGatewayClient).GetEvent("Diagnostic");

            Assert.NotNull(diagnosticEvent);
            diagnosticEvent!.AddEventHandler(
                client,
                CreateDiagnosticCallback(diagnosticEvent.EventHandlerType!, diagnostics));

            Assert.True(client.Start());
            await EventuallyAsync(() => diagnostics.Count == 1);
            Assert.InRange(
                await delay.ReleaseNextAsync(),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(1));

            await EventuallyAsync(() => diagnostics.Count == 2);
            Assert.InRange(
                await delay.TakeNextAsync(),
                TimeSpan.FromMilliseconds(1001),
                TimeSpan.FromSeconds(2));

            Assert.All(diagnostics, diagnostic =>
            {
                Assert.Equal(
                    "connection_failure",
                    diagnostic.GetType().GetProperty("Code")!.GetValue(diagnostic));
                Assert.Equal(
                    "InvalidOperationException",
                    diagnostic.GetType().GetProperty("FailureType")!.GetValue(diagnostic));
                Assert.DoesNotContain("gateway-token", diagnostic.ToString());
            });
            Assert.True(await client.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
        }

        [Fact]
        public async Task Gateway_client_stop_is_bounded_while_an_accepted_message_drains()
        {
            var socket = new ScriptedGatewaySocket();
            var sink = new BlockingInboundSink();
            using var client = new DiscordGatewayClient(
                Options(TimeSpan.Zero),
                sink,
                new QueueGatewaySocketFactory(socket),
                new ControlledGatewayDelay());
            client.Start();
            socket.Push("{\"op\":10,\"d\":{\"heartbeat_interval\":60000}}");
            await EventuallyAsync(() => socket.SentPayloads.Any(payload => Opcode(payload) == 2));
            socket.Push(Message(1, "message-1", "guild-1", "channel-1", "user-1", false, false, "hello"));
            await sink.Entered.Task;

            Assert.False(await client.StopAsync(TimeSpan.FromMilliseconds(25), CancellationToken.None));
            sink.Release.TrySetResult(true);
            Assert.True(await client.StopAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
            Assert.Single(sink.Messages);
        }

        private static DiscordInteractionSignatureVerifier InteractionVerifier() =>
            new DiscordInteractionSignatureVerifier(
                PublicKeyHex,
                () => SignatureNow,
                TimeSpan.FromMinutes(5));

        private static DiscordGatewayOptions Options(TimeSpan? reconnectDelay = null) =>
            new DiscordGatewayOptions(
                "gateway-token",
                "guild-1",
                new[] { "channel-1" },
                new Uri("wss://gateway.discord.test"),
                reconnectDelay ?? TimeSpan.FromSeconds(1));

        private static string Message(
            long sequence,
            string messageId,
            string guildId,
            string channelId,
            string authorId,
            bool bot,
            bool webhook,
            string content) =>
            new JObject
            {
                ["op"] = 0,
                ["s"] = sequence,
                ["t"] = "MESSAGE_CREATE",
                ["d"] = new JObject
                {
                    ["id"] = messageId,
                    ["guild_id"] = guildId,
                    ["channel_id"] = channelId,
                    ["content"] = content,
                    ["author"] = new JObject
                    {
                        ["id"] = authorId,
                        ["bot"] = bot
                    },
                    ["webhook_id"] = webhook ? "webhook-1" : null
                }
            }.ToString(Newtonsoft.Json.Formatting.None);

        private static int Opcode(string? payload) => (int)Payload(payload)["op"]!;

        private static JObject Payload(string? payload)
        {
            Assert.False(string.IsNullOrWhiteSpace(payload));
            return JObject.Parse(payload!);
        }

        private static string Sign(string timestamp, byte[] body)
        {
            var timestampBytes = Encoding.ASCII.GetBytes(timestamp);
            var message = new byte[timestampBytes.Length + body.Length];
            Buffer.BlockCopy(timestampBytes, 0, message, 0, timestampBytes.Length);
            Buffer.BlockCopy(body, 0, message, timestampBytes.Length, body.Length);
            var signer = new Ed25519Signer();
            signer.Init(true, new Ed25519PrivateKeyParameters(Hex(PrivateKeyHex)));
            signer.BlockUpdate(message, 0, message.Length);
            return Hex(signer.GenerateSignature());
        }

        private static Delegate CreateDiagnosticCallback(
            Type handlerType,
            ICollection<object> diagnostics)
        {
            var diagnosticType = Assert.Single(handlerType.GetGenericArguments());
            var factory = typeof(DiscordTransportTests)
                .GetMethod(
                    nameof(CreateDiagnosticCallbackCore),
                    BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(diagnosticType);
            return (Delegate)factory.Invoke(null, new object[] { diagnostics })!;
        }

        private static Action<T> CreateDiagnosticCallbackCore<T>(
            ICollection<object> diagnostics) =>
            diagnostic => diagnostics.Add(diagnostic!);

        private static byte[] Hex(string value)
        {
            var bytes = new byte[value.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
                bytes[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            return bytes;
        }

        private static string Hex(byte[] value) =>
            string.Concat(value.Select(item => item.ToString("x2")));

        private static async Task EventuallyAsync(Func<bool> predicate)
        {
            var timeout = DateTime.UtcNow.AddSeconds(3);
            while (!predicate())
            {
                if (DateTime.UtcNow >= timeout) throw new TimeoutException("Condition was not reached.");
                await Task.Delay(10);
            }
        }

        private class RecordingInboundSink : IDiscordInboundTransportSink
        {
            public List<DiscordMessageCreateEnvelope> Messages { get; } =
                new List<DiscordMessageCreateEnvelope>();
            public List<DiscordInteractionEnvelope> Interactions { get; } =
                new List<DiscordInteractionEnvelope>();

            public virtual Task<DiscordInboundResult> HandleMessageAsync(
                DiscordMessageCreateEnvelope message,
                CancellationToken cancellationToken)
            {
                Messages.Add(message);
                return Task.FromResult(DiscordInboundResult.From(
                    DiscordInboundDisposition.Forwarded,
                    "forwarded"));
            }

            public Task<DiscordInboundResult> HandleInteractionAsync(
                DiscordInteractionEnvelope interaction,
                CancellationToken cancellationToken)
            {
                Interactions.Add(interaction);
                return Task.FromResult(DiscordInboundResult.From(
                    DiscordInboundDisposition.Dispatched,
                    "dispatched"));
            }
        }

        private sealed class BlockingInboundSink : RecordingInboundSink
        {
            public TaskCompletionSource<bool> Entered { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public override async Task<DiscordInboundResult> HandleMessageAsync(
                DiscordMessageCreateEnvelope message,
                CancellationToken cancellationToken)
            {
                Messages.Add(message);
                Entered.TrySetResult(true);
                await Release.Task;
                return DiscordInboundResult.From(
                    DiscordInboundDisposition.Forwarded,
                    "forwarded");
            }
        }

        private sealed class QueueGatewaySocketFactory : IDiscordGatewaySocketFactory
        {
            private readonly ConcurrentQueue<IDiscordGatewaySocket> sockets;
            private int createdCount;

            public QueueGatewaySocketFactory(params IDiscordGatewaySocket[] sockets) =>
                this.sockets = new ConcurrentQueue<IDiscordGatewaySocket>(sockets);

            public int CreatedCount => Volatile.Read(ref createdCount);

            public IDiscordGatewaySocket Create()
            {
                Interlocked.Increment(ref createdCount);
                if (!sockets.TryDequeue(out var socket))
                    throw new InvalidOperationException("No scripted gateway socket remains.");
                return socket;
            }
        }

        private sealed class ScriptedGatewaySocket : IDiscordGatewaySocket
        {
            private readonly ConcurrentQueue<string?> received = new ConcurrentQueue<string?>();
            private readonly SemaphoreSlim receivedSignal = new SemaphoreSlim(0);
            private readonly object sentSync = new object();
            private readonly List<string> sent = new List<string>();

            public IReadOnlyList<string> SentPayloads
            {
                get
                {
                    lock (sentSync) return sent.ToArray();
                }
            }

            public void Push(string payload)
            {
                received.Enqueue(payload);
                receivedSignal.Release();
            }

            public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
            {
                await receivedSignal.WaitAsync(cancellationToken);
                received.TryDequeue(out var payload);
                return payload;
            }

            public Task SendTextAsync(string payload, CancellationToken cancellationToken)
            {
                lock (sentSync) sent.Add(payload);
                return Task.CompletedTask;
            }

            public void Dispose() => receivedSignal.Dispose();
        }

        private sealed class FailingGatewaySocket : IDiscordGatewaySocket
        {
            public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("gateway-token-must-not-leak");

            public Task<string?> ReceiveTextAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task SendTextAsync(string payload, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public void Dispose()
            {
            }
        }

        private sealed class ControlledGatewayDelay : IDiscordGatewayDelay
        {
            private readonly ConcurrentQueue<PendingDelay> pending =
                new ConcurrentQueue<PendingDelay>();

            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                if (delay <= TimeSpan.Zero) return Task.CompletedTask;
                var item = new PendingDelay(delay, cancellationToken);
                pending.Enqueue(item);
                return item.Completion.Task;
            }

            public async Task ReleaseAsync(TimeSpan expected)
            {
                PendingDelay? item = null;
                await EventuallyAsync(() => pending.TryDequeue(out item));
                Assert.NotNull(item);
                Assert.Equal(expected, item!.Delay);
                item.Completion.TrySetResult(true);
            }

            public async Task<TimeSpan> ReleaseNextAsync()
            {
                PendingDelay? item = null;
                await EventuallyAsync(() => pending.TryDequeue(out item));
                Assert.NotNull(item);
                item!.Completion.TrySetResult(true);
                return item.Delay;
            }

            public async Task<TimeSpan> TakeNextAsync()
            {
                PendingDelay? item = null;
                await EventuallyAsync(() => pending.TryDequeue(out item));
                Assert.NotNull(item);
                return item!.Delay;
            }

            public async Task<TimeSpan?> ReleaseNextMatchingAsync(
                Func<TimeSpan, bool> predicate,
                TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow.Add(timeout);
                while (DateTime.UtcNow < deadline)
                {
                    if (pending.TryDequeue(out var item))
                    {
                        if (!predicate(item.Delay)) continue;
                        item.Completion.TrySetResult(true);
                        return item.Delay;
                    }
                    await Task.Delay(10);
                }
                return null;
            }

            private sealed class PendingDelay
            {
                public PendingDelay(TimeSpan delay, CancellationToken cancellationToken)
                {
                    Delay = delay;
                    Completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    cancellationToken.Register(() => Completion.TrySetCanceled(cancellationToken));
                }

                public TimeSpan Delay { get; }
                public TaskCompletionSource<bool> Completion { get; }
            }
        }
    }
}
