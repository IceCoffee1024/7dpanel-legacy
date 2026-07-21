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
                authentication);
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(FakeAuthenticationStore.Subject, null));
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
                () => now,
                TimeSpan.FromSeconds(1));
            using var output = new MemoryStream();

            Assert.True(session.TryAuthorize(FakeAuthenticationStore.Subject, "bearer-token"));
            Assert.True(session.TryReserve());
            await session.WriteAsync(output, null, CancellationToken.None);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("event: welcome\n", text);
            Assert.DoesNotContain("event: game-ready\n", text);
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

            public FakeServerEventStream(
                IReadOnlyList<ServerEvent> replay,
                IReadOnlyList<ServerEvent?> live,
                Action? onRead = null)
            {
                this.replay = replay;
                this.live = live;
                this.onRead = onRead;
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
                subscription = new FakeSubscription(live, onRead);
                return true;
            }
        }

        private sealed class FakeSubscription : IServerEventSubscription
        {
            private readonly Queue<ServerEvent?> events;
            private readonly Action? onRead;

            public FakeSubscription(IEnumerable<ServerEvent?> events, Action? onRead)
            {
                this.events = new Queue<ServerEvent?>(events);
                this.onRead = onRead;
            }

            public bool IsOverflowed => false;

            public Task<ServerEvent?> ReadAsync(CancellationToken cancellationToken)
            {
                onRead?.Invoke();
                return Task.FromResult(events.Dequeue());
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeAuthenticationStore :
            IPanelCredentialStore,
            IPanelAccessTokenStore
        {
            public const string Subject = "owner";

            private readonly PanelUserIdentity identity =
                new PanelUserIdentity(Subject, "Owner");

            public bool Active { get; set; } = true;

            public bool TryVerify(
                string username,
                string password,
                out PanelUserIdentity panelIdentity)
            {
                panelIdentity = null!;
                if (!Active || !string.Equals(username, identity.Username, StringComparison.Ordinal))
                    return false;
                panelIdentity = identity;
                return true;
            }

            public bool TryGetActive(string subject, out PanelUserIdentity panelIdentity)
            {
                panelIdentity = null!;
                if (!Active || !string.Equals(subject, Subject, StringComparison.Ordinal))
                    return false;
                panelIdentity = identity;
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
                storedToken = null!;
                if (!Active || !string.Equals(token, "bearer-token", StringComparison.Ordinal))
                    return false;
                storedToken = new StoredAccessToken(identity, utcNow, utcNow.AddMinutes(1));
                return true;
            }
        }
    }
}
