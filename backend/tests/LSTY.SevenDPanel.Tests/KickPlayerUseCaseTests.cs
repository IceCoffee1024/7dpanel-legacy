using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Application")]
    public sealed class KickPlayerUseCaseTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Empty_actor_is_rejected_before_audit_and_action(string actorSubject)
        {
            var fixture = new KickFixture();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                fixture.UseCase.ExecuteAsync(
                    fixture.Request(actorSubject: actorSubject),
                    CancellationToken.None));

            Assert.Empty(fixture.Audit.Intents);
            Assert.Equal(0, fixture.Actions.CallCount);
        }

        [Fact]
        public async Task Negative_entity_id_is_rejected_before_audit_and_action()
        {
            var fixture = new KickFixture();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                fixture.UseCase.ExecuteAsync(
                    fixture.Request(entityId: -1),
                    CancellationToken.None));

            Assert.Empty(fixture.Audit.Intents);
            Assert.Equal(0, fixture.Actions.CallCount);
        }

        [Fact]
        public async Task Missing_identity_is_rejected_before_audit_and_action()
        {
            var fixture = new KickFixture();

            await Assert.ThrowsAsync<InvalidPlayerIdentityException>(() =>
                fixture.UseCase.ExecuteAsync(
                    new KickPlayerRequest(
                        "owner",
                        7,
                        null!,
                        "违反服务器规则",
                        true),
                    CancellationToken.None));

            Assert.Empty(fixture.Audit.Intents);
            Assert.Equal(0, fixture.Actions.CallCount);
        }

        [Fact]
        public async Task Missing_confirmation_is_rejected_before_audit_and_action()
        {
            var fixture = new KickFixture();

            await Assert.ThrowsAsync<PlayerKickConfirmationRequiredException>(() =>
                fixture.UseCase.ExecuteAsync(
                    fixture.Request(confirmed: false),
                    CancellationToken.None));

            Assert.Empty(fixture.Audit.Intents);
            Assert.Equal(0, fixture.Actions.CallCount);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Empty_reason_is_rejected_before_audit_and_action(string reason)
        {
            var fixture = new KickFixture();

            await Assert.ThrowsAsync<InvalidPlayerKickReasonException>(() =>
                fixture.UseCase.ExecuteAsync(
                    fixture.Request(reason: reason),
                    CancellationToken.None));

            Assert.Empty(fixture.Audit.Intents);
            Assert.Equal(0, fixture.Actions.CallCount);
        }

        [Fact]
        public async Task Reason_longer_than_200_characters_is_rejected_before_audit_and_action()
        {
            var fixture = new KickFixture();

            await Assert.ThrowsAsync<InvalidPlayerKickReasonException>(() =>
                fixture.UseCase.ExecuteAsync(
                    fixture.Request(reason: new string('x', 201)),
                    CancellationToken.None));

            Assert.Empty(fixture.Audit.Intents);
            Assert.Equal(0, fixture.Actions.CallCount);
        }

        [Fact]
        public async Task Valid_reason_is_trimmed_before_audit_and_action()
        {
            var fixture = new KickFixture();

            await fixture.UseCase.ExecuteAsync(
                fixture.Request(reason: "  违反服务器规则  "),
                CancellationToken.None);

            Assert.Equal("违反服务器规则", Assert.Single(fixture.Audit.Intents).Reason);
            Assert.Equal("违反服务器规则", Assert.Single(fixture.Actions.Commands).Reason);
        }

        [Fact]
        public async Task Unicode_reason_is_preserved()
        {
            var fixture = new KickFixture();

            await fixture.UseCase.ExecuteAsync(
                fixture.Request(reason: "违反服务器规则"),
                CancellationToken.None);

            Assert.Equal("违反服务器规则", Assert.Single(fixture.Audit.Intents).Reason);
            Assert.Equal("违反服务器规则", Assert.Single(fixture.Actions.Commands).Reason);
        }

        [Fact]
        public async Task Pending_audit_is_persisted_before_the_game_action()
        {
            var order = new List<string>();
            var fixture = new KickFixture(order);

            var result = await fixture.UseCase.ExecuteAsync(
                fixture.Request(),
                CancellationToken.None);

            Assert.Equal(new[] { "audit:pending", "action:kick", "audit:Succeeded" }, order);
            Assert.Equal("succeeded", result.Status);
            Assert.Equal("operation-id", result.OperationId);
            Assert.Equal(fixture.RequestedAtUtc, result.RequestedAtUtc);
            Assert.Equal(fixture.CompletedAtUtc, result.CompletedAtUtc);
        }

        [Fact]
        public async Task Concurrent_second_request_is_busy_without_creating_an_audit()
        {
            var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<KickPlayerActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var fixture = new KickFixture();
            fixture.Actions.Handler = command =>
            {
                started.TrySetResult(null);
                return release.Task;
            };

            var first = fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None);
            await started.Task;

            await Assert.ThrowsAsync<PlayerActionBusyException>(() =>
                fixture.UseCase.ExecuteAsync(fixture.Request(entityId: 8), CancellationToken.None));
            Assert.Single(fixture.Audit.Intents);

            release.SetResult(fixture.Actions.SuccessResult(fixture.Actions.Commands[0]));
            await first;
        }

        [Theory]
        [InlineData(KickPlayerActionStatus.PlayerNotOnline, "player_not_online", null)]
        [InlineData(KickPlayerActionStatus.PlayerIdentityChanged, "player_identity_changed", "Replacement Player")]
        public async Task Typed_action_failure_is_a_failed_audit_and_stable_exception(
            KickPlayerActionStatus status,
            string failureCode,
            string? targetName)
        {
            var fixture = new KickFixture();
            fixture.Actions.Handler = command => Task.FromResult(
                status == KickPlayerActionStatus.PlayerNotOnline
                    ? KickPlayerActionResult.PlayerNotOnline()
                    : KickPlayerActionResult.PlayerIdentityChanged(
                        command.EntityId,
                        targetName!,
                        command.ExpectedPlatformIdentity));

            if (status == KickPlayerActionStatus.PlayerNotOnline)
            {
                await Assert.ThrowsAsync<PlayerNotOnlineException>(() =>
                    fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None));
            }
            else
            {
                await Assert.ThrowsAsync<PlayerIdentityChangedException>(() =>
                    fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None));
            }

            var completion = Assert.Single(fixture.Audit.Completions);
            Assert.Equal(PlayerActionAuditStatus.Failed, completion.Status);
            Assert.Equal(failureCode, completion.FailureCode);
            Assert.Equal(targetName, completion.TargetName);
        }

        [Fact]
        public async Task Timeout_is_failed_and_gate_is_released()
        {
            var fixture = new KickFixture();
            fixture.Actions.Handler = _ => Task.FromException<KickPlayerActionResult>(new TimeoutException());

            await Assert.ThrowsAsync<TimeoutException>(() =>
                fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None));
            Assert.Equal("game_thread_timeout", Assert.Single(fixture.Audit.Completions).FailureCode);

            fixture.Actions.Handler = command => Task.FromResult(fixture.Actions.SuccessResult(command));
            var result = await fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None);
            Assert.Equal("succeeded", result.Status);
        }

        [Fact]
        public async Task Cancellation_is_failed_and_gate_is_released()
        {
            var fixture = new KickFixture();
            fixture.Actions.Handler = _ => Task.FromCanceled<KickPlayerActionResult>(new CancellationToken(true));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None));
            Assert.Equal("player_kick_cancelled", Assert.Single(fixture.Audit.Completions).FailureCode);

            fixture.Actions.Handler = command => Task.FromResult(fixture.Actions.SuccessResult(command));
            var result = await fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None);
            Assert.Equal("succeeded", result.Status);
        }

        [Fact]
        public async Task Unknown_action_exception_is_failed_and_wrapped()
        {
            var fixture = new KickFixture();
            fixture.Actions.Handler = _ => Task.FromException<KickPlayerActionResult>(
                new InvalidOperationException("internal detail"));

            var exception = await Assert.ThrowsAsync<PlayerKickFailedException>(() =>
                fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal("player_kick_failed", Assert.Single(fixture.Audit.Completions).FailureCode);
        }

        [Fact]
        public async Task Pending_audit_failure_prevents_the_game_action()
        {
            var fixture = new KickFixture();
            fixture.Audit.CreateException = new InvalidOperationException("database unavailable");

            var exception = await Assert.ThrowsAsync<AuditUnavailableException>(() =>
                fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal(0, fixture.Actions.CallCount);
            Assert.Empty(fixture.Audit.Completions);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Completion_failure_is_unknown_to_the_caller_without_a_second_terminal_write(
            bool throwOnComplete)
        {
            var fixture = new KickFixture();
            fixture.Audit.CompleteResult = false;
            if (throwOnComplete)
                fixture.Audit.CompleteException = new InvalidOperationException("database unavailable");

            await Assert.ThrowsAsync<AuditCompletionUnavailableException>(() =>
                fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None));

            Assert.Single(fixture.Audit.Completions);
        }

        [Theory]
        [InlineData("pending")]
        [InlineData("action")]
        [InlineData("completion")]
        public async Task Gate_is_released_after_each_infrastructure_failure(string failurePoint)
        {
            var fixture = new KickFixture();
            if (failurePoint == "pending")
                fixture.Audit.CreateException = new InvalidOperationException("pending failed");
            else if (failurePoint == "action")
                fixture.Actions.Handler = _ => Task.FromException<KickPlayerActionResult>(
                    new InvalidOperationException("action failed"));
            else
                fixture.Audit.CompleteResult = false;

            await Assert.ThrowsAnyAsync<Exception>(() =>
                fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None));

            fixture.Audit.CreateException = null;
            fixture.Audit.CompleteResult = true;
            fixture.Actions.Handler = command => Task.FromResult(fixture.Actions.SuccessResult(command));
            var result = await fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None);

            Assert.Equal("succeeded", result.Status);
        }

        [Theory]
        [InlineData(typeof(KickPlayerCommand))]
        [InlineData(typeof(PlayerActionTarget))]
        [InlineData(typeof(PlayerActionAuditCompletion))]
        public void Application_result_models_do_not_expose_unrestricted_public_constructors(Type modelType)
        {
            Assert.DoesNotContain(modelType.GetConstructors(), constructor => constructor.IsPublic);
        }

        [Fact]
        public async Task Cancellation_token_is_forwarded_to_the_player_actions_port()
        {
            var fixture = new KickFixture();
            using var cancellation = new CancellationTokenSource();

            await fixture.UseCase.ExecuteAsync(fixture.Request(), cancellation.Token);

            Assert.Equal(cancellation.Token, fixture.Actions.CancellationToken);
        }

        [Theory]
        [InlineData(KickPlayerActionStatus.PlayerNotOnline)]
        [InlineData(KickPlayerActionStatus.PlayerIdentityChanged)]
        public async Task Failed_action_completion_failure_attempts_only_one_terminal_write(
            KickPlayerActionStatus status)
        {
            var fixture = new KickFixture();
            fixture.Audit.CompleteResult = false;
            fixture.Actions.Handler = command => Task.FromResult(
                status == KickPlayerActionStatus.PlayerNotOnline
                    ? KickPlayerActionResult.PlayerNotOnline()
                    : KickPlayerActionResult.PlayerIdentityChanged(
                        command.EntityId,
                        "Replacement Player",
                        command.ExpectedPlatformIdentity));

            await Assert.ThrowsAsync<AuditCompletionUnavailableException>(() =>
                fixture.UseCase.ExecuteAsync(fixture.Request(), CancellationToken.None));

            Assert.Single(fixture.Audit.Completions);
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class KickFixture
        {
            public KickFixture(List<string>? order = null)
            {
                RequestedAtUtc = new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);
                CompletedAtUtc = RequestedAtUtc.AddMilliseconds(100);
                var clockCalls = 0;
                Actions = new RecordingPlayerActions(order);
                Audit = new RecordingAuditTrail(order);
                UseCase = new KickPlayerUseCase(
                    Actions,
                    Audit,
                    () => "operation-id",
                    () => Interlocked.Increment(ref clockCalls) % 2 == 1
                        ? RequestedAtUtc
                        : CompletedAtUtc);
            }

            public DateTimeOffset RequestedAtUtc { get; }

            public DateTimeOffset CompletedAtUtc { get; }

            public RecordingPlayerActions Actions { get; }

            public RecordingAuditTrail Audit { get; }

            public KickPlayerUseCase UseCase { get; }

            public KickPlayerRequest Request(
                string actorSubject = "owner",
                int entityId = 7,
                PlayerPlatformIdentity? expectedPlatformIdentity = null,
                string reason = "违反服务器规则",
                bool confirmed = true)
            {
                return new KickPlayerRequest(
                    actorSubject,
                    entityId,
                    expectedPlatformIdentity ?? new PlayerPlatformIdentity("Steam_123", "Steam"),
                    reason,
                    confirmed);
            }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingPlayerActions : IPlayerActions
        {
            private readonly List<string>? order;

            public RecordingPlayerActions(List<string>? order)
            {
                this.order = order;
                Handler = command => Task.FromResult(SuccessResult(command));
            }

            public List<KickPlayerCommand> Commands { get; } = new List<KickPlayerCommand>();

            public Func<KickPlayerCommand, Task<KickPlayerActionResult>> Handler { get; set; }

            public CancellationToken CancellationToken { get; private set; }

            public int CallCount => Commands.Count;

            public Task<KickPlayerActionResult> KickAsync(
                KickPlayerCommand command,
                CancellationToken cancellationToken)
            {
                Commands.Add(command);
                CancellationToken = cancellationToken;
                order?.Add("action:kick");
                return Handler(command);
            }

            public KickPlayerActionResult SuccessResult(KickPlayerCommand command)
            {
                return KickPlayerActionResult.Succeeded(
                    command.EntityId,
                    "Test Player",
                    command.ExpectedPlatformIdentity);
            }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingAuditTrail : IPlayerActionAuditTrail
        {
            private readonly List<string>? order;

            public RecordingAuditTrail(List<string>? order)
            {
                this.order = order;
            }

            public List<PlayerActionAuditIntent> Intents { get; } = new List<PlayerActionAuditIntent>();

            public List<PlayerActionAuditCompletion> Completions { get; } = new List<PlayerActionAuditCompletion>();

            public Exception? CreateException { get; set; }

            public Exception? CompleteException { get; set; }

            public bool CompleteResult { get; set; } = true;

            public void CreatePending(PlayerActionAuditIntent intent)
            {
                if (CreateException != null)
                    throw CreateException;

                Intents.Add(intent);
                order?.Add("audit:pending");
            }

            public bool TryComplete(PlayerActionAuditCompletion completion)
            {
                Completions.Add(completion);
                order?.Add($"audit:{completion.Status}");
                if (CompleteException != null)
                    throw CompleteException;

                return CompleteResult;
            }

            public int MarkPendingUnknown(DateTimeOffset completedAtUtc)
            {
                return 0;
            }
        }
    }
}