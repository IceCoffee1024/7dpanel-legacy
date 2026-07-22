using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class SevenDaysPlayerActions : IPlayerActions
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly Func<string, Func<KickPlayerActionResult>, TimeSpan, CancellationToken, Task<KickPlayerActionResult>> dispatcher;
        private readonly Func<KickPlayerCommand, KickPlayerActionResult> kick;

        public SevenDaysPlayerActions()
            : this(
                dispatcher: (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                kick: null)
        {
        }

        internal SevenDaysPlayerActions(
            Func<string, Func<KickPlayerActionResult>, TimeSpan, CancellationToken, Task<KickPlayerActionResult>> dispatcher,
            Func<KickPlayerCommand, KickPlayerActionResult>? kick)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.kick = kick ?? CaptureAndKick;
        }

        public Task<KickPlayerActionResult> KickAsync(
            KickPlayerCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            return dispatcher(
                "7DPanel.Players.Kick",
                () => kick(command),
                DispatchTimeout,
                cancellationToken);
        }

        private static KickPlayerActionResult CaptureAndKick(KickPlayerCommand command)
        {
            var clients = global::ConnectionManager.Instance?.Clients?.List;
            if (clients == null)
                return KickPlayerActionResult.PlayerNotOnline();

            global::ClientInfo? matchedClient = null;
            var snapshots = new List<PlayerConnectionSnapshot>();
            foreach (var client in clients)
            {
                if (client == null || client.entityId != command.EntityId)
                    continue;

                if (matchedClient != null)
                    throw new InvalidOperationException("Multiple game clients share the requested entity id.");

                matchedClient = client;
                var platformIdentity = client.PlatformId;
                snapshots.Add(new PlayerConnectionSnapshot(
                    client.entityId,
                    client.playerName ?? string.Empty,
                    platformIdentity?.CombinedString ?? string.Empty,
                    platformIdentity?.PlatformIdentifierString ?? string.Empty,
                    client));
            }

            return ResolveAndKick(
                snapshots,
                command,
                (handle, kickData) => global::GameUtils.KickPlayerForClientInfo(
                    (global::ClientInfo)handle,
                    CreateKickData(kickData)));
        }

        internal static KickPlayerActionResult ResolveAndKick(
            IReadOnlyCollection<PlayerConnectionSnapshot> connections,
            KickPlayerCommand command,
            Action<object, KickDataSnapshot> nativeKick)
        {
            if (nativeKick == null) throw new ArgumentNullException(nameof(nativeKick));

            var result = ResolveTarget(connections, command);
            if (result.Status != KickPlayerActionStatus.Succeeded)
                return result;

            object? handle = null;
            foreach (var connection in connections)
            {
                if (connection.EntityId == command.EntityId)
                {
                    handle = connection.Handle;
                    break;
                }
            }

            if (handle == null)
                throw new InvalidOperationException("The matched game client handle is unavailable.");

            nativeKick(handle, CreateKickDataSnapshot(command.Reason));
            return result;
        }

        internal static KickPlayerActionResult ResolveTarget(
            IReadOnlyCollection<PlayerConnectionSnapshot> connections,
            KickPlayerCommand command)
        {
            if (connections == null) throw new ArgumentNullException(nameof(connections));
            if (command == null) throw new ArgumentNullException(nameof(command));

            PlayerConnectionSnapshot? matched = null;
            foreach (var connection in connections)
            {
                if (connection.EntityId != command.EntityId)
                    continue;

                if (matched != null)
                    throw new InvalidOperationException("Multiple game clients share the requested entity id.");

                matched = connection;
            }

            if (matched == null)
                return KickPlayerActionResult.PlayerNotOnline();

            if (!string.Equals(
                    matched.CombinedId,
                    command.ExpectedPlatformIdentity.CombinedId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    matched.Platform,
                    command.ExpectedPlatformIdentity.Platform,
                    StringComparison.Ordinal))
            {
                var changedIdentity = string.IsNullOrWhiteSpace(matched.CombinedId) ||
                    string.IsNullOrWhiteSpace(matched.Platform)
                    ? command.ExpectedPlatformIdentity
                    : new PlayerPlatformIdentity(matched.CombinedId, matched.Platform);
                return KickPlayerActionResult.PlayerIdentityChanged(
                    matched.EntityId,
                    matched.Name,
                    changedIdentity);
            }

            var currentIdentity = new PlayerPlatformIdentity(
                matched.CombinedId,
                matched.Platform);
            return KickPlayerActionResult.Succeeded(
                matched.EntityId,
                matched.Name,
                currentIdentity);
        }

        internal static KickDataSnapshot CreateKickDataSnapshot(string reason)
        {
            return new KickDataSnapshot(
                "ManualKick",
                0,
                default(DateTime),
                reason);
        }

        private static global::GameUtils.KickPlayerData CreateKickData(KickDataSnapshot snapshot)
        {
            return new global::GameUtils.KickPlayerData(
                global::GameUtils.EKickReason.ManualKick,
                snapshot.ApiResponseEnum,
                snapshot.BanUntil,
                snapshot.CustomReason);
        }

        internal sealed class KickDataSnapshot
        {
            public KickDataSnapshot(
                string reason,
                int apiResponseEnum,
                DateTime banUntil,
                string customReason)
            {
                Reason = reason;
                ApiResponseEnum = apiResponseEnum;
                BanUntil = banUntil;
                CustomReason = customReason;
            }

            public string Reason { get; }

            public int ApiResponseEnum { get; }

            public DateTime BanUntil { get; }

            public string CustomReason { get; }
        }

        internal sealed class PlayerConnectionSnapshot
        {
            public PlayerConnectionSnapshot(
                int entityId,
                string name,
                string combinedId,
                string platform,
                object? handle = null)
            {
                EntityId = entityId;
                Name = name;
                CombinedId = combinedId;
                Platform = platform;
                Handle = handle;
            }

            public int EntityId { get; }

            public string Name { get; }

            public string CombinedId { get; }

            public string Platform { get; }

            public object? Handle { get; }
        }
    }
}