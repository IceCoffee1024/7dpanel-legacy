using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ServerOperations;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ServerOperationsTests
    {
        [Fact]
        public async Task Restart_requires_confirmation_before_audit_or_launch()
        {
            var fixture = new RestartFixture();

            var exception = await Assert.ThrowsAsync<ServerOperationConfirmationRequiredException>(() =>
                fixture.UseCase.ExecuteAsync("owner", false, CancellationToken.None));

            Assert.Equal("confirmation_required", exception.FailureCode);
            Assert.Empty(fixture.Audit.Pending);
            Assert.Equal(0, fixture.Launcher.CallCount);
        }

        [Fact]
        public void Restart_launcher_rejects_missing_configuration_without_starting_a_process()
        {
            var starts = 0;
            var launcher = new RestartScriptLauncher(
                null,
                () => RestartScriptPlatform.Windows,
                _ => true,
                _ => { starts++; return new Process(); },
                () => DateTimeOffset.UtcNow);

            var exception = Assert.Throws<ServerOperationFailedException>(() =>
                launcher.StartConfiguredScript());

            Assert.Equal("restart_script_not_configured", exception.FailureCode);
            Assert.Equal(0, starts);
        }

        [Fact]
        public void Restart_launcher_rejects_a_missing_script_without_starting_a_process()
        {
            var starts = 0;
            var launcher = new RestartScriptLauncher(
                Options(),
                () => RestartScriptPlatform.Windows,
                _ => false,
                _ => { starts++; return new Process(); },
                () => DateTimeOffset.UtcNow);

            var exception = Assert.Throws<ServerOperationFailedException>(() =>
                launcher.StartConfiguredScript());

            Assert.Equal("restart_script_missing", exception.FailureCode);
            Assert.Equal(0, starts);
        }

        [Fact]
        public void Restart_launcher_uses_fixed_windows_command_interpreter_arguments()
        {
            ProcessStartInfo? actual = null;
            var expected = new DateTimeOffset(2026, 7, 25, 1, 2, 3, TimeSpan.Zero);
            var options = Options();
            var launcher = new RestartScriptLauncher(
                options,
                () => RestartScriptPlatform.Windows,
                _ => true,
                info => { actual = info; return new Process(); },
                () => expected);

            var startedAtUtc = launcher.StartConfiguredScript();

            Assert.Equal(expected, startedAtUtc);
            Assert.NotNull(actual);
            Assert.Equal("cmd.exe", actual!.FileName);
            Assert.Equal("/d /s /c \"\"" + options.WindowsScript + "\"\"", actual.Arguments);
            Assert.Equal(options.WorkingDirectory, actual.WorkingDirectory);
            Assert.False(actual.UseShellExecute);
            Assert.False(actual.RedirectStandardInput);
            Assert.False(actual.RedirectStandardOutput);
            Assert.False(actual.RedirectStandardError);
        }

        [Fact]
        public void Restart_launcher_uses_fixed_linux_shell_arguments()
        {
            ProcessStartInfo? actual = null;
            var options = RestartScriptOptions.FromBinding(
                "scripts/restart.cmd",
                "scripts/restart server.sh",
                ".",
                System.IO.Path.GetTempPath());
            var launcher = new RestartScriptLauncher(
                options,
                () => RestartScriptPlatform.Linux,
                _ => true,
                info => { actual = info; return new Process(); },
                () => DateTimeOffset.UtcNow);

            launcher.StartConfiguredScript();

            Assert.NotNull(actual);
            Assert.Equal("/bin/sh", actual!.FileName);
            Assert.Equal("'" + options.LinuxScript + "'", actual.Arguments);
        }

        [Fact]
        public void Restart_launcher_returns_as_soon_as_process_start_returns()
        {
            var launcher = new RestartScriptLauncher(
                Options(),
                () => RestartScriptPlatform.Linux,
                _ => true,
                _ => new Process(),
                () => DateTimeOffset.UtcNow);

            var result = launcher.StartConfiguredScript();

            Assert.NotEqual(default, result);
        }

        [Fact]
        public void Restart_launcher_disposes_process_handle_immediately_after_start()
        {
            var processHandle = new TrackingProcessHandle();
            var launcher = new RestartScriptLauncher(
                Options(),
                () => RestartScriptPlatform.Linux,
                _ => true,
                _ => processHandle,
                () => DateTimeOffset.UtcNow);

            launcher.StartConfiguredScript();

            Assert.True(processHandle.IsDisposed);
        }

        [Fact]
        public async Task Restart_is_single_flight_while_process_start_is_in_progress()
        {
            using var release = new ManualResetEventSlim(false);
            using var entered = new ManualResetEventSlim(false);
            var fixture = new RestartFixture(
                new BlockingLauncher(entered, release));
            var first = Task.Factory.StartNew(
                    () => fixture.UseCase.ExecuteAsync("owner", true, CancellationToken.None),
                    TestContext.Current.CancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            var exception = await Assert.ThrowsAsync<ServerOperationBusyException>(() =>
                fixture.UseCase.ExecuteAsync("owner", true, CancellationToken.None));

            Assert.Equal("operation_in_progress", exception.FailureCode);
            release.Set();
            await first;
        }

        [Fact]
        public async Task Restart_start_failure_is_safe_and_is_audited_without_leaking_exception_text()
        {
            var fixture = new RestartFixture(new ThrowingLauncher());

            var exception = await Assert.ThrowsAsync<ServerOperationFailedException>(() =>
                fixture.UseCase.ExecuteAsync("owner", true, CancellationToken.None));

            Assert.Equal("restart_script_start_failed", exception.FailureCode);
            Assert.DoesNotContain("machine-specific detail", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("restart_script_start_failed", Assert.Single(fixture.Audit.Failed).FailureCode);
        }

        [Theory]
        [InlineData("restart_script_not_configured")]
        [InlineData("restart_script_missing")]
        [InlineData("restart_script_platform_unsupported")]
        [InlineData("restart_script_start_failed")]
        public async Task Restart_failure_activity_uses_the_fixed_contract(
            string failureCode)
        {
            var activity = new SignalingRecentActivityWriter();
            var fixture = new RestartFixture(
                new FixedFailureLauncher(failureCode),
                activity);

            await Assert.ThrowsAsync<ServerOperationFailedException>(() =>
                fixture.UseCase.ExecuteAsync("owner", true, CancellationToken.None));
            var recorded = await activity.FailureRecorded;

            Assert.Equal("owner", recorded.ActorSubject);
            Assert.Equal("restart_script", recorded.OperationCode);
            Assert.Equal(failureCode, recorded.FailureCode);
        }

        [Fact]
        public async Task Pending_audit_failure_prevents_restart_side_effect()
        {
            var fixture = new RestartFixture();
            fixture.Audit.CreateException = new InvalidOperationException("database unavailable");

            var exception = await Assert.ThrowsAsync<ServerOperationAuditUnavailableException>(() =>
                fixture.UseCase.ExecuteAsync("owner", true, CancellationToken.None));

            Assert.Equal("audit_unavailable", exception.FailureCode);
            Assert.Equal(0, fixture.Launcher.CallCount);
        }

        [Fact]
        public async Task Restart_started_audit_failure_reports_accepted_operation_with_degraded_audit()
        {
            var fixture = new RestartFixture();
            fixture.Audit.StartedException = new InvalidOperationException("database unavailable");

            var result = await fixture.UseCase.ExecuteAsync("owner", true, CancellationToken.None);

            Assert.Equal("restart_script_started", result.Status);
            Assert.Equal("audit_degraded", result.AuditStatus);
            Assert.Equal(1, fixture.Launcher.CallCount);
        }

        [Fact]
        public async Task Restart_activity_failure_does_not_change_the_accepted_result_or_block_it()
        {
            var fixture = new RestartFixture(recentActivity: new FailingRecentActivityWriter());

            var result = await fixture.UseCase.ExecuteAsync("owner", true, CancellationToken.None);

            Assert.Equal("restart_script_started", result.Status);
        }

        [Fact]
        public async Task Shutdown_uses_fixed_gateway_without_accepting_command_text()
        {
            var audit = new RecordingAuditTrail();
            string? command = null;
            var gateway = new SevenDaysShutdownServerGateway(
                (name, action, timeout, token) =>
                {
                    Assert.Equal("7DPanel.ServerOperations.Shutdown", name);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    action();
                    return Task.CompletedTask;
                },
                value => command = value);
            var useCase = new ShutdownServerUseCase(
                gateway,
                audit,
                new NoopRecentActivityWriter(),
                () => "shutdown-1",
                () => DateTimeOffset.UtcNow);

            var result = await useCase.ExecuteAsync("owner", true, CancellationToken.None);

            Assert.Equal("shutdown_requested", result.Status);
            Assert.Equal("shutdown", command);
            Assert.Equal("shutdown", Assert.Single(audit.Pending).OperationCode);
            Assert.Single(typeof(IShutdownServerGateway).GetMethods());
            var parameter = Assert.Single(typeof(IShutdownServerGateway).GetMethods()[0].GetParameters());
            Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
        }

        [Fact]
        public async Task Shutdown_requires_its_own_confirmation_and_does_not_report_restart_status()
        {
            var gateway = new RecordingShutdownGateway();
            var useCase = new ShutdownServerUseCase(
                gateway,
                new RecordingAuditTrail(),
                new NoopRecentActivityWriter(),
                () => "shutdown-1",
                () => DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<ServerOperationConfirmationRequiredException>(() =>
                useCase.ExecuteAsync("owner", false, CancellationToken.None));

            Assert.Equal(0, gateway.CallCount);
        }

        [Theory]
        [InlineData("shutdown_unavailable")]
        [InlineData("shutdown_timeout")]
        [InlineData("shutdown_cancelled")]
        [InlineData("shutdown_failed")]
        public async Task Shutdown_failure_activity_uses_the_fixed_contract(
            string failureCode)
        {
            var activity = new SignalingRecentActivityWriter();
            var useCase = new ShutdownServerUseCase(
                new FixedFailureShutdownGateway(failureCode),
                new RecordingAuditTrail(),
                activity,
                () => "shutdown-1",
                () => DateTimeOffset.UtcNow);

            await Assert.ThrowsAsync<ServerOperationFailedException>(() =>
                useCase.ExecuteAsync("owner", true, CancellationToken.None));
            var recorded = await activity.FailureRecorded;

            Assert.Equal("owner", recorded.ActorSubject);
            Assert.Equal("shutdown", recorded.OperationCode);
            Assert.Equal(failureCode, recorded.FailureCode);
        }

        [Fact]
        public async Task Shutdown_is_single_flight_while_gateway_is_in_progress()
        {
            using var entered = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);
            var useCase = new ShutdownServerUseCase(
                new BlockingShutdownGateway(entered, release),
                new RecordingAuditTrail(),
                new NoopRecentActivityWriter(),
                () => Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow);
            var first = Task.Factory.StartNew(
                    () => useCase.ExecuteAsync("owner", true, CancellationToken.None),
                    TestContext.Current.CancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();
            Assert.True(entered.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));

            var exception = await Assert.ThrowsAsync<ServerOperationBusyException>(() =>
                useCase.ExecuteAsync("owner", true, CancellationToken.None));

            Assert.Equal("operation_in_progress", exception.FailureCode);
            release.Set();
            await first;
        }

        [Fact]
        public async Task Shutdown_cancellation_records_failure_and_releases_single_flight()
        {
            var gateway = new CancelOnceShutdownGateway();
            var activity = new SignalingRecentActivityWriter();
            var useCase = new ShutdownServerUseCase(
                gateway,
                new RecordingAuditTrail(),
                activity,
                () => Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow);

            var exception = await Assert.ThrowsAsync<ServerOperationFailedException>(() =>
                useCase.ExecuteAsync("owner", true, CancellationToken.None));
            var failure = await activity.FailureRecorded;
            var retry = await useCase.ExecuteAsync(
                "owner",
                true,
                CancellationToken.None);

            Assert.Equal("shutdown_cancelled", exception.FailureCode);
            Assert.Equal("shutdown_cancelled", failure.FailureCode);
            Assert.Equal("shutdown_requested", retry.Status);
            Assert.Equal(2, gateway.CallCount);
        }

        private static RestartScriptOptions Options()
        {
            return RestartScriptOptions.FromBinding(
                "scripts/restart.cmd",
                "scripts/restart.sh",
                ".",
                System.IO.Path.GetTempPath());
        }

        private sealed class RestartFixture
        {
            public RestartFixture(
                IRestartScriptLauncher? launcher = null,
                IRecentActivityWriter? recentActivity = null)
            {
                Launcher = launcher as RecordingLauncher ?? new RecordingLauncher();
                Audit = new RecordingAuditTrail();
                UseCase = new RestartServerUseCase(
                    launcher ?? Launcher,
                    Audit,
                    recentActivity ?? new NoopRecentActivityWriter(),
                    () => "restart-1",
                    () => DateTimeOffset.UtcNow);
            }

            public RecordingLauncher Launcher { get; }
            public RecordingAuditTrail Audit { get; }
            public RestartServerUseCase UseCase { get; }
        }

        private class RecordingLauncher : IRestartScriptLauncher
        {
            public int CallCount { get; private set; }

            public virtual DateTimeOffset StartConfiguredScript()
            {
                CallCount++;
                return DateTimeOffset.UtcNow;
            }
        }

        private sealed class BlockingLauncher : RecordingLauncher
        {
            private readonly ManualResetEventSlim entered;
            private readonly ManualResetEventSlim release;

            public BlockingLauncher(ManualResetEventSlim entered, ManualResetEventSlim release)
            {
                this.entered = entered;
                this.release = release;
            }

            public override DateTimeOffset StartConfiguredScript()
            {
                entered.Set();
                release.Wait(TestContext.Current.CancellationToken);
                return DateTimeOffset.UtcNow;
            }
        }

        private sealed class ThrowingLauncher : IRestartScriptLauncher
        {
            public DateTimeOffset StartConfiguredScript() =>
                throw new InvalidOperationException("machine-specific detail");
        }

        private sealed class FixedFailureLauncher : IRestartScriptLauncher
        {
            private readonly string failureCode;

            public FixedFailureLauncher(string failureCode)
            {
                this.failureCode = failureCode;
            }

            public DateTimeOffset StartConfiguredScript()
            {
                throw new ServerOperationFailedException(failureCode);
            }
        }

        private sealed class TrackingProcessHandle : IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private sealed class RecordingShutdownGateway : IShutdownServerGateway
        {
            public int CallCount { get; private set; }

            public Task RequestShutdownAsync(CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.CompletedTask;
            }
        }

        private sealed class FixedFailureShutdownGateway : IShutdownServerGateway
        {
            private readonly string failureCode;

            public FixedFailureShutdownGateway(string failureCode)
            {
                this.failureCode = failureCode;
            }

            public Task RequestShutdownAsync(CancellationToken cancellationToken)
            {
                if (failureCode == "shutdown_timeout")
                    return Task.FromException(new TimeoutException());
                if (failureCode == "shutdown_cancelled")
                    return Task.FromException(new OperationCanceledException());
                return Task.FromException(
                    new ServerOperationFailedException(failureCode));
            }
        }

        private sealed class BlockingShutdownGateway : IShutdownServerGateway
        {
            private readonly ManualResetEventSlim entered;
            private readonly ManualResetEventSlim release;

            public BlockingShutdownGateway(
                ManualResetEventSlim entered,
                ManualResetEventSlim release)
            {
                this.entered = entered;
                this.release = release;
            }

            public Task RequestShutdownAsync(CancellationToken cancellationToken)
            {
                entered.Set();
                release.Wait(TestContext.Current.CancellationToken);
                return Task.CompletedTask;
            }
        }

        private sealed class CancelOnceShutdownGateway : IShutdownServerGateway
        {
            public int CallCount { get; private set; }

            public Task RequestShutdownAsync(CancellationToken cancellationToken)
            {
                CallCount++;
                return CallCount == 1
                    ? Task.FromException(new OperationCanceledException())
                    : Task.CompletedTask;
            }
        }

        private sealed class RecordingAuditTrail : IServerOperationAuditTrail
        {
            public List<ServerOperationAuditIntent> Pending { get; } = new List<ServerOperationAuditIntent>();
            public List<ServerOperationAuditFailure> Failed { get; } = new List<ServerOperationAuditFailure>();
            public Exception? CreateException { get; set; }
            public Exception? StartedException { get; set; }

            public void CreatePending(ServerOperationAuditIntent intent)
            {
                if (CreateException != null) throw CreateException;
                Pending.Add(intent);
            }

            public bool TryMarkStarted(string operationId, DateTimeOffset updatedAtUtc)
            {
                if (StartedException != null) throw StartedException;
                return true;
            }

            public bool TryMarkFailed(ServerOperationAuditFailure failure)
            {
                Failed.Add(failure);
                return true;
            }
        }

        private class NoopRecentActivityWriter : IRecentActivityWriter
        {
            public Task RecordPanelLoginSucceededAsync(string actorSubject, string actorDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerJoinedAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerLeftAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public virtual Task RecordRestartScriptStartedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordShutdownRequestedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public virtual Task RecordServerOperationFailedAsync(string actorSubject, string operationCode, string failureCode, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class FailingRecentActivityWriter : NoopRecentActivityWriter
        {
            public override Task RecordRestartScriptStartedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) =>
                Task.FromException(new InvalidOperationException("activity unavailable"));
        }

        private sealed class SignalingRecentActivityWriter : NoopRecentActivityWriter
        {
            private readonly TaskCompletionSource<FailureActivity> failureRecorded =
                new TaskCompletionSource<FailureActivity>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<FailureActivity> FailureRecorded => failureRecorded.Task;

            public override Task RecordServerOperationFailedAsync(
                string actorSubject,
                string operationCode,
                string failureCode,
                DateTimeOffset occurredAtUtc,
                CancellationToken cancellationToken)
            {
                failureRecorded.TrySetResult(new FailureActivity(
                    actorSubject,
                    operationCode,
                    failureCode));
                return Task.CompletedTask;
            }
        }

        private sealed class FailureActivity
        {
            public FailureActivity(
                string actorSubject,
                string operationCode,
                string failureCode)
            {
                ActorSubject = actorSubject;
                OperationCode = operationCode;
                FailureCode = failureCode;
            }

            public string ActorSubject { get; }
            public string OperationCode { get; }
            public string FailureCode { get; }
        }
    }
}
