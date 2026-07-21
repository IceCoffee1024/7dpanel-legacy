using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ConsoleLogServiceTests
    {
        [Fact]
        public void Panel_runtime_orders_lifecycle_events_after_accepted_logs_and_publishes_ready_once()
        {
            var source = new FakeLogSource();
            var window = new ServerEventLiveWindow(4);
            using var service = CreateService(source, window);
            var runtime = new ConsoleLogRuntime(service, new RecordingRuntime(new List<string>()));

            runtime.Start();
            source.Publish(CreateEntry("accepted"));
            runtime.MarkGameReady();
            runtime.MarkGameReady();
            runtime.Stop();

            var events = window.ReadAfter(null, 10).Entries;
            Assert.Equal(
                new[]
                {
                    ServerEventNames.ConsoleLog,
                    ServerEventNames.GameReady,
                    ServerEventNames.ServerStopping
                },
                events.Select(entry => entry.EventName));
            Assert.Equal(new[] { 1L, 2L, 3L }, events.Select(entry => entry.Sequence));
        }

        [Fact]
        public void Accepted_entries_are_consumed_once_in_order_with_all_fields()
        {
            var source = new FakeLogSource();
            var window = new ServerEventLiveWindow(4);
            using var service = CreateService(source, window);
            var timestamp = new DateTime(2026, 7, 20, 8, 9, 10, DateTimeKind.Utc);

            service.Start();
            source.Publish(new ConsoleLogEntry(
                "formatted:first",
                "first",
                "trace",
                ConsoleLogType.Warning,
                timestamp,
                1234L));
            source.Publish(CreateEntry("second"));

            Assert.True(SpinWait.SpinUntil(
                () => service.ConsumedCount == 2,
                TimeSpan.FromSeconds(5)));
            service.Stop();

            var entries = ReadConsoleLogs(window);
            var first = entries[0];
            Assert.Equal(new[] { "first", "second" }, entries.Select(entry => entry.Message));
            Assert.Equal("formatted:first", first.FormattedMessage);
            Assert.Equal("trace", first.Trace);
            Assert.Equal("warning", first.LogType);
            Assert.Equal(timestamp, first.Timestamp);
            Assert.Equal(1234L, first.UptimeMilliseconds);
            Assert.Equal(new[] { 1L, 2L }, entries.Select(entry => entry.Sequence));
            Assert.Equal(1, source.SubscribeCount);
            Assert.Equal(1, source.DisposeCount);
        }

        [Fact]
        public async Task Consumed_entry_is_published_through_the_stream_with_its_sequence()
        {
            var source = new FakeLogSource();
            using var service = CreateService(source, new ServerEventLiveWindow(2));
            Assert.True(service.Stream.TrySubscribe(1, out var subscription));
            using var activeSubscription = Assert.IsAssignableFrom<IServerEventSubscription>(subscription);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromSeconds(5));

            service.Start();
            source.Publish(CreateEntry("streamed"));

            var streamEvent = await activeSubscription.ReadAsync(cancellation.Token);
            service.Stop();

            Assert.NotNull(streamEvent);
            Assert.Equal(1L, streamEvent.Sequence);
            var data = Assert.IsType<ConsoleLogEventData>(streamEvent.Data);
            Assert.Equal("streamed", data.Message);
            Assert.Equal("formatted:streamed", data.FormattedMessage);
        }

        [Fact]
        public void Source_callback_never_runs_window_append_on_the_callback_thread()
        {
            var source = new FakeLogSource();
            var window = new ServerEventLiveWindow(2);
            using var appendEntered = new ManualResetEventSlim();
            using var releaseAppend = new ManualResetEventSlim();
            var appendThreadId = 0;
            using var service = new ConsoleLogService(
                window,
                2,
                TimeSpan.FromSeconds(5),
                source.Subscribe,
                entry =>
                {
                    appendThreadId = Environment.CurrentManagedThreadId;
                    appendEntered.Set();
                    releaseAppend.Wait();
                    return window.AppendConsoleLog(entry);
                });

            service.Start();
            var callbackThreadId = Environment.CurrentManagedThreadId;
            source.Publish(CreateEntry("one"));

            try
            {
                Assert.True(appendEntered.Wait(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));
                Assert.NotEqual(callbackThreadId, appendThreadId);
            }
            finally
            {
                releaseAppend.Set();
            }

            service.Stop();
        }

        [Fact]
        public void Full_queue_is_rejected_without_waiting_and_is_counted()
        {
            var source = new FakeLogSource();
            var window = new ServerEventLiveWindow(4);
            using var appendEntered = new ManualResetEventSlim();
            using var releaseAppend = new ManualResetEventSlim();
            using var service = new ConsoleLogService(
                window,
                1,
                TimeSpan.FromSeconds(5),
                source.Subscribe,
                entry =>
                {
                    appendEntered.Set();
                    releaseAppend.Wait();
                    return window.AppendConsoleLog(entry);
                });

            service.Start();
            Assert.True(service.TryPublish(CreateEntry("one")));
            Assert.True(appendEntered.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
            Assert.True(service.TryPublish(CreateEntry("two")));

            var startedAt = DateTime.UtcNow;
            Assert.False(service.TryPublish(CreateEntry("three")));
            Assert.True(DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(1));
            Assert.Equal(1L, service.DroppedFullCount);
            Assert.Equal(1, service.HighWaterMark);

            releaseAppend.Set();
            service.Stop();
            Assert.Equal(2L, service.AcceptedCount);
            Assert.Equal(2L, service.ConsumedCount);
        }

        [Fact]
        public void Single_append_failure_is_counted_and_later_entries_continue()
        {
            var source = new FakeLogSource();
            var window = new ServerEventLiveWindow(2);
            var appendCalls = 0;
            using var service = new ConsoleLogService(
                window,
                2,
                TimeSpan.FromSeconds(5),
                source.Subscribe,
                entry =>
                {
                    if (Interlocked.Increment(ref appendCalls) == 1)
                        throw new InvalidOperationException("append failed");
                    return window.AppendConsoleLog(entry);
                });

            service.Start();
            source.Publish(CreateEntry("first"));
            source.Publish(CreateEntry("second"));

            Assert.True(SpinWait.SpinUntil(
                () => service.ConsumerFailureCount == 1 && service.ConsumedCount == 1,
                TimeSpan.FromSeconds(5)));
            service.Stop();

            Assert.Equal(
                "second",
                Assert.Single(ReadConsoleLogs(window)).Message);
        }

        [Fact]
        public void Stop_unsubscribes_before_rejecting_late_publication_and_draining()
        {
            var source = new FakeLogSource { PublishOnDispose = CreateEntry("late") };
            var messages = new List<string>();
            var window = new ServerEventLiveWindow(2);
            using var service = CreateService(source, window, messages.Add);

            service.Start();
            source.Publish(CreateEntry("accepted"));
            service.Stop();

            Assert.Equal(1, source.DisposeCount);
            Assert.Equal(1L, service.RejectedStoppingCount);
            Assert.Equal(
                "accepted",
                Assert.Single(ReadConsoleLogs(window)).Message);
            Assert.Single(messages);
            Assert.Contains("accepted=1", messages[0]);
        }

        [Fact]
        public void Subscribe_failure_is_preserved_and_service_stops_accepting()
        {
            var expected = new InvalidOperationException("subscribe failed");
            var source = new FakeLogSource { SubscribeException = expected };
            using var service = CreateService(source, new ServerEventLiveWindow(2));

            var actual = Assert.Throws<InvalidOperationException>(service.Start);

            Assert.Same(expected, actual);
            Assert.False(service.TryPublish(CreateEntry("rejected")));
            Assert.Equal(1L, service.RejectedStoppingCount);
        }

        [Fact]
        public void Panel_runtime_starts_logs_before_inner_runtime_and_stops_them_first()
        {
            var calls = new List<string>();
            var source = new FakeLogSource(calls);
            using var service = CreateService(source, new ServerEventLiveWindow(2));
            var inner = new RecordingRuntime(calls);
            var runtime = new ConsoleLogRuntime(service, inner);

            runtime.Start();
            runtime.MarkGameReady();
            runtime.Stop();

            Assert.Equal(
                new[] { "logs:start", "inner:start", "inner:ready", "logs:stop", "inner:stop" },
                calls);
        }

        [Fact]
        public void Panel_runtime_stops_inner_runtime_when_log_drain_times_out()
        {
            var calls = new List<string>();
            var source = new FakeLogSource(calls);
            var window = new ServerEventLiveWindow(2);
            using var appendEntered = new ManualResetEventSlim();
            using var releaseAppend = new ManualResetEventSlim();
            using var service = new ConsoleLogService(
                window,
                2,
                TimeSpan.FromMilliseconds(50),
                source.Subscribe,
                entry =>
                {
                    appendEntered.Set();
                    releaseAppend.Wait();
                    return window.AppendConsoleLog(entry);
                });
            var inner = new RecordingRuntime(calls);
            var runtime = new ConsoleLogRuntime(service, inner);

            runtime.Start();
            source.Publish(CreateEntry("blocked"));
            Assert.True(appendEntered.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));

            try
            {
                var exception = Assert.Throws<AggregateException>(runtime.Stop);
                Assert.Contains(
                    exception.Flatten().InnerExceptions,
                    error => error is TimeoutException);
                Assert.Contains("inner:stop", calls);
            }
            finally
            {
                releaseAppend.Set();
            }
        }

        private static ConsoleLogService CreateService(
            FakeLogSource source,
            ServerEventLiveWindow window,
            Action<string>? log = null) =>
            new ConsoleLogService(
                window,
                8,
                TimeSpan.FromSeconds(5),
                source.Subscribe,
                null,
                log);

        private static ConsoleLogEntry CreateEntry(string message) =>
            new ConsoleLogEntry(
                "formatted:" + message,
                message,
                string.Empty,
                ConsoleLogType.Log,
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                0L);

        private static IReadOnlyList<ConsoleLogEventData> ReadConsoleLogs(
            ServerEventLiveWindow window) =>
            window.ReadAfter(null, 100).Entries
                .Where(entry => entry.EventName == ServerEventNames.ConsoleLog)
                .Select(entry => Assert.IsType<ConsoleLogEventData>(entry.Data))
                .ToArray();

        private sealed class FakeLogSource
        {
            private readonly IList<string>? calls;
            private Action<ConsoleLogEntry>? handler;

            public FakeLogSource(IList<string>? calls = null)
            {
                this.calls = calls;
            }

            public int SubscribeCount { get; private set; }
            public int DisposeCount { get; private set; }
            public Exception? SubscribeException { get; set; }
            public ConsoleLogEntry? PublishOnDispose { get; set; }

            public IDisposable Subscribe(Action<ConsoleLogEntry> callback)
            {
                if (SubscribeException != null) throw SubscribeException;
                SubscribeCount++;
                calls?.Add("logs:start");
                handler = callback;
                return new CallbackDisposable(() =>
                {
                    DisposeCount++;
                    calls?.Add("logs:stop");
                    var candidate = Interlocked.Exchange(ref handler, null);
                    if (PublishOnDispose != null) candidate?.Invoke(PublishOnDispose);
                });
            }

            public void Publish(ConsoleLogEntry entry) => handler?.Invoke(entry);
        }

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly IList<string> calls;

            public RecordingRuntime(IList<string> calls)
            {
                this.calls = calls;
            }

            public void Start() => calls.Add("inner:start");
            public void MarkGameReady() => calls.Add("inner:ready");
            public void Stop() => calls.Add("inner:stop");
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action? dispose;

            public CallbackDisposable(Action dispose)
            {
                this.dispose = dispose;
            }

            public void Dispose() => Interlocked.Exchange(ref dispose, null)?.Invoke();
        }
    }
}
