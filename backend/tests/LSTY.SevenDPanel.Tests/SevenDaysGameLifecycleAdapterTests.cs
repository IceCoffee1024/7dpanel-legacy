using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Lifecycle;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysGameLifecycleAdapterTests
    {
        [Fact]
        public void RegisterAndStart_subscribes_all_events_before_starting_runtime()
        {
            var trace = new List<string>();
            var events = new FakeLifecycleEvents(trace);
            var runtime = new RecordingRuntime(trace);
            var adapter = new SevenDaysGameLifecycleAdapter(runtime, events);

            adapter.RegisterAndStart();
            adapter.RegisterAndStart();

            Assert.Equal(
                new[] { "subscribe-world", "subscribe-game-shutdown", "subscribe-game-ready", "start" },
                trace);
        }

        [Fact]
        public void GameStartDone_marks_runtime_ready_without_starting_it_again()
        {
            var events = new FakeLifecycleEvents();
            var runtime = new RecordingRuntime();
            var adapter = new SevenDaysGameLifecycleAdapter(runtime, events);
            adapter.RegisterAndStart();

            events.RaiseGameStartDone();

            Assert.Equal(1, runtime.StartCount);
            Assert.Equal(1, runtime.MarkGameReadyCount);
            Assert.Equal(0, runtime.StopCount);
        }

        [Fact]
        public void Both_shutdown_events_stop_runtime_idempotently()
        {
            var events = new FakeLifecycleEvents();
            var runtime = new RecordingRuntime();
            var adapter = new SevenDaysGameLifecycleAdapter(runtime, events);
            adapter.RegisterAndStart();

            events.RaiseWorldShuttingDown();
            events.RaiseGameShutdown();

            Assert.Equal(2, runtime.StopCount);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Registration_failure_rolls_back_prior_subscriptions_in_reverse_order(int failureIndex)
        {
            var trace = new List<string>();
            var events = new FakeLifecycleEvents(trace) { FailureIndex = failureIndex };
            var runtime = new RecordingRuntime(trace);
            var adapter = new SevenDaysGameLifecycleAdapter(runtime, events);

            var exception = Assert.Throws<InvalidOperationException>(() => adapter.RegisterAndStart());

            Assert.Equal("registration failed", exception.Message);
            Assert.Equal(0, events.ActiveSubscriptionCount);
            Assert.Equal(0, runtime.StartCount);
            Assert.Equal(ExpectedRegistrationFailureTrace(failureIndex), trace);
        }

        [Fact]
        public void Start_failure_rolls_back_all_subscriptions_without_masking_original_exception()
        {
            var trace = new List<string>();
            var startException = new InvalidOperationException("start failed");
            var events = new FakeLifecycleEvents(trace) { DisposeFailureName = "game-shutdown" };
            var runtime = new RecordingRuntime(trace)
            {
                StartException = startException,
                StopException = new InvalidOperationException("stop failed")
            };
            var adapter = new SevenDaysGameLifecycleAdapter(runtime, events);

            var exception = Assert.Throws<InvalidOperationException>(() => adapter.RegisterAndStart());

            Assert.Same(startException, exception);
            Assert.Equal(0, events.ActiveSubscriptionCount);
            Assert.Equal(
                new[]
                {
                    "subscribe-world", "subscribe-game-shutdown", "subscribe-game-ready", "start",
                    "dispose-game-ready", "dispose-game-shutdown", "dispose-world", "stop"
                },
                trace);
        }

        [Fact]
        public void Dispose_releases_only_subscriptions_and_prevents_later_callbacks()
        {
            var trace = new List<string>();
            var events = new FakeLifecycleEvents(trace);
            var runtime = new RecordingRuntime(trace);
            var adapter = new SevenDaysGameLifecycleAdapter(runtime, events);
            adapter.RegisterAndStart();

            adapter.Dispose();
            adapter.Dispose();
            events.RaiseGameStartDone();
            events.RaiseWorldShuttingDown();
            events.RaiseGameShutdown();

            Assert.Equal(0, events.ActiveSubscriptionCount);
            Assert.Equal(0, runtime.MarkGameReadyCount);
            Assert.Equal(0, runtime.StopCount);
            Assert.Equal(
                new[]
                {
                    "subscribe-world", "subscribe-game-shutdown", "subscribe-game-ready", "start",
                    "dispose-game-ready", "dispose-game-shutdown", "dispose-world"
                },
                trace);
        }

        private static string[] ExpectedRegistrationFailureTrace(int failureIndex)
        {
            if (failureIndex == 1)
                return new[] { "subscribe-world" };
            if (failureIndex == 2)
                return new[] { "subscribe-world", "subscribe-game-shutdown", "dispose-world" };
            return new[]
            {
                "subscribe-world", "subscribe-game-shutdown", "subscribe-game-ready",
                "dispose-game-shutdown", "dispose-world"
            };
        }

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly IList<string>? trace;

            public RecordingRuntime(IList<string>? trace = null)
            {
                this.trace = trace;
            }

            public int StartCount { get; private set; }
            public int MarkGameReadyCount { get; private set; }
            public int StopCount { get; private set; }
            public Exception? StartException { get; set; }
            public Exception? StopException { get; set; }

            public void Start()
            {
                StartCount++;
                trace?.Add("start");
                if (StartException != null) throw StartException;
            }

            public void MarkGameReady()
            {
                MarkGameReadyCount++;
                trace?.Add("game-ready");
            }

            public void Stop()
            {
                StopCount++;
                trace?.Add("stop");
                if (StopException != null) throw StopException;
            }
        }

        private sealed class FakeLifecycleEvents : ISevenDaysLifecycleEvents
        {
            private readonly IList<string> trace;
            private readonly List<FakeSubscription> subscriptions = new List<FakeSubscription>();
            private int subscriptionCount;

            public FakeLifecycleEvents(IList<string>? trace = null)
            {
                this.trace = trace ?? new List<string>();
            }

            public int FailureIndex { get; set; }
            public string? DisposeFailureName { get; set; }
            public int ActiveSubscriptionCount => subscriptions.FindAll(subscription => subscription.IsActive).Count;

            public IDisposable SubscribeGameStartDone(Action handler) =>
                Subscribe("game-ready", handler);

            public IDisposable SubscribeWorldShuttingDown(Action handler) =>
                Subscribe("world", handler);

            public IDisposable SubscribeGameShutdown(Action handler) =>
                Subscribe("game-shutdown", handler);

            public void RaiseGameStartDone() => Raise("game-ready");
            public void RaiseWorldShuttingDown() => Raise("world");
            public void RaiseGameShutdown() => Raise("game-shutdown");

            private IDisposable Subscribe(string name, Action handler)
            {
                subscriptionCount++;
                trace.Add("subscribe-" + name);
                if (subscriptionCount == FailureIndex)
                    throw new InvalidOperationException("registration failed");

                var subscription = new FakeSubscription(
                    name,
                    handler,
                    trace,
                    () => string.Equals(name, DisposeFailureName, StringComparison.Ordinal));
                subscriptions.Add(subscription);
                return subscription;
            }

            private void Raise(string name)
            {
                foreach (var subscription in subscriptions)
                {
                    if (string.Equals(subscription.Name, name, StringComparison.Ordinal))
                        subscription.Invoke();
                }
            }
        }

        private sealed class FakeSubscription : IDisposable
        {
            private readonly Action handler;
            private readonly IList<string> trace;
            private readonly Func<bool> shouldFailOnDispose;

            public FakeSubscription(
                string name,
                Action handler,
                IList<string> trace,
                Func<bool> shouldFailOnDispose)
            {
                Name = name;
                this.handler = handler;
                this.trace = trace;
                this.shouldFailOnDispose = shouldFailOnDispose;
            }

            public string Name { get; }
            public bool IsActive { get; private set; } = true;

            public void Invoke()
            {
                if (IsActive) handler();
            }

            public void Dispose()
            {
                if (!IsActive) return;
                IsActive = false;
                trace.Add("dispose-" + Name);
                if (shouldFailOnDispose()) throw new InvalidOperationException("dispose failed");
            }
        }
    }
}
