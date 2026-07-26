using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ServerEventSseSessionTests
    {
        [Fact]
        public async Task Welcome_precedes_replay_and_duplicate_live_event_is_written_once_without_bom()
        {
            var replayed = ServerEvent.CreateConsoleLog(
                1,
                "formatted",
                "message",
                string.Empty,
                "log",
                new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc),
                1L);
            var live = ServerEvent.CreateGameReady(
                2,
                new DateTime(2026, 7, 21, 0, 1, 0, DateTimeKind.Utc));
            var stream = new FakeServerEventStream(
                new[] { replayed },
                new ServerEvent?[] { replayed, live, null });
            var authentication = new FakeAuthenticationStore();
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication);
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "bearer-token",
                PanelCredentialType.AccessToken,
                new[] { PanelUserIdentity.OwnerRole }));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, null, CancellationToken.None);

            var bytes = output.ToArray();
            Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
            var text = Encoding.UTF8.GetString(bytes);
            Assert.StartsWith("event: welcome\n", text);
            Assert.Equal(1, Count(text, "event: console-log\n"));
            Assert.Equal(1, Count(text, "id: 1\n"));
            Assert.Equal(1, Count(text, "event: game-ready\n"));
            Assert.Equal(1, Count(text, "id: 2\n"));
        }

        [Fact]
        public async Task Viewer_filters_console_logs_from_replay_and_live_but_keeps_lifecycle_events()
        {
            var replayLog = ServerEvent.CreateConsoleLog(
                1, "formatted", "replay", null, "log", DateTime.UtcNow, 1L);
            var gameReady = ServerEvent.CreateGameReady(2, DateTime.UtcNow);
            var liveLog = ServerEvent.CreateConsoleLog(
                3, "formatted", "live", null, "log", DateTime.UtcNow, 2L);
            var stopping = ServerEvent.CreateServerStopping(4, DateTime.UtcNow);
            var stream = new FakeServerEventStream(
                new[] { replayLog, gameReady },
                new ServerEvent?[] { liveLog, stopping, null });
            var authentication = new FakeAuthenticationStore
            {
                Role = PanelUserIdentity.ViewerRole
            };
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication);
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "bearer-token",
                PanelCredentialType.AccessToken,
                new[]
                {
                    PanelUserIdentity.OwnerRole,
                    PanelUserIdentity.AdminRole,
                    PanelUserIdentity.ViewerRole
                }));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, null, TestContext.Current.CancellationToken);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.DoesNotContain("event: console-log\n", text);
            Assert.Contains("event: game-ready\n", text);
            Assert.Contains("event: server-stopping\n", text);
        }

        [Fact]
        public async Task Owner_receives_chat_messages_from_replay_and_live()
        {
            var replayChat = CreateChatMessage(1, "replay");
            var liveChat = CreateChatMessage(2, "live");
            var stream = new FakeServerEventStream(
                new[] { replayChat },
                new ServerEvent?[] { liveChat, null });
            var authentication = new FakeAuthenticationStore();
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication);
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "bearer-token",
                PanelCredentialType.AccessToken,
                new[] { PanelUserIdentity.OwnerRole }));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, null, TestContext.Current.CancellationToken);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.Equal(2, Count(text, "event: chat-message\n"));
            Assert.Contains("\"message\":\"replay\"", text);
            Assert.Contains("\"message\":\"live\"", text);
        }

        [Theory]
        [InlineData(PanelUserIdentity.AdminRole)]
        [InlineData(PanelUserIdentity.ViewerRole)]
        public async Task Non_owner_filters_chat_from_replay_and_live_and_advances_cursor(string role)
        {
            var replayChat = CreateChatMessage(7, "replay");
            var stopping = ServerEvent.CreateServerStopping(8, DateTime.UtcNow);
            var liveChat = CreateChatMessage(9, "live");
            var stream = new FakeServerEventStream(
                new[] { replayChat },
                new ServerEvent?[] { stopping, liveChat, null },
                overflowed: true);
            var authentication = new FakeAuthenticationStore { Role = role };
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication);
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "bearer-token",
                PanelCredentialType.AccessToken,
                new[] { PanelUserIdentity.AdminRole, PanelUserIdentity.ViewerRole }));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, 6L, TestContext.Current.CancellationToken);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.DoesNotContain("event: chat-message\n", text);
            Assert.Contains("event: server-stopping\n", text);
            Assert.Contains("event: gap\n", text);
            Assert.Contains("\"afterSequence\":9", text);
        }

        [Fact]
        public async Task Filtered_live_event_advances_the_gap_cursor()
        {
            var liveLog = ServerEvent.CreateConsoleLog(
                7, "formatted", "live", null, "log", DateTime.UtcNow, 2L);
            var stream = new FakeServerEventStream(
                Array.Empty<ServerEvent>(),
                new ServerEvent?[] { liveLog, null },
                overflowed: true);
            var authentication = new FakeAuthenticationStore
            {
                Role = PanelUserIdentity.ViewerRole
            };
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication);
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "bearer-token",
                PanelCredentialType.AccessToken,
                new[] { PanelUserIdentity.ViewerRole }));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, 6L, TestContext.Current.CancellationToken);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.DoesNotContain("event: console-log\n", text);
            Assert.Contains("event: gap\n", text);
            Assert.Contains("\"afterSequence\":7", text);
        }

        [Fact]
        public async Task Invalidated_bearer_authorization_closes_before_the_next_event()
        {
            var now = new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero);
            var live = ServerEvent.CreateGameReady(
                1,
                new DateTime(2026, 7, 21, 0, 1, 0, DateTimeKind.Utc));
            var authentication = new FakeAuthenticationStore();
            var stream = new FakeServerEventStream(
                Array.Empty<ServerEvent>(),
                new ServerEvent?[] { live },
                () =>
                {
                    authentication.Active = false;
                    now = now.AddSeconds(2);
                });
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication,
                () => now,
                TimeSpan.FromSeconds(1));
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "bearer-token",
                PanelCredentialType.AccessToken,
                new[] { PanelUserIdentity.OwnerRole }));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, null, CancellationToken.None);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("event: welcome\n", text);
            Assert.DoesNotContain("event: game-ready\n", text);
        }

        [Fact]
        public async Task Revoked_api_key_authorization_closes_before_the_next_event()
        {
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
            var live = ServerEvent.CreateGameReady(
                1,
                new DateTime(2026, 7, 23, 0, 1, 0, DateTimeKind.Utc));
            var authentication = new FakeAuthenticationStore();
            var stream = new FakeServerEventStream(
                Array.Empty<ServerEvent>(),
                new ServerEvent?[] { live },
                () =>
                {
                    authentication.ApiKeyActive = false;
                    now = now.AddSeconds(2);
                });
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication,
                () => now,
                TimeSpan.FromSeconds(1));
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "api-key",
                PanelCredentialType.ApiKey,
                new[] { PanelUserIdentity.OwnerRole }));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, null, CancellationToken.None);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("event: welcome\n", text);
            Assert.DoesNotContain("event: game-ready\n", text);
            Assert.Equal(0, authentication.AccessTokenValidationCount);
            Assert.True(authentication.ApiKeyValidationCount >= 2);
        }

        [Fact]
        public async Task Disabled_api_key_owner_closes_before_the_next_event()
        {
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
            var live = ServerEvent.CreateGameReady(
                1,
                new DateTime(2026, 7, 23, 0, 1, 0, DateTimeKind.Utc));
            var authentication = new FakeAuthenticationStore();
            var stream = new FakeServerEventStream(
                Array.Empty<ServerEvent>(),
                new ServerEvent?[] { live },
                () =>
                {
                    authentication.Active = false;
                    now = now.AddSeconds(2);
                });
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication,
                () => now,
                TimeSpan.FromSeconds(1));
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "api-key",
                PanelCredentialType.ApiKey,
                new[] { PanelUserIdentity.OwnerRole }));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, null, CancellationToken.None);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("event: welcome\n", text);
            Assert.DoesNotContain("event: game-ready\n", text);
            Assert.Equal(0, authentication.AccessTokenValidationCount);
            Assert.True(authentication.ApiKeyValidationCount >= 2);
        }

        [Fact]
        public async Task Role_outside_the_allowed_set_closes_before_the_next_event()
        {
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
            var live = ServerEvent.CreateGameReady(
                1,
                new DateTime(2026, 7, 23, 0, 1, 0, DateTimeKind.Utc));
            var authentication = new FakeAuthenticationStore();
            var stream = new FakeServerEventStream(
                Array.Empty<ServerEvent>(),
                new ServerEvent?[] { live },
                () =>
                {
                    authentication.Role = PanelUserIdentity.ViewerRole;
                    now = now.AddSeconds(2);
                });
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication,
                () => now,
                TimeSpan.FromSeconds(1));
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "bearer-token",
                PanelCredentialType.AccessToken,
                new[] { PanelUserIdentity.OwnerRole }));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, null, CancellationToken.None);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("event: welcome\n", text);
            Assert.DoesNotContain("event: game-ready\n", text);
        }

        [Fact]
        public void Missing_bearer_credential_is_rejected()
        {
            var authentication = new FakeAuthenticationStore();
            using var session = new ServerEventSseSession(
                new FakeServerEventStream(Array.Empty<ServerEvent>(), Array.Empty<ServerEvent?>()),
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication);

            Assert.False(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                null,
                PanelCredentialType.AccessToken,
                new[] { PanelUserIdentity.OwnerRole }));
        }

        [Fact]
        public async Task Expired_api_key_closes_at_its_expiration_boundary()
        {
            var now = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
            var live = ServerEvent.CreateGameReady(
                1,
                new DateTime(2026, 7, 23, 0, 1, 0, DateTimeKind.Utc));
            var authentication = new FakeAuthenticationStore
            {
                ApiKeyExpiresUtc = now.AddSeconds(1)
            };
            var stream = new FakeServerEventStream(
                Array.Empty<ServerEvent>(),
                new ServerEvent?[] { live },
                () => now = now.AddSeconds(2));
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication,
                () => now,
                TimeSpan.FromSeconds(15));
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "api-key",
                PanelCredentialType.ApiKey,
                new[] { PanelUserIdentity.OwnerRole }));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, null, CancellationToken.None);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("event: welcome\n", text);
            Assert.DoesNotContain("event: game-ready\n", text);
            Assert.True(authentication.ApiKeyValidationCount >= 2);
        }

        [Fact]
        public async Task Idle_api_key_stream_closes_at_its_expiration_boundary()
        {
            var now = DateTimeOffset.UtcNow;
            var authentication = new FakeAuthenticationStore
            {
                ApiKeyExpiresUtc = now.AddMilliseconds(100)
            };
            var stream = new FakeServerEventStream(
                Array.Empty<ServerEvent>(),
                Array.Empty<ServerEvent?>());
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication,
                () => DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(15));
            using var output = new MemoryStream();
            using var cancellation = new CancellationTokenSource();

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "api-key",
                PanelCredentialType.ApiKey,
                new[] { PanelUserIdentity.OwnerRole }));
            Assert.True(session.TryReserve());

            var writeTask = session.WriteAsync(output, null, cancellation.Token);
            var completed = await Task.WhenAny(
                writeTask,
                Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
            if (completed != writeTask)
            {
                cancellation.Cancel();
                await writeTask;
            }

            Assert.Same(writeTask, completed);
            Assert.True(authentication.ApiKeyValidationCount >= 2);
            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("event: welcome\n", text);
            Assert.DoesNotContain(": keep-alive\n\n", text);
        }

        [Fact]
        public async Task Idle_stream_writes_heartbeats_while_authorization_remains_valid()
        {
            var authentication = new FakeAuthenticationStore();
            var stream = new FakeServerEventStream(
                Array.Empty<ServerEvent>(),
                Array.Empty<ServerEvent?>());
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication,
                () => DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(15),
                TimeSpan.FromMilliseconds(25));
            using var output = new MemoryStream();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "bearer-token",
                PanelCredentialType.AccessToken,
                new[] { PanelUserIdentity.OwnerRole }));
            Assert.True(session.TryReserve());

            var writeTask = session.WriteAsync(output, null, cancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(125), TestContext.Current.CancellationToken);
            cancellation.Cancel();
            await writeTask;

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("event: welcome\n", text);
            Assert.Contains(": keep-alive\n\n", text);
        }

        [Fact]
        public async Task Idle_stream_writes_a_heartbeat_when_authorization_and_heartbeat_are_due_together()
        {
            var start = new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);
            var now = start;
            var canceledReadCount = 0;
            var authentication = new FakeAuthenticationStore();
            authentication.AfterAccessTokenValidation = () =>
            {
                if (authentication.AccessTokenValidationCount > 1)
                    now = now.AddMilliseconds(1);
            };
            var stream = new FakeServerEventStream(
                Array.Empty<ServerEvent>(),
                Array.Empty<ServerEvent?>(),
                null,
                () =>
                {
                    canceledReadCount++;
                    now = start.AddMilliseconds(canceledReadCount * 20);
                });
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus(),
                authentication,
                authentication,
                authentication,
                () => now,
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(20));
            using var output = new MemoryStream();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

            Assert.True(session.TryAuthorize(
                FakeAuthenticationStore.Subject,
                "bearer-token",
                PanelCredentialType.AccessToken,
                new[] { PanelUserIdentity.OwnerRole }));
            Assert.True(session.TryReserve());
            now = now.AddMilliseconds(1);

            var writeTask = session.WriteAsync(output, null, cancellation.Token);
            Assert.True(SpinWait.SpinUntil(
                () => authentication.AccessTokenValidationCount >= 3,
                TimeSpan.FromSeconds(1)));
            cancellation.Cancel();
            await writeTask;

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("event: welcome\n", text);
            Assert.Contains(": keep-alive\n\n", text);
        }

        private static int Count(string value, string fragment)
        {
            var count = 0;
            var startIndex = 0;
            while ((startIndex = value.IndexOf(
                fragment,
                startIndex,
                StringComparison.Ordinal)) >= 0)
            {
                count++;
                startIndex += fragment.Length;
            }

            return count;
        }

        private static ServerEvent CreateChatMessage(long sequence, string message) =>
            ServerEvent.CreateChatMessage(
                sequence,
                new DateTimeOffset(2026, 7, 26, 1, 2, 3, TimeSpan.Zero),
                42,
                "EOS_123",
                "Alice",
                "Global",
                "Player",
                message);

        private sealed class FakeRuntimeStatus : IPanelRuntimeStatus
        {
            public ModHostState State => ModHostState.Running;
            public GameReadinessState GameReadiness => GameReadinessState.Ready;
        }

        private sealed class FakeServerEventStream : IServerEventStream
        {
            private readonly IReadOnlyList<ServerEvent> replay;
            private readonly IReadOnlyList<ServerEvent?> live;
            private readonly Action? onRead;
            private readonly Action? onCanceledRead;
            private readonly bool overflowed;

            public FakeServerEventStream(
                IReadOnlyList<ServerEvent> replay,
                IReadOnlyList<ServerEvent?> live,
                Action? onRead = null,
                Action? onCanceledRead = null,
                bool overflowed = false)
            {
                this.replay = replay;
                this.live = live;
                this.onRead = onRead;
                this.onCanceledRead = onCanceledRead;
                this.overflowed = overflowed;
            }

            public IReadOnlyList<ServerEvent> ReadAfter(
                long? afterSequence,
                int limit,
                out bool hasGap)
            {
                hasGap = false;
                return replay;
            }

            public bool TrySubscribe(
                int capacity,
                out IServerEventSubscription? subscription)
            {
                subscription = new FakeSubscription(live, onRead, onCanceledRead, overflowed);
                return true;
            }
        }

        private sealed class FakeSubscription : IServerEventSubscription
        {
            private readonly Queue<ServerEvent?> events;
            private readonly Action? onRead;
            private readonly Action? onCanceledRead;
            private readonly bool overflowed;

            public FakeSubscription(
                IEnumerable<ServerEvent?> events,
                Action? onRead,
                Action? onCanceledRead,
                bool overflowed)
            {
                this.events = new Queue<ServerEvent?>(events);
                this.onRead = onRead;
                this.onCanceledRead = onCanceledRead;
                this.overflowed = overflowed;
            }

            public bool IsOverflowed => overflowed;

            public async Task<ServerEvent?> ReadAsync(CancellationToken cancellationToken)
            {
                onRead?.Invoke();
                if (events.Count == 0)
                {
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        onCanceledRead?.Invoke();
                        throw;
                    }
                    return null;
                }

                return events.Dequeue();
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeAuthenticationStore :
            IPanelCredentialStore,
            IPanelAccessTokenStore,
            IPanelApiKeyStore
        {
            public const string Subject = "owner";

            public bool Active { get; set; } = true;
            public bool ApiKeyActive { get; set; } = true;
            public DateTimeOffset? ApiKeyExpiresUtc { get; set; }
            public string Role { get; set; } = PanelUserIdentity.OwnerRole;
            public Action? AfterAccessTokenValidation { get; set; }
            public int AccessTokenValidationCount { get; private set; }
            public int ApiKeyValidationCount { get; private set; }

            private PanelUserIdentity Identity =>
                new PanelUserIdentity(Subject, "Owner", Role);

            public bool TryVerify(
                string username,
                string password,
                out PanelUserIdentity panelIdentity)
            {
                panelIdentity = null!;
                if (!Active || !string.Equals(username, Identity.Username, StringComparison.Ordinal))
                    return false;
                panelIdentity = Identity;
                return true;
            }

            public bool TryGetActive(string subject, out PanelUserIdentity panelIdentity)
            {
                panelIdentity = null!;
                if (!Active || !string.Equals(subject, Subject, StringComparison.Ordinal))
                    return false;
                panelIdentity = Identity;
                return true;
            }

            public string Issue(
                PanelUserIdentity panelIdentity,
                DateTimeOffset issuedUtc,
                DateTimeOffset expiresUtc) => "bearer-token";

            public bool TryValidate(
                string token,
                DateTimeOffset utcNow,
                out StoredAccessToken storedToken)
            {
                AccessTokenValidationCount++;
                AfterAccessTokenValidation?.Invoke();
                storedToken = null!;
                if (!Active || !string.Equals(token, "bearer-token", StringComparison.Ordinal))
                    return false;
                storedToken = new StoredAccessToken(Identity, utcNow, utcNow.AddMinutes(1));
                return true;
            }

            public ApiKeyCreateResult Create(
                string subject,
                string name,
                DateTimeOffset createdUtc,
                DateTimeOffset? expiresUtc) =>
                throw new NotSupportedException();

            public IReadOnlyList<StoredApiKey> List(string subject, DateTimeOffset utcNow) =>
                Array.Empty<StoredApiKey>();

            public bool Revoke(string subject, string keyId, DateTimeOffset revokedUtc) => false;

            public bool TryValidate(
                string apiKey,
                DateTimeOffset utcNow,
                out StoredApiKey storedApiKey)
            {
                ApiKeyValidationCount++;
                storedApiKey = null!;
                if (!Active ||
                    !ApiKeyActive ||
                    ApiKeyExpiresUtc.HasValue && ApiKeyExpiresUtc.Value <= utcNow ||
                    !string.Equals(apiKey, "api-key", StringComparison.Ordinal))
                    return false;

                storedApiKey = new StoredApiKey(
                    "api-key-id",
                    Identity,
                    "test",
                    utcNow,
                    null,
                    ApiKeyExpiresUtc,
                    null,
                    utcNow);
                return true;
            }
        }
    }
}
