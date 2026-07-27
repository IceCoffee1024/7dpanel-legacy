using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application.Community;
using UnityEngine;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Community
{
    public sealed class SevenDaysCommunityGameGateway : ICommunityGameGateway
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly Func<
            string,
            Func<TeleportActionResult>,
            TimeSpan,
            CancellationToken,
            Task<TeleportActionResult>> dispatcher;
        private readonly Func<TeleportActionCommand, CommunityTeleportRuntimeContext?> captureContext;

        public SevenDaysCommunityGameGateway()
            : this(
                (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                CaptureNativeContext)
        {
        }

        internal SevenDaysCommunityGameGateway(
            Func<
                string,
                Func<TeleportActionResult>,
                TimeSpan,
                CancellationToken,
                Task<TeleportActionResult>> dispatcher,
            Func<TeleportActionCommand, CommunityTeleportRuntimeContext?> captureContext)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.captureContext = captureContext ?? throw new ArgumentNullException(nameof(captureContext));
        }

        public async Task<TeleportActionResult> TeleportAsync(
            TeleportActionCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var started = 0;
            try
            {
                return await dispatcher(
                        "7DPanel.Community.Teleport",
                        () => ExecuteOnGameThread(command, cancellationToken, ref started),
                        DispatchTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Volatile.Read(ref started) == 0
                    ? TeleportActionResult.Cancelled()
                    : TeleportActionResult.ResultUnknown();
            }
            catch
            {
                return Volatile.Read(ref started) == 0
                    ? TeleportActionResult.Failed(TeleportFailureCodes.GatewayFailure)
                    : TeleportActionResult.ResultUnknown();
            }
        }

        private TeleportActionResult ExecuteOnGameThread(
            TeleportActionCommand command,
            CancellationToken cancellationToken,
            ref int started)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = captureContext(command);
            if (context == null)
                return TeleportActionResult.Rejected(TeleportFailureCodes.PlayerNotOnline);
            if (!string.Equals(context.CrossplatformId, command.CrossplatformId, StringComparison.Ordinal) ||
                context.EntityId != command.ExpectedEntityId ||
                !string.Equals(context.WorldId, command.ExpectedWorldId, StringComparison.Ordinal))
            {
                return TeleportActionResult.Rejected(TeleportFailureCodes.TargetChanged);
            }
            if (!context.IsAlive)
                return TeleportActionResult.Rejected(TeleportFailureCodes.PlayerDead);
            if (!context.IsSpawned)
                return TeleportActionResult.Rejected(TeleportFailureCodes.PlayerNotSpawned);
            if (!context.DestinationInBounds)
                return TeleportActionResult.Rejected(TeleportFailureCodes.DestinationOutOfBounds);
            if (command.DenyDuringBloodMoon && context.IsBloodMoon)
                return TeleportActionResult.Rejected(TeleportFailureCodes.BloodMoonDenied);

            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref started, 1);
            context.SendTypedTeleport(command.Destination);
            return TeleportActionResult.Succeeded(context.Origin);
        }

        private static CommunityTeleportRuntimeContext? CaptureNativeContext(
            TeleportActionCommand command)
        {
            var clients = global::ConnectionManager.Instance?.Clients;
            var client = clients?.ForEntityId(command.ExpectedEntityId);
            var combinedId = client?.CrossplatformId?.CombinedString;
            var world = global::GameManager.Instance?.World;
            var player = world?.GetEntity(command.ExpectedEntityId) as global::EntityPlayer;
            var worldId = global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld);
            if (client == null || player == null || world == null ||
                string.IsNullOrWhiteSpace(combinedId) || string.IsNullOrWhiteSpace(worldId))
            {
                return null;
            }

            var destination = new Vector3(
                (float)command.Destination.X,
                (float)command.Destination.Y,
                (float)command.Destination.Z);
            var destinationInBounds = string.Equals(
                    command.Destination.WorldId,
                    worldId,
                    StringComparison.Ordinal) &&
                world.IsPositionInBounds(destination);
            var isBloodMoon = global::GameUtils.IsBloodMoonTime(
                world.worldTime,
                global::GameUtils.CalcDuskDawnHours(
                    global::GamePrefs.GetInt(global::EnumGamePrefs.DayLightLength)),
                global::GameStats.GetInt(global::EnumGameStats.BloodMoonDay));
            var origin = new WorldPosition(
                worldId,
                player.position.x,
                player.position.y,
                player.position.z,
                player.rotation.y);

            return new CommunityTeleportRuntimeContext(
                combinedId!,
                client.entityId,
                worldId,
                origin,
                !player.IsDead(),
                player.IsSpawned(),
                destinationInBounds,
                isBloodMoon,
                fixedDestination =>
                {
                    var package = global::NetPackageManager
                        .GetPackage<global::NetPackageTeleportPlayer>()
                        .Setup(
                            new Vector3(
                                (float)fixedDestination.X,
                                (float)fixedDestination.Y,
                                (float)fixedDestination.Z),
                            new Vector3(0f, (float)fixedDestination.Yaw, 0f),
                            false);
                    client.SendPackage(package);
                });
        }
    }

    internal sealed class CommunityTeleportRuntimeContext
    {
        public CommunityTeleportRuntimeContext(
            string crossplatformId,
            int entityId,
            string worldId,
            WorldPosition origin,
            bool alive,
            bool spawned,
            bool destinationInBounds,
            bool bloodMoon,
            Action<WorldPosition> sendTypedTeleport)
        {
            if (string.IsNullOrWhiteSpace(crossplatformId))
                throw new ArgumentException("A cross-platform identity is required.", nameof(crossplatformId));
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            if (string.IsNullOrWhiteSpace(worldId))
                throw new ArgumentException("A world identity is required.", nameof(worldId));
            CrossplatformId = crossplatformId.Trim();
            EntityId = entityId;
            WorldId = worldId.Trim();
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            IsAlive = alive;
            IsSpawned = spawned;
            DestinationInBounds = destinationInBounds;
            IsBloodMoon = bloodMoon;
            SendTypedTeleport = sendTypedTeleport ??
                throw new ArgumentNullException(nameof(sendTypedTeleport));
        }

        public string CrossplatformId { get; }
        public int EntityId { get; }
        public string WorldId { get; }
        public WorldPosition Origin { get; }
        public bool IsAlive { get; }
        public bool IsSpawned { get; }
        public bool DestinationInBounds { get; }
        public bool IsBloodMoon { get; }
        public Action<WorldPosition> SendTypedTeleport { get; }
    }
}
