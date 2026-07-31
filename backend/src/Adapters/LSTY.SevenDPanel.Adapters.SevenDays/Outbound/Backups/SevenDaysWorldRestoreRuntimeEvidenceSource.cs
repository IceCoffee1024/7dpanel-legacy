using LSTY.SevenDPanel.Application.Backups;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Backups
{
    public sealed class SevenDaysWorldRestoreRuntimeEvidenceSource :
        IWorldRestoreRuntimeEvidenceSource
    {
        public WorldRestoreRuntimeEvidence Capture()
        {
            if (!global::ThreadManager.IsMainThread())
            {
                return new WorldRestoreRuntimeEvidence(
                    false,
                    false,
                    false,
                    false,
                    null,
                    null,
                    null);
            }

            var manager = global::GameManager.Instance;
            return new WorldRestoreRuntimeEvidence(
                true,
                global::GameManager.IsDedicatedServer,
                manager != null,
                manager?.World != null,
                global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld),
                global::GameIO.GetSaveGameDir(),
                global::GamePrefs.GetString(global::EnumGamePrefs.GameVersion));
        }
    }
}

