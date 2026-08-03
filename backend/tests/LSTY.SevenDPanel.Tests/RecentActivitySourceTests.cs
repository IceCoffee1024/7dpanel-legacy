using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Activity;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
using Microsoft.Owin;
using Microsoft.Owin.Security.OAuth;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class RecentActivitySourceTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

        [Fact]
        public async Task Successful_password_grant_records_only_the_safe_panel_login_values()
        {
            var writer = new RecordingRecentActivityWriter();
            var provider = CreateProvider(writer);
            var context = CreateGrantContext();

            await provider.GrantResourceOwnerCredentials(context);
            Assert.True(writer.PanelLoginRecorded.Wait(TestTimeout));

            Assert.True(context.IsValidated);
            var login = Assert.Single(writer.PanelLogins);
            Assert.Equal("owner-42", login.Subject);
            Assert.Equal("Owner Display", login.DisplayName);
            Assert.Equal(TimeSpan.Zero, login.OccurredAtUtc.Offset);
        }

        [Fact]
        public async Task Password_grant_awaits_isolated_writer_without_blocking_the_caller_thread()
        {
            var writer = new RecordingRecentActivityWriter { BlockPanelLoginBeforeTask = true };
            var provider = CreateProvider(writer);
            var context = CreateGrantContext();
            var callReturned = new TaskCompletionSource<Task>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _ = Task.Run(() =>
                callReturned.TrySetResult(provider.GrantResourceOwnerCredentials(context)));
            Assert.True(writer.PanelLoginEntered.Wait(TestTimeout));
            var grant = await callReturned.Task;
            var completedBeforeWriterRelease = grant.IsCompleted;

            writer.PanelLoginRelease.Set();
            await grant;

            Assert.False(completedBeforeWriterRelease);
            Assert.True(context.IsValidated);
        }

        [Theory]
        [InlineData(WriterFailure.SynchronousThrow)]
        [InlineData(WriterFailure.FaultedTask)]
        [InlineData(WriterFailure.CanceledTask)]
        public async Task Panel_login_writer_failures_are_observed_with_one_fixed_warning(
            WriterFailure failure)
        {
            var writer = new RecordingRecentActivityWriter { PanelLoginFailure = failure };
            var log = new RecordingLog();
            var provider = CreateProvider(writer, log.Write);
            var context = CreateGrantContext();

            await provider.GrantResourceOwnerCredentials(context);
            Assert.True(log.Written.Wait(TestTimeout));

            Assert.True(context.IsValidated);
            Assert.Equal(1, log.Count);
            Assert.Equal("Recent activity recording failed; token issuance continues.", log.SingleMessage);
        }

        [Fact]
        public void Recorder_event_callback_returns_while_writer_is_blocked_before_returning_a_task()
        {
            var fixture = new RecorderFixture();
            var writer = new RecordingRecentActivityWriter { BlockJoinBeforeTask = true };
            using var recorder = fixture.CreateRecorder(writer);
            recorder.Start();

            var callback = Task.Run(() => fixture.RaiseJoined("Amy"));
            Assert.True(writer.JoinEntered.Wait(TestTimeout));
            var returnedBeforeWriterRelease = callback.Wait(TimeSpan.FromSeconds(1));

            writer.JoinRelease.Set();
            Assert.True(callback.Wait(TestTimeout));

            Assert.True(returnedBeforeWriterRelease);
        }

        [Fact]
        public async Task Recorder_dispose_waits_for_an_accepted_write_to_finish()
        {
            var fixture = new RecorderFixture();
            var writer = new RecordingRecentActivityWriter { BlockJoinBeforeTask = true };
            var recorder = fixture.CreateRecorder(writer);
            recorder.Start();

            fixture.RaiseJoined("Amy");
            Assert.True(writer.JoinEntered.Wait(TestTimeout));
            var dispose = Task.Run(recorder.Dispose);
            var disposedBeforeWriterRelease = dispose.Wait(TimeSpan.FromMilliseconds(250));

            writer.JoinRelease.Set();
            await dispose;

            Assert.False(disposedBeforeWriterRelease);
            Assert.True(writer.JoinRecorded.IsSet);
            Assert.Equal(
                new[]
                {
                    "subscribe-joined", "subscribe-left", "dispose-left", "dispose-joined"
                },
                fixture.Trace);
        }

        [Fact]
        public void Recorder_copies_join_and_leave_names_and_utc_times()
        {
            var fixture = new RecorderFixture();
            var writer = new RecordingRecentActivityWriter();
            using var recorder = fixture.CreateRecorder(writer);

            recorder.Start();
            fixture.RaiseJoined("Amy");
            fixture.RaiseLeft("Amy");
            Assert.True(writer.JoinRecorded.Wait(TestTimeout));
            Assert.True(writer.LeftRecorded.Wait(TestTimeout));

            var joined = Assert.Single(writer.Joins);
            var left = Assert.Single(writer.Leaves);
            Assert.Equal("Amy", joined.DisplayName);
            Assert.Equal("Amy", left.DisplayName);
            Assert.Equal(TimeSpan.Zero, joined.OccurredAtUtc.Offset);
            Assert.Equal(TimeSpan.Zero, left.OccurredAtUtc.Offset);
        }

        [Theory]
        [InlineData(WriterFailure.SynchronousThrow)]
        [InlineData(WriterFailure.FaultedTask)]
        [InlineData(WriterFailure.CanceledTask)]
        public void Recorder_writer_failures_are_observed_with_one_fixed_warning(
            WriterFailure failure)
        {
            var fixture = new RecorderFixture();
            var writer = new RecordingRecentActivityWriter { JoinFailure = failure };
            var log = new RecordingLog();
            using var recorder = fixture.CreateRecorder(writer, log.Write);
            recorder.Start();

            fixture.RaiseJoined("Amy");
            Assert.True(log.Written.Wait(TestTimeout));

            Assert.Equal(1, log.Count);
            Assert.Equal("Recent activity recording failed; player activity continues.", log.SingleMessage);
        }

        [Fact]
        public void Recorder_start_and_dispose_are_idempotent_and_unsubscribe_in_reverse_order()
        {
            var fixture = new RecorderFixture();
            using var recorder = fixture.CreateRecorder(new RecordingRecentActivityWriter());

            recorder.Start();
            recorder.Start();
            recorder.Dispose();
            recorder.Dispose();

            Assert.Equal(
                new[]
                {
                    "subscribe-joined", "subscribe-left", "dispose-left", "dispose-joined"
                },
                fixture.Trace);
        }

        [Fact]
        public void Dispose_interleaved_with_subscription_creation_cleans_up_and_rejects_late_callbacks()
        {
            var fixture = new RecorderFixture();
            var writer = new RecordingRecentActivityWriter();
            using var recorder = fixture.CreateRecorder(writer);
            fixture.DuringJoinedSubscription = recorder.Dispose;

            recorder.Start();
            fixture.RaiseJoined("Late");

            Assert.Equal(new[] { "subscribe-joined", "dispose-joined" }, fixture.Trace);
            Assert.False(writer.JoinEntered.Wait(TimeSpan.FromMilliseconds(250)));
        }

        private static PanelOAuthAuthorizationServerProvider CreateProvider(
            IRecentActivityWriter writer,
            Action<string>? log = null)
        {
            var options = PanelAuthenticationOptions.FromBinding(
                enabled: true,
                username: "owner",
                password: "password",
                allowInsecureHttp: true);
            var identity = new PanelUserIdentity("owner-42", "Owner Display", "Owner");
            return new PanelOAuthAuthorizationServerProvider(
                options,
                new PanelCredentialVerifier(new TestCredentialStore(identity)),
                writer,
                log);
        }

        private static OAuthGrantResourceOwnerCredentialsContext CreateGrantContext()
        {
            return new OAuthGrantResourceOwnerCredentialsContext(
                new OwinContext(),
                new OAuthAuthorizationServerOptions(),
                "client",
                "owner",
                "password",
                new List<string>());
        }

        public enum WriterFailure
        {
            None,
            SynchronousThrow,
            FaultedTask,
            CanceledTask
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class TestCredentialStore : IPanelCredentialStore
        {
            private readonly PanelUserIdentity identity;

            public TestCredentialStore(PanelUserIdentity identity)
            {
                this.identity = identity;
            }

            public bool TryVerify(string username, string password, out PanelUserIdentity result)
            {
                result = identity;
                return true;
            }

            public bool TryGetActive(string subject, out PanelUserIdentity result)
            {
                result = identity;
                return true;
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecorderFixture
        {
            private Action<string>? joined;
            private Action<string>? left;

            public Action? DuringJoinedSubscription { get; set; }

            public List<string> Trace { get; } = new List<string>();

            public SevenDaysRecentActivityRecorder CreateRecorder(
                IRecentActivityWriter writer,
                Action<string>? log = null)
            {
                return new SevenDaysRecentActivityRecorder(
                    handler => Subscribe("joined", handler, value => joined = value),
                    handler => Subscribe("left", handler, value => left = value),
                    writer,
                    log ?? (_ => { }));
            }

            public void RaiseJoined(string displayName) => joined!(displayName);

            public void RaiseLeft(string displayName) => left!(displayName);

            private IDisposable Subscribe(
                string name,
                Action<string> handler,
                Action<Action<string>> capture)
            {
                Trace.Add("subscribe-" + name);
                capture(handler);
                if (name == "joined") DuringJoinedSubscription?.Invoke();
                return new Subscription(() => Trace.Add("dispose-" + name));
            }

            [Trait("Capability", "Operations")]

            [Trait("Boundary", "Application")]

            private sealed class Subscription : IDisposable
            {
                private Action? dispose;

                public Subscription(Action dispose)
                {
                    this.dispose = dispose;
                }

                public void Dispose()
                {
                    Interlocked.Exchange(ref dispose, null)?.Invoke();
                }
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingLog
        {
            private int count;
            private string? singleMessage;

            public ManualResetEventSlim Written { get; } = new ManualResetEventSlim();

            public int Count => Volatile.Read(ref count);

            public string? SingleMessage => singleMessage;

            public void Write(string message)
            {
                singleMessage = message;
                Interlocked.Increment(ref count);
                Written.Set();
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingRecentActivityWriter : IRecentActivityWriter
        {
            public List<(string Subject, string DisplayName, DateTimeOffset OccurredAtUtc)> PanelLogins { get; } = new();
            public List<(string DisplayName, DateTimeOffset OccurredAtUtc)> Joins { get; } = new();
            public List<(string DisplayName, DateTimeOffset OccurredAtUtc)> Leaves { get; } = new();
            public ManualResetEventSlim PanelLoginEntered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim PanelLoginRelease { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim PanelLoginRecorded { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim JoinEntered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim JoinRelease { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim JoinRecorded { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim LeftRecorded { get; } = new ManualResetEventSlim();
            public bool BlockPanelLoginBeforeTask { get; set; }
            public bool BlockJoinBeforeTask { get; set; }
            public WriterFailure PanelLoginFailure { get; set; }
            public WriterFailure JoinFailure { get; set; }

            public Task RecordPanelLoginSucceededAsync(
                string subject,
                string displayName,
                DateTimeOffset occurredAtUtc,
                CancellationToken cancellationToken)
            {
                PanelLoginEntered.Set();
                if (BlockPanelLoginBeforeTask) PanelLoginRelease.Wait();
                var failure = CreateFailure(PanelLoginFailure);
                if (failure != null) return failure;
                PanelLogins.Add((subject, displayName, occurredAtUtc));
                PanelLoginRecorded.Set();
                return Task.CompletedTask;
            }

            public Task RecordPlayerJoinedAsync(
                string displayName,
                DateTimeOffset occurredAtUtc,
                CancellationToken cancellationToken)
            {
                JoinEntered.Set();
                if (BlockJoinBeforeTask) JoinRelease.Wait();
                var failure = CreateFailure(JoinFailure);
                if (failure != null) return failure;
                Joins.Add((displayName, occurredAtUtc));
                JoinRecorded.Set();
                return Task.CompletedTask;
            }

            public Task RecordPlayerLeftAsync(
                string displayName,
                DateTimeOffset occurredAtUtc,
                CancellationToken cancellationToken)
            {
                Leaves.Add((displayName, occurredAtUtc));
                LeftRecorded.Set();
                return Task.CompletedTask;
            }

            public Task RecordRestartScriptStartedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordShutdownRequestedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordServerOperationFailedAsync(string actorSubject, string operationCode, string failureCode, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;

            private static Task? CreateFailure(WriterFailure failure)
            {
                switch (failure)
                {
                    case WriterFailure.None:
                        return null;
                    case WriterFailure.SynchronousThrow:
                        throw new InvalidOperationException("sensitive writer failure");
                    case WriterFailure.FaultedTask:
                        return Task.FromException(new InvalidOperationException("sensitive writer failure"));
                    case WriterFailure.CanceledTask:
                        return Task.FromCanceled(new CancellationToken(canceled: true));
                    default:
                        throw new ArgumentOutOfRangeException(nameof(failure));
                }
            }
        }
    }
}
