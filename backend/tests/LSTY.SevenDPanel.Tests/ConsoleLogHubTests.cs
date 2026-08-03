using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class ConsoleLogHubTests
    {
        [Fact]
        public async Task Publish_broadcasts_the_same_sequenced_event_to_each_subscriber()
        {
            var window = new ServerEventLiveWindow(4);
            var hub = new ServerEventHub(window, 2);
            Assert.True(hub.TrySubscribe(2, out var first));
            Assert.True(hub.TrySubscribe(2, out var second));
            var firstSubscription = Assert.IsAssignableFrom<IServerEventSubscription>(first);
            var secondSubscription = Assert.IsAssignableFrom<IServerEventSubscription>(second);
            using (firstSubscription)
            using (secondSubscription)
            {
                var retained = window.AppendConsoleLog(CreateEntry("one"));
                hub.Publish(retained);

                var firstEvent = await firstSubscription.ReadAsync(TestContext.Current.CancellationToken);
                var secondEvent = await secondSubscription.ReadAsync(TestContext.Current.CancellationToken);

                Assert.NotNull(firstEvent);
                Assert.NotNull(secondEvent);
                Assert.Equal(1L, firstEvent.Sequence);
                Assert.Equal("one", Assert.IsType<ConsoleLogEventData>(firstEvent.Data).Message);
                Assert.Equal(firstEvent.Sequence, secondEvent.Sequence);
                Assert.Equal("log", Assert.IsType<ConsoleLogEventData>(firstEvent.Data).LogType);
            }
        }

        [Fact]
        public async Task Full_mailbox_overflows_only_the_slow_subscriber()
        {
            var window = new ServerEventLiveWindow(4);
            var hub = new ServerEventHub(window, 2);
            Assert.True(hub.TrySubscribe(1, out var slow));
            Assert.True(hub.TrySubscribe(2, out var fast));
            var slowSubscription = Assert.IsAssignableFrom<IServerEventSubscription>(slow);
            var fastSubscription = Assert.IsAssignableFrom<IServerEventSubscription>(fast);
            using (slowSubscription)
            using (fastSubscription)
            {
                hub.Publish(window.AppendConsoleLog(CreateEntry("one")));
                Assert.NotNull(await fastSubscription.ReadAsync(TestContext.Current.CancellationToken));

                hub.Publish(window.AppendConsoleLog(CreateEntry("two")));

                Assert.True(slowSubscription.IsOverflowed);
                Assert.Equal(1, hub.SubscriberCount);
                Assert.Equal(
                    "two",
                    Assert.IsType<ConsoleLogEventData>((await fastSubscription.ReadAsync(
                        TestContext.Current.CancellationToken))?.Data).Message);
                Assert.Equal(
                    "one",
                    Assert.IsType<ConsoleLogEventData>((await slowSubscription.ReadAsync(
                        TestContext.Current.CancellationToken))?.Data).Message);
                Assert.Null(await slowSubscription.ReadAsync(TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public void Subscriber_limit_and_completion_reject_new_subscriptions()
        {
            var hub = new ServerEventHub(new ServerEventLiveWindow(2), 1);
            Assert.True(hub.TrySubscribe(1, out var first));
            using (first!)
            {
                Assert.False(hub.TrySubscribe(1, out var second));
                Assert.Null(second);
                hub.Complete();
                Assert.False(hub.TrySubscribe(1, out var afterCompletion));
                Assert.Null(afterCompletion);
            }
        }

        [Fact]
        public void Read_after_maps_window_entries_and_preserves_gap()
        {
            var window = new ServerEventLiveWindow(2);
            var hub = new ServerEventHub(window);
            window.AppendConsoleLog(CreateEntry("one"));
            window.AppendConsoleLog(CreateEntry("two"));
            window.AppendConsoleLog(CreateEntry("three"));

            var entries = hub.ReadAfter(0L, 10, out var hasGap);

            Assert.True(hasGap);
            Assert.Equal(new[] { 2L, 3L }, entries.Select(entry => entry.Sequence));
            Assert.Equal(
                new[] { "two", "three" },
                entries.Select(entry => Assert.IsType<ConsoleLogEventData>(entry.Data).Message));
        }

        [Fact]
        public void Cursor_from_a_previous_process_reports_gap_and_replays_current_window()
        {
            var window = new ServerEventLiveWindow(2);
            var hub = new ServerEventHub(window);
            window.AppendConsoleLog(CreateEntry("one"));
            window.AppendConsoleLog(CreateEntry("two"));

            var entries = hub.ReadAfter(99L, 10, out var hasGap);

            Assert.True(hasGap);
            Assert.Equal(new[] { 1L, 2L }, entries.Select(entry => entry.Sequence));
        }

        [Fact]
        public void Zero_cursor_on_an_empty_window_does_not_report_a_gap()
        {
            var hub = new ServerEventHub(new ServerEventLiveWindow(2));

            var entries = hub.ReadAfter(0L, 10, out var hasGap);

            Assert.False(hasGap);
            Assert.Empty(entries);
        }

        [Fact]
        public async Task Complete_releases_waiting_subscribers()
        {
            var hub = new ServerEventHub(new ServerEventLiveWindow(2));
            Assert.True(hub.TrySubscribe(1, out var subscription));
            var activeSubscription = Assert.IsAssignableFrom<IServerEventSubscription>(subscription);
            using (activeSubscription)
            {
                var read = activeSubscription.ReadAsync(TestContext.Current.CancellationToken);

                hub.Complete();

                Assert.Null(await read);
                Assert.False(activeSubscription.IsOverflowed);
            }
        }

        private static ConsoleLogEntry CreateEntry(string message) =>
            new ConsoleLogEntry(
                "formatted:" + message,
                message,
                string.Empty,
                ConsoleLogType.Log,
                new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
                1L);
    }
}
