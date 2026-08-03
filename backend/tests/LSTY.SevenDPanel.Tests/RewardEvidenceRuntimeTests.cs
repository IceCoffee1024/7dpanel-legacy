using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Rewards;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Economy")]
    [Trait("Boundary", "SevenDays")]
    public sealed class RewardEvidenceRuntimeTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
        private static readonly DateTimeOffset ObservedAtUtc =
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Persisted_scalar_and_session_evidence_invoke_reward_use_cases()
        {
            using var history = new PlayerHistoryWriteService(
                new RecordingHistoryStore(), 4, TimeSpan.FromSeconds(1), () => ObservedAtUtc);
            using var evidence = new PlayerEvidenceWriteService(
                new RecordingEvidenceStore(), 4, TimeSpan.FromSeconds(1), () => ObservedAtUtc);
            var achievements = new List<ObserveAchievementCommand>();
            EvaluateOnlineRewardsCommand? onlineEvaluation = null;
            var achievementsCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var onlineEvaluationCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var runtime = new RewardEvidenceRuntime(
                history.SubscribePersisted,
                evidence.SubscribePersisted,
                (command, _) =>
                {
                    lock (achievements)
                    {
                        achievements.Add(command);
                        if (achievements.Count == 4)
                            achievementsCompleted.TrySetResult(true);
                    }
                    return Task.CompletedTask;
                },
                (command, _) =>
                {
                    onlineEvaluation = command;
                    onlineEvaluationCompleted.TrySetResult(true);
                    return Task.CompletedTask;
                },
                new NoopRuntime(),
                _ => { });

            history.Start();
            evidence.Start();
            runtime.Start();

            Assert.True(evidence.TryRecord(new PlayerEvidenceDraft(
                "EOS-player",
                "server-1",
                "world-1",
                ObservedAtUtc.AddMinutes(-1),
                new PlayerEvidenceSessionDraft(
                    1,
                    ObservedAtUtc.AddMinutes(-1),
                    null,
                    null,
                    null,
                    PlayerProfileSectionState.Available),
                null,
                null,
                null,
                null)));
            Assert.True(history.TryRecord(Snapshot()));

            await WaitForCompletion(
                Task.WhenAll(achievementsCompleted.Task, onlineEvaluationCompleted.Task),
                TestContext.Current.CancellationToken);
            ObserveAchievementCommand[] observedAchievements;
            lock (achievements) observedAchievements = achievements.ToArray();
            Assert.Equal(
                new[]
                {
                    AchievementStatistic.Level,
                    AchievementStatistic.ZombieKills,
                    AchievementStatistic.PlayerKills,
                    AchievementStatistic.Deaths
                },
                observedAchievements.Select(command => command.Statistic));
            Assert.All(observedAchievements, command =>
            {
                Assert.Equal("EOS-player", command.CrossplatformId);
                Assert.Equal(42, command.ExpectedEntityId);
                Assert.Equal("world-1", command.ExpectedWorldId);
            });
            Assert.NotNull(onlineEvaluation);
            Assert.Equal("EOS-player", onlineEvaluation!.CrossplatformId);
            Assert.Equal(42, onlineEvaluation.ExpectedEntityId);
            Assert.Equal("world-1", onlineEvaluation.ExpectedWorldId);

            runtime.Stop();
            evidence.Stop();
            history.Stop();
        }

        private static async Task WaitForCompletion(Task task, CancellationToken cancellationToken)
        {
            var timeout = Task.Delay(TestTimeout, cancellationToken);
            var completed = await Task.WhenAny(task, timeout);
            if (completed != task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException("Reward evidence callbacks did not complete in time.");
            }

            await task;
        }

        private static PlayerSnapshot Snapshot() => new PlayerSnapshot(
            42,
            "Tester",
            new PlayerPlatformIdentity("Steam-player", "Steam"),
            new PlayerPlatformIdentity("EOS-player", "EOS"),
            PlayerDeviceType.Windows,
            null,
            0,
            null,
            null,
            0,
            new PlayerPosition(1, 2, 3),
            false,
            100,
            100,
            10,
            0,
            20,
            1,
            2,
            60,
            0,
            0,
            10,
            5,
            ObservedAtUtc);

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "SevenDays")]

        private sealed class NoopRuntime : IModRuntime
        {
            public void Start() { }
            public void MarkGameReady() { }
            public void Stop() { }
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingHistoryStore : IPlayerHistoryStore
        {
            public void Append(PlayerSnapshot snapshot) { }
            public void AppendGap(PlayerHistoryGap gap) { }
            public HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query) =>
                throw new NotSupportedException();
            public HistoricalPlayerDetails? GetPlayer(string crossplatformId) =>
                throw new NotSupportedException();
            public PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query) =>
                throw new NotSupportedException();
            public PlayerTrackHistory? GetPlayerTrack(GetPlayerTrackQuery query) =>
                throw new NotSupportedException();
            public IReadOnlyList<HistoricalPlayerLastRetainedLocation>
                GetHistoricalPlayerLastRetainedLocations(HistoricalPlayerLastLocationsStoreQuery query) =>
                throw new NotSupportedException();
            public int Compact(DateTimeOffset utcNow, int maximumDeletes) => 0;
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingEvidenceStore : IPlayerEvidenceStore
        {
            public void AppendSession(PlayerSession session) { }
            public void AppendActivity(PlayerActivityEvent activity) { }
            public void AppendInventorySnapshot(PlayerInventorySnapshot snapshot) { }
            public void AppendSkillSnapshot(PlayerSkillSnapshot snapshot) { }
            public void AppendInventoryGap(PlayerEvidenceGap gap) { }
            public void AppendSkillGap(PlayerEvidenceGap gap) { }
            public IReadOnlyList<PlayerSession> GetSessions(PlayerEvidenceRangeQuery query) =>
                throw new NotSupportedException();
            public IReadOnlyList<PlayerActivityEvent> GetActivity(PlayerEvidenceRangeQuery query) =>
                throw new NotSupportedException();
            public PlayerInventorySnapshotsPage GetInventorySnapshots(
                PlayerInventorySnapshotsQuery query) => throw new NotSupportedException();
            public PlayerSkillSnapshotsPage GetSkillSnapshots(PlayerSkillSnapshotsQuery query) =>
                throw new NotSupportedException();
            public IReadOnlyList<PlayerEvidenceGap> GetInventoryGaps(PlayerEvidenceRangeQuery query) =>
                throw new NotSupportedException();
            public IReadOnlyList<PlayerEvidenceGap> GetSkillGaps(PlayerEvidenceRangeQuery query) =>
                throw new NotSupportedException();
            public void Compact(PlayerEvidenceCompactionRequest request) { }
        }
    }
}
