using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Community;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Domain.Community;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class CommunityVoteUseCaseTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 4, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Starting_a_vote_persists_the_eligibility_snapshot_and_enforces_rules_and_cooldowns()
        {
            using var database = new TemporaryDatabase();
            var store = database.Store;
            store.SaveConfiguration(Configuration(
                VoteKind.Kick,
                initiatorCooldown: TimeSpan.FromMinutes(10),
                targetCooldown: TimeSpan.FromMinutes(10),
                globalCooldown: TimeSpan.FromMinutes(10),
                participantMinimumOnline: TimeSpan.FromMinutes(2)));
            var start = new StartVoteUseCase(store);

            var first = start.Execute(StartRequest(
                "round-1",
                "EOS-A",
                "EOS-B",
                Now,
                Eligible(
                    ("EOS-A", 20),
                    ("EOS-B", 15),
                    ("EOS-C", 5),
                    ("EOS-TOO-NEW", 1))));

            Assert.Equal(VoteStartStatus.Started, first.Status);
            Assert.NotNull(first.Round);
            Assert.Equal(3, first.Round!.EligibleCount);
            Assert.Equal("EOS-B", first.Round.TargetCrossplatformId);
            using (var connection = database.ConnectionFactory.Open())
            {
                Assert.Equal(
                    new[] { "EOS-A", "EOS-B", "EOS-C" },
                    connection.Query<string>(
                        "SELECT crossplatform_id FROM vote_eligible_players WHERE round_id = 'round-1' ORDER BY crossplatform_id;")
                        .ToArray());
            }

            Assert.Equal(
                VoteStartStatus.Replayed,
                start.Execute(StartRequest(
                    "round-1",
                    "EOS-A",
                    "EOS-B",
                    Now,
                    Eligible(("EOS-A", 20), ("EOS-B", 15), ("EOS-C", 5))))
                    .Status);
            Assert.Equal(
                VoteStartStatus.ScopeBusy,
                start.Execute(StartRequest(
                    "round-scope-busy",
                    "EOS-C",
                    "EOS-B",
                    Now.AddSeconds(1),
                    Eligible(("EOS-A", 20), ("EOS-B", 15), ("EOS-C", 5))))
                    .Status);

            Assert.Equal(
                VoteSettlementStatus.Settled,
                new SettleVoteUseCase(store).Execute("round-1", Now.AddMinutes(1)).Status);
            Assert.Equal(
                VoteStartStatus.InitiatorCooldown,
                start.Execute(StartRequest(
                    "round-initiator-cooldown",
                    "EOS-A",
                    "EOS-C",
                    Now.AddMinutes(2),
                    Eligible(("EOS-A", 20), ("EOS-B", 15), ("EOS-C", 5))))
                    .Status);
            Assert.Equal(
                VoteStartStatus.TargetCooldown,
                start.Execute(StartRequest(
                    "round-target-cooldown",
                    "EOS-C",
                    "EOS-B",
                    Now.AddMinutes(2),
                    Eligible(("EOS-A", 20), ("EOS-B", 15), ("EOS-C", 5))))
                    .Status);
            Assert.Equal(
                VoteStartStatus.GlobalCooldown,
                start.Execute(StartRequest(
                    "round-global-cooldown",
                    "EOS-C",
                    "EOS-A",
                    Now.AddMinutes(2),
                    Eligible(("EOS-A", 20), ("EOS-B", 15), ("EOS-C", 5))))
                    .Status);

            store.SaveConfiguration(Configuration(VoteKind.Restart, enabled: false));
            Assert.Equal(
                VoteStartStatus.Disabled,
                start.Execute(StartRequest(
                    "round-disabled",
                    "EOS-A",
                    null,
                    Now,
                    Eligible(("EOS-A", 20), ("EOS-B", 15))))
                    .Status);
        }

        [Fact]
        public async Task Ballots_change_once_settle_once_and_keep_action_results_separate_from_passed()
        {
            using var database = new TemporaryDatabase();
            var store = database.Store;
            store.SaveConfiguration(Configuration(VoteKind.Restart, allowVoteChange: true));
            var start = new StartVoteUseCase(store);
            var cast = new CastVoteUseCase(store);
            var settle = new SettleVoteUseCase(store);

            Assert.Equal(
                VoteStartStatus.Started,
                start.Execute(StartRequest(
                    "round-pass",
                    "EOS-A",
                    null,
                    Now,
                    Eligible(("EOS-A", 20), ("EOS-B", 15), ("EOS-C", 5))))
                    .Status);
            Assert.Equal(VoteCastStatus.Accepted, cast.Execute("round-pass", "EOS-A", VoteChoice.Yes, Now).Status);
            Assert.Equal(VoteCastStatus.Replayed, cast.Execute("round-pass", "EOS-A", VoteChoice.Yes, Now).Status);
            Assert.Equal(VoteCastStatus.Changed, cast.Execute("round-pass", "EOS-A", VoteChoice.No, Now).Status);
            Assert.Equal(VoteCastStatus.ChangeNotAllowed, cast.Execute("round-pass", "EOS-A", VoteChoice.Yes, Now).Status);
            Assert.Equal(VoteCastStatus.NotEligible, cast.Execute("round-pass", "EOS-X", VoteChoice.Yes, Now).Status);
            Assert.Equal(VoteCastStatus.Accepted, cast.Execute("round-pass", "EOS-B", VoteChoice.Yes, Now).Status);
            Assert.Equal(VoteCastStatus.Accepted, cast.Execute("round-pass", "EOS-C", VoteChoice.Yes, Now).Status);
            Assert.Equal(VoteSettlementStatus.NotDue, settle.Execute("round-pass", Now.AddSeconds(59)).Status);

            var settlementAttempts = await Task.WhenAll(
                Enumerable.Range(0, 8)
                    .Select(_ => Task.Run(() => settle.Execute("round-pass", Now.AddMinutes(1)))));

            Assert.Single(settlementAttempts.Where(result => result.WasSettled));
            Assert.Equal(VoteRoundState.Passed, store.GetRound("round-pass").State);

            var action = new RecordingVoteActionPort(VoteActionResult.Succeeded("operation-1", null));
            var dispatched = await new DispatchVoteActionUseCase(store, action).ExecuteAsync(
                "round-pass",
                Now.AddMinutes(1).AddSeconds(1),
                CancellationToken.None);
            Assert.Equal(VoteActionDispatchStatus.Dispatched, dispatched.Status);
            Assert.Equal(VoteRoundState.ActionSucceeded, dispatched.Round.State);
            Assert.Equal(1, action.CallCount);

            var secondOpenedAt = Now.AddMinutes(2);
            Assert.Equal(
                VoteStartStatus.Started,
                start.Execute(StartRequest(
                    "round-interrupted",
                    "EOS-A",
                    null,
                    secondOpenedAt,
                    Eligible(("EOS-A", 20), ("EOS-B", 15))))
                    .Status);
            cast.Execute("round-interrupted", "EOS-A", VoteChoice.Yes, secondOpenedAt);
            cast.Execute("round-interrupted", "EOS-B", VoteChoice.Yes, secondOpenedAt);
            settle.Execute("round-interrupted", secondOpenedAt.AddMinutes(1));
            var passed = store.GetRound("round-interrupted");
            Assert.True(store.TryQueueAction(passed.RoundId, passed.RowVersion, secondOpenedAt.AddMinutes(1)));

            var recovered = new RecoverQueuedVoteActionsUseCase(store).Execute(
                secondOpenedAt.AddMinutes(1).AddSeconds(1));

            Assert.Equal(1, recovered);
            Assert.Equal(VoteRoundState.ActionResultUnknown, store.GetRound("round-interrupted").State);
            Assert.Equal(1, action.CallCount);
        }

        private static VoteConfiguration Configuration(
            VoteKind kind,
            bool enabled = true,
            bool allowVoteChange = false,
            TimeSpan? initiatorCooldown = null,
            TimeSpan? targetCooldown = null,
            TimeSpan? globalCooldown = null,
            TimeSpan? participantMinimumOnline = null) =>
            new VoteConfiguration(
                "config-" + kind.ToString().ToLowerInvariant(),
                kind,
                enabled,
                TimeSpan.FromMinutes(1),
                60,
                2,
                TimeSpan.FromMinutes(2),
                participantMinimumOnline ?? TimeSpan.Zero,
                initiatorCooldown ?? TimeSpan.Zero,
                targetCooldown ?? TimeSpan.Zero,
                globalCooldown ?? TimeSpan.Zero,
                "global",
                allowVoteChange,
                Now,
                0);

        private static StartVoteRequest StartRequest(
            string roundId,
            string initiator,
            string? target,
            DateTimeOffset openedAt,
            IReadOnlyList<VoteEligiblePlayer> eligiblePlayers) =>
            new StartVoteRequest(
                roundId,
                target == null ? VoteKind.Restart : VoteKind.Kick,
                initiator,
                target,
                eligiblePlayers,
                "idempotency-" + roundId,
                "correlation-" + roundId,
                openedAt);

        private static IReadOnlyList<VoteEligiblePlayer> Eligible(
            params (string CrossplatformId, int OnlineMinutes)[] players) =>
            players.Select(player => new VoteEligiblePlayer(
                    player.CrossplatformId,
                    TimeSpan.FromMinutes(player.OnlineMinutes)))
                .ToArray();

        private sealed class RecordingVoteActionPort : ICommunityVoteActionPort
        {
            private readonly VoteActionResult result;

            public RecordingVoteActionPort(VoteActionResult result) => this.result = result;

            public int CallCount { get; private set; }

            public Task<VoteActionResult> ExecuteAsync(
                VoteActionCommand command,
                CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(result);
            }
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-community-vote-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase()
            {
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
                Store = new SqliteVoteStore(ConnectionFactory);
            }

            public SqliteConnectionFactory ConnectionFactory { get; }
            public SqliteVoteStore Store { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
