using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LSTY.SevenDPanel.Application
{
    public sealed class OverviewAttentionEvaluator
    {
        private const double LowDiskSpaceThreshold = 0.10;

        public IReadOnlyList<OverviewAttention> Evaluate(
            GameOverviewSnapshot game,
            HostOverviewSnapshot host,
            RestartPolicySummary restartPolicy)
        {
            var attention = new List<OverviewAttention>();

            if (game.Availability == AvailabilityState.Stale)
                attention.Add(new OverviewAttention("game_snapshot_stale"));
            else if (game.Availability != AvailabilityState.Available)
                attention.Add(new OverviewAttention("game_not_ready"));

            foreach (var volume in host.StorageVolumes)
            {
                if (volume.TotalBytes.HasValue &&
                    volume.AvailableBytes.HasValue &&
                    volume.TotalBytes.Value > 0 &&
                    (double)volume.AvailableBytes.Value / volume.TotalBytes.Value <= LowDiskSpaceThreshold)
                {
                    attention.Add(new OverviewAttention("disk_space_low"));
                    break;
                }
            }

            if (restartPolicy.Availability != AvailabilityState.Available || !restartPolicy.IsConfigured)
                attention.Add(new OverviewAttention("restart_script_not_configured"));

            if (host.PublicNetwork.Availability == AvailabilityState.Unavailable)
                attention.Add(new OverviewAttention("public_ip_unavailable"));

            return new ReadOnlyCollection<OverviewAttention>(attention);
        }
    }
}
