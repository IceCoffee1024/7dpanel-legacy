using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Diagnostics
{
    public enum ClientInfoBoundaryProbeStatus
    {
        Skipped,
        Failed,
        Passed
    }

    public sealed class ClientInfoBoundaryProbeResult
    {
        internal ClientInfoBoundaryProbeResult(
            string probeName,
            ClientInfoBoundaryProbeStatus status,
            string code,
            string? detail)
        {
            ProbeName = probeName;
            Status = status;
            Code = code;
            Detail = detail;
        }

        public string ProbeName { get; }

        public ClientInfoBoundaryProbeStatus Status { get; }

        public string Code { get; }

        public string? Detail { get; }
    }

    public sealed class ClientInfoBoundaryProbe
    {
        public const string IdentityProbeName = "identity";
        public const string CurrentPositionProbeName = "current_position";
        public const string PrivateReplyProbeName = "private_reply";
        private static readonly TimeSpan GameThreadStartTimeout = TimeSpan.FromSeconds(3);

        private readonly string stableIdentity;
        private readonly Func<
            string,
            Func<ClientInfoBoundaryProbeResult>,
            TimeSpan,
            CancellationToken,
            Task<ClientInfoBoundaryProbeResult>> dispatch;
        private readonly Func<string, ClientInfoIdentitySnapshot?> readIdentity;
        private readonly Func<string, ClientInfoPositionSnapshot?> readPosition;
        private readonly Func<string, string, bool> sendPrivateReply;

        public ClientInfoBoundaryProbe(string stableIdentity)
            : this(
                stableIdentity,
                (operation, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(operation, action, timeout, cancellationToken),
                ReadNativeIdentity,
                ReadNativePosition,
                SendNativePrivateReply)
        {
        }

        internal ClientInfoBoundaryProbe(
            string stableIdentity,
            Func<
                string,
                Func<ClientInfoBoundaryProbeResult>,
                TimeSpan,
                CancellationToken,
                Task<ClientInfoBoundaryProbeResult>> dispatch,
            Func<string, ClientInfoIdentitySnapshot?> readIdentity,
            Func<string, ClientInfoPositionSnapshot?> readPosition,
            Func<string, string, bool> sendPrivateReply)
        {
            if (string.IsNullOrWhiteSpace(stableIdentity))
                throw new ArgumentException("A stable player identity is required.", nameof(stableIdentity));

            this.stableIdentity = stableIdentity.Trim();
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.readIdentity = readIdentity ?? throw new ArgumentNullException(nameof(readIdentity));
            this.readPosition = readPosition ?? throw new ArgumentNullException(nameof(readPosition));
            this.sendPrivateReply = sendPrivateReply ?? throw new ArgumentNullException(nameof(sendPrivateReply));
        }

        public Task<ClientInfoBoundaryProbeResult> ProbeIdentityAsync(
            CancellationToken cancellationToken = default) =>
            DispatchAsync(
                "7dpanel-client-info-identity-probe",
                IdentityProbeName,
                ProbeIdentityOnGameThread,
                cancellationToken);

        public Task<ClientInfoBoundaryProbeResult> ProbeCurrentPositionAsync(
            CancellationToken cancellationToken = default) =>
            DispatchAsync(
                "7dpanel-client-info-position-probe",
                CurrentPositionProbeName,
                ProbeCurrentPositionOnGameThread,
                cancellationToken);

        public Task<ClientInfoBoundaryProbeResult> ProbePrivateReplyAsync(
            string message,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message))
                return Task.FromResult(Skipped(PrivateReplyProbeName, "empty_message"));

            return DispatchAsync(
                "7dpanel-client-info-private-reply-probe",
                PrivateReplyProbeName,
                () => ProbePrivateReplyOnGameThread(message),
                cancellationToken);
        }

        private ClientInfoBoundaryProbeResult ProbeIdentityOnGameThread()
        {
            try
            {
                var snapshot = readIdentity(stableIdentity);
                if (snapshot == null)
                    return Skipped(IdentityProbeName, "client_offline");

                var detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "entityId={0}; playerName={1}",
                    snapshot.EntityId,
                    snapshot.PlayerName ?? string.Empty);
                return Passed(IdentityProbeName, detail);
            }
            catch
            {
                return Failed(IdentityProbeName, "identity_read_failed");
            }
        }

        private ClientInfoBoundaryProbeResult ProbeCurrentPositionOnGameThread()
        {
            try
            {
                var snapshot = readPosition(stableIdentity);
                if (snapshot == null)
                    return Skipped(CurrentPositionProbeName, "client_or_player_unavailable");

                var detail = string.Format(
                    CultureInfo.InvariantCulture,
                    "x={0:R}; y={1:R}; z={2:R}",
                    snapshot.X,
                    snapshot.Y,
                    snapshot.Z);
                return Passed(CurrentPositionProbeName, detail);
            }
            catch
            {
                return Failed(CurrentPositionProbeName, "position_read_failed");
            }
        }

        private ClientInfoBoundaryProbeResult ProbePrivateReplyOnGameThread(string message)
        {
            try
            {
                return sendPrivateReply(stableIdentity, message)
                    ? Passed(PrivateReplyProbeName, null)
                    : Skipped(PrivateReplyProbeName, "client_offline");
            }
            catch
            {
                return Failed(PrivateReplyProbeName, "private_reply_failed");
            }
        }

        internal static T? FindPreferredIdentityMatch<T>(
            IEnumerable<T> candidates,
            string stableIdentity,
            Func<T, string?> crossplatformId,
            Func<T, string?> platformId,
            Func<T, bool> isAvailable)
            where T : class
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (stableIdentity == null) throw new ArgumentNullException(nameof(stableIdentity));
            if (crossplatformId == null) throw new ArgumentNullException(nameof(crossplatformId));
            if (platformId == null) throw new ArgumentNullException(nameof(platformId));
            if (isAvailable == null) throw new ArgumentNullException(nameof(isAvailable));

            var current = candidates.Where(isAvailable).ToList();
            var crossplatformMatches = current.Where(candidate => string.Equals(
                crossplatformId(candidate), stableIdentity, StringComparison.Ordinal)).ToList();
            if (crossplatformMatches.Count != 0)
                return crossplatformMatches.Count == 1 ? crossplatformMatches[0] : null;

            var platformMatches = current.Where(candidate => string.Equals(
                platformId(candidate), stableIdentity, StringComparison.Ordinal)).ToList();
            return platformMatches.Count == 1 ? platformMatches[0] : null;
        }

        private static global::ClientInfo? ResolveOnlineClient(string stableIdentity)
        {
            var clients = global::ConnectionManager.Instance?.Clients?.List;
            if (clients == null) return null;

            return FindPreferredIdentityMatch(
                clients,
                stableIdentity,
                client => client.CrossplatformId?.CombinedString,
                client => client.PlatformId?.CombinedString,
                client => !client.disconnecting);
        }

        private async Task<ClientInfoBoundaryProbeResult> DispatchAsync(
            string operationName,
            string probeName,
            Func<ClientInfoBoundaryProbeResult> action,
            CancellationToken cancellationToken)
        {
            try
            {
                return await dispatch(
                        operationName,
                        action,
                        GameThreadStartTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Skipped(probeName, "probe_cancelled");
            }
            catch
            {
                return Failed(probeName, "game_thread_dispatch_failed");
            }
        }

        private static ClientInfoIdentitySnapshot? ReadNativeIdentity(string stableIdentity)
        {
            var client = ResolveOnlineClient(stableIdentity);
            return client == null
                ? null
                : new ClientInfoIdentitySnapshot(
                    client.entityId,
                    client.playerName,
                    client.CrossplatformId?.CombinedString,
                    client.PlatformId?.CombinedString);
        }

        private static ClientInfoPositionSnapshot? ReadNativePosition(string stableIdentity)
        {
            var client = ResolveOnlineClient(stableIdentity);
            var world = global::GameManager.Instance?.World;
            if (client == null || world == null ||
                !world.Players.dict.TryGetValue(client.entityId, out var player) || player == null)
            {
                return null;
            }

            var position = player.GetPosition();
            return new ClientInfoPositionSnapshot(position.x, position.y, position.z);
        }

        private static bool SendNativePrivateReply(string stableIdentity, string message)
        {
            var client = ResolveOnlineClient(stableIdentity);
            if (client == null) return false;

            new SevenDaysGameChatCommandReplySender().Send(client, new[] { message });
            return true;
        }

        private static ClientInfoBoundaryProbeResult Passed(string probeName, string? detail) =>
            new ClientInfoBoundaryProbeResult(probeName, ClientInfoBoundaryProbeStatus.Passed, "passed", detail);

        private static ClientInfoBoundaryProbeResult Skipped(string probeName, string code) =>
            new ClientInfoBoundaryProbeResult(probeName, ClientInfoBoundaryProbeStatus.Skipped, code, null);

        private static ClientInfoBoundaryProbeResult Failed(string probeName, string code) =>
            new ClientInfoBoundaryProbeResult(probeName, ClientInfoBoundaryProbeStatus.Failed, code, null);
    }

    internal sealed class ClientInfoIdentitySnapshot
    {
        public ClientInfoIdentitySnapshot(
            int entityId,
            string? playerName,
            string? crossplatformId,
            string? platformId)
        {
            EntityId = entityId;
            PlayerName = playerName;
            CrossplatformId = crossplatformId;
            PlatformId = platformId;
        }

        public int EntityId { get; }
        public string? PlayerName { get; }
        public string? CrossplatformId { get; }
        public string? PlatformId { get; }
    }

    internal sealed class ClientInfoPositionSnapshot
    {
        public ClientInfoPositionSnapshot(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
    }
}
