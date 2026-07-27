using System;

namespace LSTY.SevenDPanel.Adapters.Local.Restore
{
    public sealed class WorldRestoreTimingGate
    {
        public const string UnverifiedError = "world_restore_timing_unverified";

        public bool IsApproved(string gameVersion)
        {
            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new ArgumentException("A game version is required.", nameof(gameVersion));

            // Task 10 must replace this closed gate only after persisted, real
            // v3.0.1-b4 evidence proves execution happens before world open.
            return false;
        }
    }
}
