using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class SevenDaysGrantItemEvidenceCapture
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private readonly SevenDaysOnlinePlayerProjection onlinePlayers;
        private readonly SevenDaysPlayerEvidenceSnapshotReader snapshotReader;

        public SevenDaysGrantItemEvidenceCapture(
            SevenDaysOnlinePlayerProjection onlinePlayers,
            SevenDaysPlayerEvidenceSnapshotReader snapshotReader)
        {
            this.onlinePlayers = onlinePlayers ?? throw new ArgumentNullException(nameof(onlinePlayers));
            this.snapshotReader = snapshotReader ?? throw new ArgumentNullException(nameof(snapshotReader));
        }

        public DateTimeOffset? FindOnlineObservedAtUtc(int entityId, string combinedId)
        {
            if (entityId < 0 || string.IsNullOrWhiteSpace(combinedId)) return null;
            var matches = onlinePlayers.GetOnlineAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult()
                .Players
                .Where(player =>
                    player.EntityId == entityId &&
                    (string.Equals(
                         player.PlatformIdentity.CombinedId,
                         combinedId,
                         StringComparison.Ordinal) ||
                     string.Equals(
                         player.CrossplatformIdentity?.CombinedId,
                         combinedId,
                         StringComparison.Ordinal)))
                .Take(2)
                .ToArray();
            return matches.Length == 1 ? matches[0].ObservedAtUtc : (DateTimeOffset?)null;
        }

        public Task<GrantItemInventorySnapshot> CaptureAsync(
            GrantItemSnapshotCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            return GameThreadDispatcher.Enqueue(
                "7DPanel.Players.GrantItem.CaptureEvidence",
                () => CaptureOnGameThread(command),
                DispatchTimeout,
                cancellationToken);
        }

        private GrantItemInventorySnapshot CaptureOnGameThread(GrantItemSnapshotCommand command)
        {
            var client = global::ConnectionManager.Instance?.Clients?.ForEntityId(
                command.Target.EntityId);
            var combinedId = client?.CrossplatformId?.CombinedString;
            if (client == null ||
                !string.Equals(combinedId, command.Target.CrossplatformId, StringComparison.Ordinal) ||
                !string.Equals(
                    global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld),
                    command.Target.WorldId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The fixed player target is no longer available.");
            }

            var entity = global::GameManager.Instance?.World?.GetEntity(
                command.Target.EntityId) as global::EntityPlayer;
            if (entity == null)
                throw new InvalidOperationException("The fixed player target is no longer online.");

            var playerData = new global::PlayerDataFile();
            playerData.FromPlayer(entity);
            var draft = snapshotReader.Read(client, playerData);
            var inventory = draft?.Inventory;
            if (draft == null || inventory == null ||
                !string.Equals(draft.CrossplatformId, command.Target.CrossplatformId, StringComparison.Ordinal) ||
                !string.Equals(draft.WorldId, command.Target.WorldId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Scalar inventory evidence is unavailable.");
            }

            return new GrantItemInventorySnapshot(
                draft.ObservedAtUtc,
                inventory.GameVersion,
                inventory.CatalogVersion,
                inventory.CatalogResolution,
                inventory.Fingerprint,
                inventory.Items);
        }
    }
}
