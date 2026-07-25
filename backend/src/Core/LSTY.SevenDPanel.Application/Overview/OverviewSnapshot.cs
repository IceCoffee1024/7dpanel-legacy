using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class OverviewSnapshot
    {
        public OverviewSnapshot(
            AvailabilityState availability,
            GameOverviewSnapshot game,
            HostOverviewSnapshot host,
            RestartPolicySummary restartPolicy,
            RecentActivitySnapshot recentActivity,
            IEnumerable<OverviewAttention>? attention)
        {
            Availability = availability;
            Game = game ?? throw new ArgumentNullException(nameof(game));
            Host = host ?? throw new ArgumentNullException(nameof(host));
            RestartPolicy = restartPolicy ?? throw new ArgumentNullException(nameof(restartPolicy));
            RecentActivity = recentActivity ?? throw new ArgumentNullException(nameof(recentActivity));
            Attention = new ReadOnlyCollection<OverviewAttention>(
                (attention ?? Enumerable.Empty<OverviewAttention>()).ToArray());
        }

        public AvailabilityState Availability { get; }

        public GameOverviewSnapshot Game { get; }

        public HostOverviewSnapshot Host { get; }

        public RestartPolicySummary RestartPolicy { get; }

        public RecentActivitySnapshot RecentActivity { get; }

        public IReadOnlyList<OverviewAttention> Attention { get; }
    }
}
