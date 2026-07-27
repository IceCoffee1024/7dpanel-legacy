using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Community;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Domain.Community;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class CommunityVoteRuntimeTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 10, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Due_open_round_is_settled_by_runtime()
        {
            using var database = new TemporaryDatabase();
            var round = database.StartPassedRound("due-round", Now.AddMinutes(-1));
            using var runtime = CreateRuntime(database.Store, new RecordingVoteActionPort(), Now);

            runtime.Start();
            runtime.RunOnce();

            Assert.Equal(VoteRoundState.Passed, database.Store.GetRound(round.RoundId).State);
        }

        [Fact]
        public void Passed_round_action_is_dispatched_only_once()
        {
            using var database = new TemporaryDatabase();
            var actions = new RecordingVoteActionPort();
            database.StartPassedRound("dispatch-once", Now.AddMinutes(-1));
            using var runtime = CreateRuntime(database.Store, actions, Now);

            runtime.Start();
            runtime.RunOnce();
            runtime.RunOnce();

            Assert.Equal(1, actions.CallCount);
            Assert.Equal(VoteRoundState.ActionSucceeded, database.Store.GetRound("dispatch-once").State);
        }

        [Fact]
        public void Start_marks_interrupted_queued_action_result_unknown_before_processing_rounds()
        {
            using var database = new TemporaryDatabase();
            var started = database.StartPassedRound("interrupted", Now.AddMinutes(-1));
            var settled = new SettleVoteUseCase(database.Store).Execute(started.RoundId, Now);
            Assert.True(database.Store.TryQueueAction(started.RoundId, settled.Round.RowVersion, Now));
            using var runtime = CreateRuntime(database.Store, new RecordingVoteActionPort(), Now);

            runtime.Start();

            Assert.Equal(
                VoteRoundState.ActionResultUnknown,
                database.Store.GetRound(started.RoundId).State);
        }

        [Fact]
        public void Stopped_runtime_does_not_process_later_due_rounds()
        {
            using var database = new TemporaryDatabase();
            var round = database.StartPassedRound("stopped", Now.AddMinutes(-1));
            using var runtime = CreateRuntime(database.Store, new RecordingVoteActionPort(), Now);

            runtime.Start();
            runtime.Stop();
            runtime.RunOnce();

            Assert.Equal(VoteRoundState.Open, database.Store.GetRound(round.RoundId).State);
        }

        private static CommunityVoteRuntime CreateRuntime(
            SqliteVoteStore store,
            RecordingVoteActionPort actions,
            DateTimeOffset now) =>
            new CommunityVoteRuntime(
                store,
                new SettleVoteUseCase(store),
                new DispatchVoteActionUseCase(store, actions),
                new RecoverQueuedVoteActionsUseCase(store),
                () => now,
                TimeSpan.FromHours(1));

        private sealed class RecordingVoteActionPort : ICommunityVoteActionPort
        {
            public int CallCount { get; private set; }

            public Task<VoteActionResult> ExecuteAsync(
                VoteActionCommand command,
                CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(VoteActionResult.Succeeded("operation-" + CallCount, null));
            }
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-community-vote-runtime-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase()
            {
                var connectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(connectionFactory).Upgrade();
                Store = new SqliteVoteStore(connectionFactory);
                ConnectionFactory = connectionFactory;
                Store.SaveConfiguration(new VoteConfiguration(
                    "restart", VoteKind.Restart, true, TimeSpan.FromMinutes(1), 60, 2,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                    "global", false,
                    Now, 0));
            }

            public SqliteConnectionFactory ConnectionFactory { get; }
            public SqliteVoteStore Store { get; }

            public VoteRoundSnapshot StartPassedRound(string roundId, DateTimeOffset openedAtUtc)
            {
                var result = new StartVoteUseCase(Store).Execute(new StartVoteRequest(
                    roundId,
                    VoteKind.Restart,
                    "EOS-A",
                    null,
                    new[]
                    {
                        new VoteEligiblePlayer("EOS-A", TimeSpan.FromMinutes(10)),
                        new VoteEligiblePlayer("EOS-B", TimeSpan.FromMinutes(10))
                    },
                    "idempotency-" + roundId,
                    "correlation-" + roundId,
                    openedAtUtc));
                Assert.Equal(VoteStartStatus.Started, result.Status);
                new CastVoteUseCase(Store).Execute(roundId, "EOS-A", VoteChoice.Yes, openedAtUtc);
                new CastVoteUseCase(Store).Execute(roundId, "EOS-B", VoteChoice.Yes, openedAtUtc);
                return result.Round!;
            }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
