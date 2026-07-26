using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteRecentActivityStore : IRecentActivityQuery, IRecentActivityWriter, IServerGovernanceActivityWriter, IGamePermissionActivityWriter
    {
        private const int MaximumRecentActivityItems = 8;
        private const string NoMessageArguments = "{}";
        private readonly SqliteConnectionFactory connectionFactory;
        private readonly int retentionLimit;

        public SqliteRecentActivityStore(SqliteConnectionFactory connectionFactory, int retentionLimit)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
            if (retentionLimit < 1) throw new ArgumentOutOfRangeException(nameof(retentionLimit));

            this.retentionLimit = retentionLimit;
        }

        public Task RecordPanelLoginSucceededAsync(
            string actorSubject,
            string actorDisplayName,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            return RecordAsync("panel_login_succeeded", actorSubject, actorDisplayName, NoMessageArguments, occurredAtUtc, cancellationToken);
        }

        public Task RecordAccessListChangedAsync(
            string actorSubject,
            string list,
            string action,
            string playerId,
            string outcome,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(list) || string.IsNullOrWhiteSpace(action) ||
                string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(outcome))
                throw new ArgumentException("Access-list audit fields are required.");
            return RecordAsync(
                "access_list_changed",
                actorSubject,
                playerId,
                NoMessageArguments,
                occurredAtUtc,
                cancellationToken);
        }

        public Task RecordGamePermissionChangedAsync(
            string actorSubject,
            string targetType,
            string action,
            string target,
            int? permissionLevel,
            string outcome,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(action) ||
                string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(outcome))
                throw new ArgumentException("Game-permission audit fields are required.");
            return RecordAsync(
                "game_permission_changed",
                actorSubject,
                target,
                NoMessageArguments,
                occurredAtUtc,
                cancellationToken);
        }

        public Task RecordPlayerJoinedAsync(
            string playerDisplayName,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            return RecordPlayerAsync(
                "player_joined",
                playerDisplayName,
                occurredAtUtc,
                cancellationToken);
        }

        public Task RecordPlayerLeftAsync(
            string playerDisplayName,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            return RecordPlayerAsync(
                "player_left",
                playerDisplayName,
                occurredAtUtc,
                cancellationToken);
        }

        public Task RecordRestartScriptStartedAsync(
            string actorSubject,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            return RecordAsync("restart_script_started", actorSubject, null, NoMessageArguments, occurredAtUtc, cancellationToken);
        }

        public Task RecordShutdownRequestedAsync(
            string actorSubject,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            return RecordAsync("shutdown_requested", actorSubject, null, NoMessageArguments, occurredAtUtc, cancellationToken);
        }

        public Task RecordServerOperationFailedAsync(
            string actorSubject,
            string operationCode,
            string failureCode,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            if (!ServerOperationCodeContract.IsFailure(operationCode, failureCode))
            {
                throw new ArgumentException("The operation and failure codes must be approved activity codes.");
            }

            return RecordAsync(
                "server_operation_failed",
                actorSubject,
                null,
                CreateServerOperationFailedMessageArguments(operationCode, failureCode),
                occurredAtUtc,
                cancellationToken);
        }

        public Task<RecentActivitySnapshot> GetRecentActivityAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = connectionFactory.Open();
            var rows = connection.Query<ActivityRow>(
                @"SELECT occurred_utc AS OccurredUtc,
                         message_key AS MessageKey,
                         message_args AS MessageArguments,
                         CASE
                             WHEN message_key IN ('player_joined', 'player_left')
                                  AND json_valid(message_args) = 1
                                  AND json_type(message_args, '$.displayName') = 'text'
                             THEN json_extract(message_args, '$.displayName')
                             ELSE NULL
                         END AS PlayerDisplayName
                  FROM recent_activity
                  ORDER BY occurred_utc DESC, activity_id DESC
                  LIMIT @Limit;",
                new { Limit = MaximumRecentActivityItems });
            var items = new List<RecentActivityItem>();
            foreach (var row in rows)
            {
                items.Add(new RecentActivityItem(
                    ParseUtcText(row.OccurredUtc),
                    row.MessageKey,
                    GetMessageArguments(row.MessageKey, row.MessageArguments, row.PlayerDisplayName)));
            }

            var metadata = connection.QuerySingle<ActivityMetadataRow>(
                @"SELECT COUNT(*) AS TotalCount, MAX(occurred_utc) AS LatestOccurredUtc
                  FROM recent_activity;");

            return Task.FromResult(new RecentActivitySnapshot(
                AvailabilityState.Available,
                DateTimeOffset.UtcNow,
                metadata.TotalCount,
                metadata.LatestOccurredUtc == null ? null : ParseUtcText(metadata.LatestOccurredUtc),
                items));
        }

        private Task RecordAsync(
            string eventType,
            string? actorSubject,
            string? actorDisplayName,
            string messageArguments,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            return RecordAsync(
                eventType,
                actorSubject,
                actorDisplayName,
                messageArguments,
                null,
                occurredAtUtc,
                cancellationToken);
        }

        private Task RecordPlayerAsync(
            string eventType,
            string playerDisplayName,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            if (playerDisplayName == null) throw new ArgumentNullException(nameof(playerDisplayName));

            return RecordAsync(
                eventType,
                null,
                null,
                NoMessageArguments,
                playerDisplayName,
                occurredAtUtc,
                cancellationToken);
        }

        private Task RecordAsync(
            string eventType,
            string? actorSubject,
            string? actorDisplayName,
            string messageArguments,
            string? playerDisplayName,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(
                @"INSERT INTO recent_activity (
                      event_type, message_key, message_args, actor_subject, actor_display_name, occurred_utc)
                  VALUES (
                      @EventType,
                      @MessageKey,
                      CASE
                          WHEN @PlayerDisplayName IS NULL THEN json(@MessageArgs)
                          ELSE json_object('displayName', @PlayerDisplayName)
                      END,
                      @ActorSubject,
                      @ActorDisplayName,
                      @OccurredUtc);",
                new
                {
                    EventType = eventType,
                    MessageKey = eventType,
                    MessageArgs = messageArguments,
                    PlayerDisplayName = playerDisplayName,
                    ActorSubject = actorSubject,
                    ActorDisplayName = actorDisplayName,
                    OccurredUtc = ToUtcText(occurredAtUtc)
                },
                transaction);
            connection.Execute(
                @"DELETE FROM recent_activity
                  WHERE activity_id IN (
                      SELECT activity_id
                      FROM recent_activity
                      ORDER BY occurred_utc DESC, activity_id DESC
                      LIMIT -1 OFFSET @RetentionLimit
                  );",
                new { RetentionLimit = retentionLimit },
                transaction);
            transaction.Commit();
            return Task.CompletedTask;
        }

        private static IEnumerable<KeyValuePair<string, string>> GetMessageArguments(
            string messageKey,
            string messageArguments,
            string? playerDisplayName)
        {
            if ((string.Equals(messageKey, "player_joined", StringComparison.Ordinal) ||
                 string.Equals(messageKey, "player_left", StringComparison.Ordinal)) &&
                playerDisplayName != null)
            {
                return new[]
                {
                    new KeyValuePair<string, string>("displayName", playerDisplayName)
                };
            }

            if (string.Equals(messageKey, "server_operation_failed", StringComparison.Ordinal) &&
                TryReadServerOperationFailure(
                    messageArguments,
                    out var operationCode,
                    out var failureCode))
            {
                return new[]
                {
                    new KeyValuePair<string, string>("failureCode", failureCode),
                    new KeyValuePair<string, string>("operationCode", operationCode)
                };
            }

            return Enumerable.Empty<KeyValuePair<string, string>>();
        }

        private static string CreateServerOperationFailedMessageArguments(
            string operationCode,
            string failureCode)
        {
            return "{\"failureCode\":\"" + failureCode +
                   "\",\"operationCode\":\"" + operationCode + "\"}";
        }

        private static bool TryReadServerOperationFailure(
            string messageArguments,
            out string operationCode,
            out string failureCode)
        {
            foreach (var candidateOperation in ServerOperationCodeContract.OperationCodes)
            {
                foreach (var candidateFailure in
                         ServerOperationCodeContract.GetFailureCodes(candidateOperation))
                {
                    if (string.Equals(
                            messageArguments,
                            CreateServerOperationFailedMessageArguments(
                                candidateOperation,
                                candidateFailure),
                            StringComparison.Ordinal))
                    {
                        operationCode = candidateOperation;
                        failureCode = candidateFailure;
                        return true;
                    }
                }
            }

            operationCode = string.Empty;
            failureCode = string.Empty;
            return false;
        }

        private static string ToUtcText(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString(
                "yyyy-MM-ddTHH:mm:ss.fffffff'Z'",
                CultureInfo.InvariantCulture);
        }

        private static DateTimeOffset ParseUtcText(string value)
        {
            return DateTimeOffset.ParseExact(
                value,
                "yyyy-MM-ddTHH:mm:ss.fffffff'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        private sealed class ActivityRow
        {
            public string OccurredUtc { get; set; } = string.Empty;
            public string MessageKey { get; set; } = string.Empty;
            public string MessageArguments { get; set; } = string.Empty;
            public string? PlayerDisplayName { get; set; }
        }

        private sealed class ActivityMetadataRow
        {
            public int TotalCount { get; set; }
            public string? LatestOccurredUtc { get; set; }
        }
    }
}
