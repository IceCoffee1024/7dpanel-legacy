using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Rewards
{
    public sealed class RewardEvidenceRuntime : IModRuntime, IDisposable
    {
        private readonly Func<Action<PlayerSnapshot>, IDisposable> subscribeScalar;
        private readonly Func<Action<PlayerSession>, IDisposable> subscribeSession;
        private readonly Action<PlayerSnapshot> enqueueScalarPersistentWork;
        private readonly Action<PlayerSession> enqueueSessionPersistentWork;
        private readonly Action cancelPersistentWork;
        private readonly IModRuntime inner;
        private readonly object gate = new object();
        private IDisposable? scalarSubscription;
        private IDisposable? sessionSubscription;
        private bool started;
        private bool stopped;

        public RewardEvidenceRuntime(
            Func<Action<PlayerSnapshot>, IDisposable> subscribeScalar,
            Func<Action<PlayerSession>, IDisposable> subscribeSession,
            Action<PlayerSnapshot> enqueueScalarPersistentWork,
            Action<PlayerSession> enqueueSessionPersistentWork,
            IModRuntime inner)
            : this(
                subscribeScalar,
                subscribeSession,
                enqueueScalarPersistentWork,
                enqueueSessionPersistentWork,
                () => { },
                inner)
        {
        }

        public RewardEvidenceRuntime(
            PlayerHistoryWriteService historyWriter,
            PlayerEvidenceWriteService evidenceWriter,
            ObserveAchievementUseCase observeAchievement,
            EvaluateOnlineRewardsUseCase evaluateOnlineRewards,
            IModRuntime inner,
            Action<string>? log = null)
            : this(
                RequireScalarSubscription(historyWriter),
                RequireSessionSubscription(evidenceWriter),
                new RewardEvidenceDispatcher(
                    RequireAchievementObserver(observeAchievement),
                    RequireOnlineEvaluator(evaluateOnlineRewards),
                    log ?? (_ => { })),
                inner)
        {
        }

        internal RewardEvidenceRuntime(
            Func<Action<PlayerSnapshot>, IDisposable> subscribeScalar,
            Func<Action<PlayerSession>, IDisposable> subscribeSession,
            Func<ObserveAchievementCommand, CancellationToken, Task> observeAchievement,
            Func<EvaluateOnlineRewardsCommand, CancellationToken, Task> evaluateOnlineRewards,
            IModRuntime inner,
            Action<string> log)
            : this(
                subscribeScalar,
                subscribeSession,
                new RewardEvidenceDispatcher(
                    observeAchievement,
                    evaluateOnlineRewards,
                    log),
                inner)
        {
        }

        private RewardEvidenceRuntime(
            Func<Action<PlayerSnapshot>, IDisposable> subscribeScalar,
            Func<Action<PlayerSession>, IDisposable> subscribeSession,
            RewardEvidenceDispatcher dispatcher,
            IModRuntime inner)
            : this(
                subscribeScalar,
                subscribeSession,
                dispatcher.ObserveScalar,
                dispatcher.ObserveSession,
                dispatcher.Cancel,
                inner)
        {
        }

        private RewardEvidenceRuntime(
            Func<Action<PlayerSnapshot>, IDisposable> subscribeScalar,
            Func<Action<PlayerSession>, IDisposable> subscribeSession,
            Action<PlayerSnapshot> enqueueScalarPersistentWork,
            Action<PlayerSession> enqueueSessionPersistentWork,
            Action cancelPersistentWork,
            IModRuntime inner)
        {
            this.subscribeScalar = subscribeScalar ??
                throw new ArgumentNullException(nameof(subscribeScalar));
            this.subscribeSession = subscribeSession ??
                throw new ArgumentNullException(nameof(subscribeSession));
            this.enqueueScalarPersistentWork = enqueueScalarPersistentWork ??
                throw new ArgumentNullException(nameof(enqueueScalarPersistentWork));
            this.enqueueSessionPersistentWork = enqueueSessionPersistentWork ??
                throw new ArgumentNullException(nameof(enqueueSessionPersistentWork));
            this.cancelPersistentWork = cancelPersistentWork ??
                throw new ArgumentNullException(nameof(cancelPersistentWork));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            lock (gate)
            {
                if (started || stopped) return;

                IDisposable? pendingScalar = null;
                IDisposable? pendingSession = null;
                var innerStartAttempted = false;
                try
                {
                    pendingScalar = subscribeScalar(EnqueueScalar);
                    if (pendingScalar == null)
                        throw new InvalidOperationException("The scalar evidence subscription is required.");
                    pendingSession = subscribeSession(EnqueueSession);
                    if (pendingSession == null)
                        throw new InvalidOperationException("The session evidence subscription is required.");
                    innerStartAttempted = true;
                    inner.Start();
                    scalarSubscription = pendingScalar;
                    sessionSubscription = pendingSession;
                    started = true;
                }
                catch
                {
                    TryDispose(pendingSession);
                    TryDispose(pendingScalar);
                    if (innerStartAttempted)
                    {
                        try { inner.Stop(); } catch { }
                    }
                    try { cancelPersistentWork(); } catch { }
                    stopped = true;
                    throw;
                }
            }
        }

        public void MarkGameReady() => inner.MarkGameReady();

        public void Stop()
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
                started = false;

                var failures = new List<Exception>();
                try
                {
                    inner.Stop();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                try
                {
                    Interlocked.Exchange(ref sessionSubscription, null)?.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                try
                {
                    Interlocked.Exchange(ref scalarSubscription, null)?.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                try { cancelPersistentWork(); }
                catch (Exception exception) { failures.Add(exception); }

                if (failures.Count > 0) throw new AggregateException(failures);
            }
        }

        public void Dispose() => Stop();

        private void EnqueueScalar(PlayerSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            enqueueScalarPersistentWork(snapshot);
        }

        private void EnqueueSession(PlayerSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            enqueueSessionPersistentWork(session);
        }

        private static void TryDispose(IDisposable? value)
        {
            try { value?.Dispose(); } catch { }
        }

        private static Func<Action<PlayerSnapshot>, IDisposable> RequireScalarSubscription(
            PlayerHistoryWriteService writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            return writer.SubscribePersisted;
        }

        private static Func<Action<PlayerSession>, IDisposable> RequireSessionSubscription(
            PlayerEvidenceWriteService writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            return writer.SubscribePersisted;
        }

        private static Func<ObserveAchievementCommand, CancellationToken, Task> RequireAchievementObserver(
            ObserveAchievementUseCase useCase)
        {
            if (useCase == null) throw new ArgumentNullException(nameof(useCase));
            return (command, cancellationToken) =>
                useCase.ExecuteAsync(command, cancellationToken);
        }

        private static Func<EvaluateOnlineRewardsCommand, CancellationToken, Task> RequireOnlineEvaluator(
            EvaluateOnlineRewardsUseCase useCase)
        {
            if (useCase == null) throw new ArgumentNullException(nameof(useCase));
            return (command, cancellationToken) =>
                useCase.ExecuteAsync(command, cancellationToken);
        }

        private sealed class RewardEvidenceDispatcher
        {
            private readonly Func<ObserveAchievementCommand, CancellationToken, Task> observeAchievement;
            private readonly Func<EvaluateOnlineRewardsCommand, CancellationToken, Task> evaluateOnlineRewards;
            private readonly Action<string> log;
            private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
            private readonly object sync = new object();
            private readonly Dictionary<string, PlayerSnapshot> snapshots =
                new Dictionary<string, PlayerSnapshot>(StringComparer.Ordinal);
            private readonly Dictionary<string, PlayerSession> sessions =
                new Dictionary<string, PlayerSession>(StringComparer.Ordinal);

            public RewardEvidenceDispatcher(
                Func<ObserveAchievementCommand, CancellationToken, Task> observeAchievement,
                Func<EvaluateOnlineRewardsCommand, CancellationToken, Task> evaluateOnlineRewards,
                Action<string> log)
            {
                this.observeAchievement = observeAchievement ??
                    throw new ArgumentNullException(nameof(observeAchievement));
                this.evaluateOnlineRewards = evaluateOnlineRewards ??
                    throw new ArgumentNullException(nameof(evaluateOnlineRewards));
                this.log = log ?? throw new ArgumentNullException(nameof(log));
            }

            public void ObserveScalar(PlayerSnapshot snapshot)
            {
                if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
                var crossplatformId = snapshot.CrossplatformIdentity?.CombinedId;
                if (string.IsNullOrWhiteSpace(crossplatformId)) return;

                PlayerSession? session;
                lock (sync)
                {
                    if (!snapshots.TryGetValue(crossplatformId!, out var current) ||
                        snapshot.ObservedAtUtc >= current.ObservedAtUtc)
                    {
                        snapshots[crossplatformId!] = snapshot;
                    }
                    else
                    {
                        snapshot = current;
                    }
                    sessions.TryGetValue(crossplatformId!, out session);
                }

                if (session == null || !IsSameObservation(snapshot, session)) return;
                Dispatch(snapshot, session, snapshot.ObservedAtUtc);
            }

            public void ObserveSession(PlayerSession session)
            {
                if (session == null) throw new ArgumentNullException(nameof(session));
                if (string.IsNullOrWhiteSpace(session.CrossplatformId)) return;

                PlayerSnapshot? snapshot;
                lock (sync)
                {
                    if (!sessions.TryGetValue(session.CrossplatformId, out var current) ||
                        IsNewerSession(session, current))
                    {
                        sessions[session.CrossplatformId] = session;
                    }
                    else
                    {
                        session = current;
                    }
                    snapshots.TryGetValue(session.CrossplatformId, out snapshot);
                }

                if (snapshot == null || !IsSameObservation(snapshot, session)) return;
                Dispatch(
                    snapshot,
                    session,
                    session.EndedAtUtc ?? snapshot.ObservedAtUtc);
            }

            public void Cancel() => cancellation.Cancel();

            private void Dispatch(
                PlayerSnapshot snapshot,
                PlayerSession session,
                DateTimeOffset evidenceToUtc)
            {
                if (cancellation.IsCancellationRequested) return;
                var crossplatformId = snapshot.CrossplatformIdentity!.CombinedId!;
                var scalarCorrelation =
                    "reward-evidence:player-snapshot:" +
                    session.ServerId + ":" + session.WorldId + ":" +
                    crossplatformId + ":" +
                    snapshot.ObservedAtUtc.ToUnixTimeMilliseconds();
                Observe(
                    snapshot,
                    session,
                    AchievementStatistic.Level,
                    snapshot.Level,
                    scalarCorrelation);
                Observe(
                    snapshot,
                    session,
                    AchievementStatistic.ZombieKills,
                    snapshot.ZombieKills,
                    scalarCorrelation);
                Observe(
                    snapshot,
                    session,
                    AchievementStatistic.PlayerKills,
                    snapshot.PlayerKills,
                    scalarCorrelation);
                Observe(
                    snapshot,
                    session,
                    AchievementStatistic.Deaths,
                    snapshot.Deaths,
                    scalarCorrelation);

                var onlineCorrelation =
                    "reward-evidence:online:" +
                    session.ServerId + ":" + session.WorldId + ":" +
                    crossplatformId + ":" + session.SessionId + ":" +
                    evidenceToUtc.ToUnixTimeMilliseconds();
                Execute(
                    "online evaluation",
                    token => evaluateOnlineRewards(
                        new EvaluateOnlineRewardsCommand(
                            crossplatformId,
                            snapshot.EntityId,
                            session.WorldId,
                            evidenceToUtc,
                            onlineCorrelation),
                        token));
            }

            private void Observe(
                PlayerSnapshot snapshot,
                PlayerSession session,
                AchievementStatistic statistic,
                long value,
                string correlationId)
            {
                var crossplatformId = snapshot.CrossplatformIdentity!.CombinedId!;
                Execute(
                    "achievement observation",
                    token => observeAchievement(
                        new ObserveAchievementCommand(
                            correlationId + ":" + statistic,
                            crossplatformId,
                            snapshot.EntityId,
                            session.WorldId,
                            statistic,
                            value,
                            correlationId,
                            snapshot.ObservedAtUtc),
                        token));
            }

            private void Execute(string operation, Func<CancellationToken, Task> execute)
            {
                if (cancellation.IsCancellationRequested) return;
                try
                {
                    execute(cancellation.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    log("Reward evidence " + operation + " failed: " + exception.Message);
                }
            }

            private static bool IsSameObservation(
                PlayerSnapshot snapshot,
                PlayerSession session) =>
                string.Equals(
                    snapshot.CrossplatformIdentity?.CombinedId,
                    session.CrossplatformId,
                    StringComparison.Ordinal) &&
                snapshot.ObservedAtUtc >= session.StartedAtUtc &&
                (!session.EndedAtUtc.HasValue ||
                 snapshot.ObservedAtUtc <= session.EndedAtUtc.Value);

            private static bool IsNewerSession(PlayerSession candidate, PlayerSession current) =>
                candidate.StartedAtUtc > current.StartedAtUtc ||
                (candidate.StartedAtUtc == current.StartedAtUtc &&
                 candidate.SessionId == current.SessionId &&
                 candidate.EndedAtUtc.HasValue &&
                 !current.EndedAtUtc.HasValue);
        }
    }
}
