using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetOverviewUseCase
    {
        private readonly IGameOverviewQuery gameQuery;
        private readonly IHostOverviewQuery hostQuery;
        private readonly IRestartPolicyQuery restartPolicyQuery;
        private readonly IRecentActivityQuery recentActivityQuery;
        private readonly OverviewAttentionEvaluator attentionEvaluator;

        public GetOverviewUseCase(
            IGameOverviewQuery gameQuery,
            IHostOverviewQuery hostQuery,
            IRestartPolicyQuery restartPolicyQuery,
            IRecentActivityQuery recentActivityQuery)
        {
            this.gameQuery = gameQuery ?? throw new ArgumentNullException(nameof(gameQuery));
            this.hostQuery = hostQuery ?? throw new ArgumentNullException(nameof(hostQuery));
            this.restartPolicyQuery = restartPolicyQuery ?? throw new ArgumentNullException(nameof(restartPolicyQuery));
            this.recentActivityQuery = recentActivityQuery ?? throw new ArgumentNullException(nameof(recentActivityQuery));
            attentionEvaluator = new OverviewAttentionEvaluator();
        }

        public async Task<OverviewSnapshot> ExecuteAsync(
            OverviewAudience audience,
            CancellationToken cancellationToken)
        {
            if (audience != OverviewAudience.Owner && audience != OverviewAudience.NonOwner)
                throw new ArgumentOutOfRangeException(nameof(audience));

            var gameTask = GetGameAsync(cancellationToken);
            var hostTask = GetHostAsync(cancellationToken);
            var recentActivityTask = GetRecentActivityAsync(cancellationToken);
            var restartPolicy = GetRestartPolicy(cancellationToken);

            await Task.WhenAll(gameTask, hostTask, recentActivityTask).ConfigureAwait(false);

            var game = await gameTask.ConfigureAwait(false);
            var host = await hostTask.ConfigureAwait(false);
            var recentActivity = await recentActivityTask.ConfigureAwait(false);
            if (audience == OverviewAudience.NonOwner)
                host = host.ForNonOwner();

            return new OverviewSnapshot(
                DetermineAvailability(game, host, restartPolicy, recentActivity),
                game,
                host,
                restartPolicy,
                recentActivity,
                attentionEvaluator.Evaluate(game, host, restartPolicy));
        }

        private async Task<GameOverviewSnapshot> GetGameAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await gameQuery.GetGameOverviewAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return GameOverviewSnapshot.Unavailable();
            }
        }

        private async Task<HostOverviewSnapshot> GetHostAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await hostQuery.GetHostOverviewAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return HostOverviewSnapshot.Unavailable();
            }
        }

        private RestartPolicySummary GetRestartPolicy(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return restartPolicyQuery.Query();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return RestartPolicySummary.Unavailable();
            }
        }

        private async Task<RecentActivitySnapshot> GetRecentActivityAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await recentActivityQuery.GetRecentActivityAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return RecentActivitySnapshot.Unavailable();
            }
        }

        private static AvailabilityState DetermineAvailability(
            GameOverviewSnapshot game,
            HostOverviewSnapshot host,
            RestartPolicySummary restartPolicy,
            RecentActivitySnapshot recentActivity)
        {
            var availableCount = 0;
            var isDegraded = false;
            var hasStale =
                game.Availability == AvailabilityState.Stale ||
                host.Availability == AvailabilityState.Stale ||
                restartPolicy.Availability == AvailabilityState.Stale ||
                recentActivity.Availability == AvailabilityState.Stale;

            CountAvailability(game.Availability, ref availableCount, ref isDegraded);
            CountAvailability(host.Availability, ref availableCount, ref isDegraded);
            CountAvailability(restartPolicy.Availability, ref availableCount, ref isDegraded);
            CountAvailability(recentActivity.Availability, ref availableCount, ref isDegraded);

            if (availableCount == 0 && !hasStale)
                return AvailabilityState.Unavailable;

            return isDegraded ? AvailabilityState.Stale : AvailabilityState.Available;
        }

        private static void CountAvailability(
            AvailabilityState availability,
            ref int availableCount,
            ref bool isDegraded)
        {
            if (availability == AvailabilityState.Available)
                availableCount++;
            else
                isDegraded = true;
        }
    }
}
