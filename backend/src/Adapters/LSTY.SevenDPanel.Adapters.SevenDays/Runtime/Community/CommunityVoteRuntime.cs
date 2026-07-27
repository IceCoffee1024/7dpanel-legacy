using System;
using System.Threading;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Domain.Community;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Community
{
    public sealed class CommunityVoteRuntime : IModRuntime, IDisposable
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);

        private readonly object sync = new object();
        private readonly IExpiringVoteRoundReader rounds;
        private readonly SettleVoteUseCase settleVote;
        private readonly DispatchVoteActionUseCase dispatchVoteAction;
        private readonly RecoverQueuedVoteActionsUseCase recoverQueuedActions;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan interval;
        private readonly Timer timer;
        private readonly IModRuntime? inner;
        private bool started;
        private bool stopped;
        private bool running;
        private bool disposed;

        public CommunityVoteRuntime(
            IExpiringVoteRoundReader rounds,
            SettleVoteUseCase settleVote,
            DispatchVoteActionUseCase dispatchVoteAction,
            RecoverQueuedVoteActionsUseCase recoverQueuedActions,
            Func<DateTimeOffset> utcNow)
            : this(
                rounds,
                settleVote,
                dispatchVoteAction,
                recoverQueuedActions,
                utcNow,
                DefaultInterval)
        {
        }

        public CommunityVoteRuntime(
            IExpiringVoteRoundReader rounds,
            SettleVoteUseCase settleVote,
            DispatchVoteActionUseCase dispatchVoteAction,
            RecoverQueuedVoteActionsUseCase recoverQueuedActions,
            Func<DateTimeOffset> utcNow,
            TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            this.rounds = rounds ?? throw new ArgumentNullException(nameof(rounds));
            this.settleVote = settleVote ?? throw new ArgumentNullException(nameof(settleVote));
            this.dispatchVoteAction = dispatchVoteAction ??
                throw new ArgumentNullException(nameof(dispatchVoteAction));
            this.recoverQueuedActions = recoverQueuedActions ??
                throw new ArgumentNullException(nameof(recoverQueuedActions));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.interval = interval;
            timer = new Timer(_ => RunTimerTick(), null,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public CommunityVoteRuntime(
            IExpiringVoteRoundReader rounds,
            SettleVoteUseCase settleVote,
            DispatchVoteActionUseCase dispatchVoteAction,
            RecoverQueuedVoteActionsUseCase recoverQueuedActions,
            Func<DateTimeOffset> utcNow,
            IModRuntime inner)
            : this(
                rounds,
                settleVote,
                dispatchVoteAction,
                recoverQueuedActions,
                utcNow)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                if (started || stopped) return;
                var now = utcNow();
                recoverQueuedActions.Execute(now);
                inner?.Start();
                started = true;
                timer.Change(interval, interval);
            }
        }

        public void MarkGameReady()
        {
            inner?.MarkGameReady();
        }

        public void Stop()
        {
            lock (sync)
            {
                if (stopped) return;
                stopped = true;
                timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                inner?.Stop();
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
            }

            Stop();
            timer.Dispose();
        }

        public void RunOnce()
        {
            lock (sync)
            {
                if (!started || stopped || running) return;
                running = true;
            }

            try
            {
                var now = utcNow();
                foreach (var round in rounds.ListDueOpenRounds(now))
                {
                    if (IsStopped()) return;
                    var settlement = settleVote.Execute(round.RoundId, now);
                    if (settlement.Round.State != VoteRoundState.Passed || IsStopped()) continue;
                    dispatchVoteAction.ExecuteAsync(
                            settlement.Round.RoundId,
                            now,
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
            }
            finally
            {
                lock (sync) running = false;
            }
        }

        private void RunTimerTick()
        {
            try { RunOnce(); }
            catch { }
        }

        private bool IsStopped()
        {
            lock (sync) return stopped;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CommunityVoteRuntime));
        }
    }
}
