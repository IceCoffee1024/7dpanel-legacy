using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Hosting;
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
            using var session = new ServerEventSseSession(
                stream,
                new FakeRuntimeStatus());
            using var output = new MemoryStream();

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

            public FakeServerEventStream(
                IReadOnlyList<ServerEvent> replay,
                IReadOnlyList<ServerEvent?> live)
            {
                this.replay = replay;
                this.live = live;
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
                subscription = new FakeSubscription(live);
                return true;
            }
        }

        private sealed class FakeSubscription : IServerEventSubscription
        {
            private readonly Queue<ServerEvent?> events;

            public FakeSubscription(IEnumerable<ServerEvent?> events)
            {
                this.events = new Queue<ServerEvent?>(events);
            }

            public bool IsOverflowed => false;

            public Task<ServerEvent?> ReadAsync(CancellationToken cancellationToken) =>
                Task.FromResult(events.Dequeue());

            public void Dispose()
            {
            }
        }
    }
}
