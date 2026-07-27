using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Discord;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteDiscordIntegrationStore :
        IDiscordIntegrationStore,
        IDiscordInteractionPersistenceStore,
        IDiscordIntegrationAdministrationStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteDiscordIntegrationStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public DiscordIntegrationSettings? GetSettings()
        {
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<SettingsRow>(
                @"SELECT version, enabled, mode, application_id, guild_id,
                         public_channel_id, bridge_game_to_discord,
                         bridge_discord_to_game, proxy_enabled, proxy_uri, updated_utc
                  FROM discord_settings WHERE singleton_id = 1;");
            return row == null ? null : Map(row);
        }

        public void SaveSettings(DiscordIntegrationSettings settings, long expectedVersion)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (expectedVersion < 0 || settings.Version != expectedVersion + 1)
                throw new ArgumentOutOfRangeException(nameof(expectedVersion));
            RequireUtc(settings.UpdatedAtUtc, nameof(settings));
            using var connection = connectionFactory.Open();
            var parameters = new
            {
                settings.Version,
                Enabled = settings.IsEnabled ? 1 : 0,
                Mode = settings.Mode.ToString(),
                settings.ApplicationId,
                settings.GuildId,
                settings.PublicChannelId,
                BridgeGameToDiscord = settings.BridgeGameToDiscord ? 1 : 0,
                BridgeDiscordToGame = settings.BridgeDiscordToGame ? 1 : 0,
                ProxyEnabled = settings.ProxyEnabled ? 1 : 0,
                settings.ProxyUri,
                UpdatedUtc = Milliseconds(settings.UpdatedAtUtc),
                ExpectedVersion = expectedVersion
            };
            var affected = expectedVersion == 0
                ? connection.Execute(
                    @"INSERT INTO discord_settings (
                          singleton_id, version, enabled, mode, application_id, guild_id,
                          public_channel_id, bridge_game_to_discord, bridge_discord_to_game,
                          proxy_enabled, proxy_uri, updated_utc)
                      VALUES (
                          1, @Version, @Enabled, @Mode, @ApplicationId, @GuildId,
                          @PublicChannelId, @BridgeGameToDiscord, @BridgeDiscordToGame,
                          @ProxyEnabled, @ProxyUri, @UpdatedUtc)
                      ON CONFLICT(singleton_id) DO NOTHING;",
                    parameters)
                : connection.Execute(
                    @"UPDATE discord_settings
                      SET version = @Version,
                          enabled = @Enabled,
                          mode = @Mode,
                          application_id = @ApplicationId,
                          guild_id = @GuildId,
                          public_channel_id = @PublicChannelId,
                          bridge_game_to_discord = @BridgeGameToDiscord,
                          bridge_discord_to_game = @BridgeDiscordToGame,
                          proxy_enabled = @ProxyEnabled,
                          proxy_uri = @ProxyUri,
                          updated_utc = @UpdatedUtc
                      WHERE singleton_id = 1 AND version = @ExpectedVersion;",
                    parameters);
            if (affected != 1) throw new DiscordIntegrationVersionConflictException();
        }

        public void SetSecret(DiscordSecretValue secret)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));
            RequireText(secret.SecretKey, nameof(secret));
            RequireText(secret.SecretValue, nameof(secret));
            RequireText(secret.Fingerprint, nameof(secret));
            RequireUtc(secret.UpdatedAtUtc, nameof(secret));
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO discord_secrets (
                      secret_key, secret_value, fingerprint, updated_utc)
                  VALUES (@SecretKey, @SecretValue, @Fingerprint, @UpdatedUtc)
                  ON CONFLICT(secret_key) DO UPDATE SET
                      secret_value = excluded.secret_value,
                      fingerprint = excluded.fingerprint,
                      updated_utc = excluded.updated_utc;",
                new
                {
                    secret.SecretKey,
                    secret.SecretValue,
                    secret.Fingerprint,
                    UpdatedUtc = Milliseconds(secret.UpdatedAtUtc)
                });
        }

        public void DeleteSecret(string secretKey)
        {
            RequireText(secretKey, nameof(secretKey));
            using var connection = connectionFactory.Open();
            connection.Execute(
                "DELETE FROM discord_secrets WHERE secret_key = @SecretKey;",
                new { SecretKey = secretKey });
        }

        public DiscordSecretValue? GetSecret(string secretKey)
        {
            RequireText(secretKey, nameof(secretKey));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<SecretRow>(
                @"SELECT secret_key, secret_value, fingerprint, updated_utc
                  FROM discord_secrets WHERE secret_key = @SecretKey;",
                new { SecretKey = secretKey });
            return row == null
                ? null
                : new DiscordSecretValue(
                    row.secret_key,
                    row.secret_value,
                    row.fingerprint,
                    Utc(row.updated_utc));
        }

        public IReadOnlyList<DiscordSecretMetadata> ListSecretMetadata()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<SecretMetadataRow>(
                    @"SELECT secret_key, fingerprint, updated_utc
                      FROM discord_secrets ORDER BY secret_key;")
                .Select(row => new DiscordSecretMetadata(
                    row.secret_key,
                    row.fingerprint,
                    Utc(row.updated_utc)))
                .ToArray();
        }

        public void SaveTarget(DiscordTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            RequireText(target.TargetKey, nameof(target));
            RequireText(target.DeliveryMode, nameof(target));
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO discord_targets (target_key, delivery_mode, channel_id, enabled)
                  VALUES (@TargetKey, @DeliveryMode, @ChannelId, @Enabled)
                  ON CONFLICT(target_key) DO UPDATE SET
                      delivery_mode = excluded.delivery_mode,
                      channel_id = excluded.channel_id,
                      enabled = excluded.enabled;",
                new
                {
                    target.TargetKey,
                    target.DeliveryMode,
                    target.ChannelId,
                    Enabled = target.IsEnabled ? 1 : 0
                });
        }

        public IReadOnlyList<DiscordTarget> ListTargets()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<TargetRow>(
                    @"SELECT target_key, delivery_mode, channel_id, enabled
                      FROM discord_targets ORDER BY target_key;")
                .Select(row => new DiscordTarget(
                    row.target_key,
                    row.delivery_mode,
                    row.channel_id,
                    row.enabled != 0))
                .ToArray();
        }

        public DiscordTarget? FindTarget(string targetKey)
        {
            RequireText(targetKey, nameof(targetKey));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<TargetRow>(
                @"SELECT target_key, delivery_mode, channel_id, enabled
                  FROM discord_targets WHERE target_key = @TargetKey;",
                new { TargetKey = targetKey });
            return row == null
                ? null
                : new DiscordTarget(
                    row.target_key,
                    row.delivery_mode,
                    row.channel_id,
                    row.enabled != 0);
        }

        public void SaveCommandSetting(DiscordCommandSetting command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            RequireText(command.CommandKey, nameof(command));
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO discord_command_settings (command_key, enabled, remote_allowed)
                  VALUES (@CommandKey, @Enabled, @RemoteAllowed)
                  ON CONFLICT(command_key) DO UPDATE SET
                      enabled = excluded.enabled,
                      remote_allowed = excluded.remote_allowed;",
                new
                {
                    command.CommandKey,
                    Enabled = command.IsEnabled ? 1 : 0,
                    RemoteAllowed = command.RemoteAllowed ? 1 : 0
                });
        }

        public IReadOnlyList<DiscordCommandSetting> ListCommandSettings()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<CommandRow>(
                    @"SELECT command_key, enabled, remote_allowed
                      FROM discord_command_settings ORDER BY command_key;")
                .Select(row => new DiscordCommandSetting(
                    row.command_key,
                    row.enabled != 0,
                    row.remote_allowed != 0))
                .ToArray();
        }

        public DiscordDeliveryEnqueueResult EnqueueDelivery(DiscordDelivery delivery)
        {
            if (delivery == null) throw new ArgumentNullException(nameof(delivery));
            RequireText(delivery.DeliveryId, nameof(delivery));
            RequireText(delivery.BusinessKey, nameof(delivery));
            RequireText(delivery.TargetKey, nameof(delivery));
            RequireText(delivery.ContentSummary, nameof(delivery));
            RequireUtc(delivery.CreatedAtUtc, nameof(delivery));
            if (delivery.Status != DiscordDeliveryStatus.Pending ||
                delivery.ContentText == null ||
                delivery.ContentText.Length < 1 ||
                delivery.ContentText.Length > 2000 ||
                delivery.RetryCount != 0 ||
                delivery.CompletedAtUtc.HasValue)
                throw new DiscordDeliveryValidationException();
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                var existing = connection.QuerySingleOrDefault<DeliveryRow>(
                    @"SELECT delivery_id, business_key, target_key, status, content_text,
                             content_summary, next_attempt_utc, retry_count,
                             created_utc, completed_utc
                      FROM discord_deliveries WHERE business_key = @BusinessKey;",
                    new { delivery.BusinessKey });
                if (existing != null)
                    return new DiscordDeliveryEnqueueResult(Map(existing), false);
                if (connection.ExecuteScalar<int?>(
                        "SELECT enabled FROM discord_settings WHERE singleton_id = 1;") != 1)
                    throw new DiscordIntegrationDisabledException();
                if (connection.ExecuteScalar<int?>(
                        @"SELECT enabled FROM discord_targets
                          WHERE target_key = @TargetKey;",
                        new { delivery.TargetKey }) != 1)
                    throw new DiscordDeliveryValidationException();
                var inserted = connection.Execute(
                    @"INSERT INTO discord_deliveries (
                          delivery_id, business_key, target_key, status, content_text,
                          content_summary, next_attempt_utc, retry_count,
                          created_utc, completed_utc)
                      VALUES (
                          @DeliveryId, @BusinessKey, @TargetKey, @Status, @ContentText,
                          @ContentSummary, @NextAttemptUtc, @RetryCount,
                          @CreatedUtc, @CompletedUtc)
                      ON CONFLICT(business_key) DO NOTHING;",
                    DeliveryParameters(delivery));
                var row = connection.QuerySingle<DeliveryRow>(
                    @"SELECT delivery_id, business_key, target_key, status, content_text,
                             content_summary, next_attempt_utc, retry_count,
                             created_utc, completed_utc
                      FROM discord_deliveries WHERE business_key = @BusinessKey;",
                    new { delivery.BusinessKey });
                return new DiscordDeliveryEnqueueResult(Map(row), inserted == 1);
            });
        }

        public void BeginDeliveryAttempt(
            string deliveryId,
            int attemptNumber,
            DateTimeOffset startedAtUtc)
        {
            RequireText(deliveryId, nameof(deliveryId));
            if (attemptNumber <= 0) throw new ArgumentOutOfRangeException(nameof(attemptNumber));
            RequireUtc(startedAtUtc, nameof(startedAtUtc));
            using var connection = connectionFactory.Open();
            ExecuteImmediate(connection, () =>
            {
                var affected = connection.Execute(
                    @"UPDATE discord_deliveries
                      SET status = 'Sending',
                          next_attempt_utc = NULL,
                          retry_count = CASE
                              WHEN retry_count < @RetryCount THEN @RetryCount
                              ELSE retry_count
                          END,
                          completed_utc = NULL
                      WHERE delivery_id = @DeliveryId
                        AND status IN ('Pending', 'RetryScheduled', 'ResultUnknown');",
                    new
                    {
                        DeliveryId = deliveryId,
                        RetryCount = attemptNumber - 1
                    });
                if (affected != 1)
                    throw new InvalidOperationException("discord_delivery_not_attemptable");
                connection.Execute(
                    @"INSERT INTO discord_delivery_attempts (
                          delivery_id, attempt_no, status, started_utc)
                      VALUES (@DeliveryId, @AttemptNumber, 'Sending', @StartedUtc);",
                    new
                    {
                        DeliveryId = deliveryId,
                        AttemptNumber = attemptNumber,
                        StartedUtc = Milliseconds(startedAtUtc)
                    });
            });
        }

        public DiscordDeliveryWorkItem? TryClaimNextDeliveryAttempt(DateTimeOffset claimedAtUtc)
        {
            RequireUtc(claimedAtUtc, nameof(claimedAtUtc));
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                var claimedUtc = Milliseconds(claimedAtUtc);
                var candidate = connection.QuerySingleOrDefault<DeliveryRow>(
                    @"SELECT delivery_id, business_key, target_key, status, content_text,
                             content_summary, next_attempt_utc, retry_count,
                             created_utc, completed_utc
                      FROM discord_deliveries
                      WHERE status = 'Pending'
                         OR (status = 'RetryScheduled' AND next_attempt_utc <= @ClaimedUtc)
                      ORDER BY CASE status WHEN 'Pending' THEN created_utc ELSE next_attempt_utc END,
                               delivery_id
                      LIMIT 1;",
                    new { ClaimedUtc = claimedUtc });
                if (candidate == null) return null;

                var attemptNumber = connection.ExecuteScalar<int>(
                    @"SELECT COALESCE(MAX(attempt_no), 0) + 1
                      FROM discord_delivery_attempts WHERE delivery_id = @DeliveryId;",
                    new { DeliveryId = candidate.delivery_id });
                var affected = connection.Execute(
                    @"UPDATE discord_deliveries
                      SET status = 'Sending', next_attempt_utc = NULL, completed_utc = NULL
                      WHERE delivery_id = @DeliveryId AND status = @ExpectedStatus;",
                    new
                    {
                        DeliveryId = candidate.delivery_id,
                        ExpectedStatus = candidate.status
                    });
                if (affected != 1)
                    throw new InvalidOperationException("discord_delivery_claim_conflict");
                connection.Execute(
                    @"INSERT INTO discord_delivery_attempts (
                          delivery_id, attempt_no, status, started_utc)
                      VALUES (@DeliveryId, @AttemptNumber, 'Sending', @StartedUtc);",
                    new
                    {
                        DeliveryId = candidate.delivery_id,
                        AttemptNumber = attemptNumber,
                        StartedUtc = claimedUtc
                    });
                var claimed = connection.QuerySingle<DeliveryRow>(
                    @"SELECT delivery_id, business_key, target_key, status, content_text,
                             content_summary, next_attempt_utc, retry_count,
                             created_utc, completed_utc
                      FROM discord_deliveries WHERE delivery_id = @DeliveryId;",
                    new { DeliveryId = candidate.delivery_id });
                return new DiscordDeliveryWorkItem(Map(claimed), attemptNumber);
            });
        }

        public void CompleteDeliveryAttempt(
            string deliveryId,
            int attemptNumber,
            DiscordDeliveryStatus finalStatus,
            DateTimeOffset completedAtUtc,
            string? errorCode,
            DateTimeOffset? nextAttemptAtUtc)
        {
            RequireText(deliveryId, nameof(deliveryId));
            if (attemptNumber <= 0) throw new ArgumentOutOfRangeException(nameof(attemptNumber));
            if (finalStatus == DiscordDeliveryStatus.Pending ||
                finalStatus == DiscordDeliveryStatus.Sending)
                throw new ArgumentOutOfRangeException(nameof(finalStatus));
            if ((finalStatus == DiscordDeliveryStatus.RetryScheduled) != nextAttemptAtUtc.HasValue)
                throw new ArgumentException("Only retry-scheduled deliveries have a next attempt.", nameof(nextAttemptAtUtc));
            RequireUtc(completedAtUtc, nameof(completedAtUtc));
            if (nextAttemptAtUtc.HasValue) RequireUtc(nextAttemptAtUtc.Value, nameof(nextAttemptAtUtc));
            var attemptStatus = finalStatus == DiscordDeliveryStatus.RetryScheduled
                ? DiscordDeliveryStatus.Failed
                : finalStatus;
            using var connection = connectionFactory.Open();
            ExecuteImmediate(connection, () =>
            {
                var attemptAffected = connection.Execute(
                    @"UPDATE discord_delivery_attempts
                      SET status = @AttemptStatus,
                          completed_utc = @CompletedUtc,
                          error_code = @ErrorCode
                      WHERE delivery_id = @DeliveryId AND attempt_no = @AttemptNumber
                        AND status = 'Sending';",
                    new
                    {
                        DeliveryId = deliveryId,
                        AttemptNumber = attemptNumber,
                        AttemptStatus = attemptStatus.ToString(),
                        CompletedUtc = Milliseconds(completedAtUtc),
                        ErrorCode = errorCode
                    });
                if (attemptAffected != 1)
                    throw new InvalidOperationException("discord_delivery_attempt_not_sending");
                var deliveryAffected = connection.Execute(
                    @"UPDATE discord_deliveries
                      SET status = @FinalStatus,
                          next_attempt_utc = @NextAttemptUtc,
                          retry_count = CASE
                              WHEN @FinalStatus = 'RetryScheduled' THEN retry_count + 1
                              ELSE retry_count
                          END,
                          completed_utc = @DeliveryCompletedUtc,
                          content_text = CASE
                              WHEN @FinalStatus = 'RetryScheduled' THEN content_text
                              ELSE NULL
                          END
                      WHERE delivery_id = @DeliveryId AND status = 'Sending';",
                    new
                    {
                        DeliveryId = deliveryId,
                        FinalStatus = finalStatus.ToString(),
                        NextAttemptUtc = NullableMilliseconds(nextAttemptAtUtc),
                        DeliveryCompletedUtc = finalStatus == DiscordDeliveryStatus.RetryScheduled
                            ? (long?)null
                            : Milliseconds(completedAtUtc)
                    });
                if (deliveryAffected != 1)
                    throw new InvalidOperationException("discord_delivery_not_sending");
            });
        }

        public int RecoverSendingAsResultUnknown(DateTimeOffset recoveredAtUtc)
        {
            RequireUtc(recoveredAtUtc, nameof(recoveredAtUtc));
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                var recoveredUtc = Milliseconds(recoveredAtUtc);
                connection.Execute(
                    @"UPDATE discord_delivery_attempts
                      SET status = 'ResultUnknown',
                          completed_utc = CASE
                              WHEN started_utc > @RecoveredUtc THEN started_utc
                              ELSE @RecoveredUtc
                          END,
                          error_code = COALESCE(error_code, 'discord_restart_result_unknown')
                      WHERE status = 'Sending';",
                    new { RecoveredUtc = recoveredUtc });
                return connection.Execute(
                    @"UPDATE discord_deliveries
                      SET status = 'ResultUnknown',
                          completed_utc = CASE
                              WHEN created_utc > @RecoveredUtc THEN created_utc
                              ELSE @RecoveredUtc
                          END,
                          next_attempt_utc = NULL, content_text = NULL
                      WHERE status = 'Sending';",
                    new { RecoveredUtc = recoveredUtc });
            });
        }

        public DiscordDelivery? FindDelivery(string deliveryId)
        {
            RequireText(deliveryId, nameof(deliveryId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<DeliveryRow>(
                @"SELECT delivery_id, business_key, target_key, status, content_text,
                         content_summary, next_attempt_utc, retry_count,
                         created_utc, completed_utc
                  FROM discord_deliveries WHERE delivery_id = @DeliveryId;",
                new { DeliveryId = deliveryId });
            return row == null ? null : Map(row);
        }

        public IReadOnlyList<DiscordDelivery> ListDeliveries(int take)
        {
            if (take < 1 || take > 200) throw new ArgumentOutOfRangeException(nameof(take));
            using var connection = connectionFactory.Open();
            return connection.Query<DeliveryRow>(
                    @"SELECT delivery_id, business_key, target_key, status, content_text,
                             content_summary, next_attempt_utc, retry_count,
                             created_utc, completed_utc
                      FROM discord_deliveries
                      ORDER BY created_utc DESC, delivery_id DESC LIMIT @Take;",
                    new { Take = take })
                .Select(Map)
                .ToArray();
        }

        public IReadOnlyList<DiscordDeliveryAttempt> ListDeliveryAttempts(string deliveryId)
        {
            RequireText(deliveryId, nameof(deliveryId));
            using var connection = connectionFactory.Open();
            return connection.Query<AttemptRow>(
                    @"SELECT delivery_id, attempt_no, status, started_utc,
                             completed_utc, error_code
                      FROM discord_delivery_attempts
                      WHERE delivery_id = @DeliveryId ORDER BY attempt_no;",
                    new { DeliveryId = deliveryId })
                .Select(row => new DiscordDeliveryAttempt(
                    row.delivery_id,
                    row.attempt_no,
                    ParseStatus(row.status),
                    Utc(row.started_utc),
                    NullableUtc(row.completed_utc),
                    row.error_code))
                .ToArray();
        }

        public DiscordDelivery ScheduleManualRetry(
            string deliveryId,
            string contentText,
            DateTimeOffset scheduledAtUtc)
        {
            RequireText(deliveryId, nameof(deliveryId));
            if (contentText == null || contentText.Length < 1 || contentText.Length > 2000)
                throw new DiscordDeliveryValidationException();
            RequireUtc(scheduledAtUtc, nameof(scheduledAtUtc));
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                if (connection.ExecuteScalar<int?>(
                        "SELECT enabled FROM discord_settings WHERE singleton_id = 1;") != 1)
                    throw new DiscordIntegrationDisabledException();
                var affected = connection.Execute(
                    @"UPDATE discord_deliveries
                      SET status = 'RetryScheduled', content_text = @ContentText,
                          next_attempt_utc = @ScheduledUtc, retry_count = 0,
                          completed_utc = NULL
                      WHERE delivery_id = @DeliveryId
                        AND status IN ('Failed', 'ResultUnknown', 'Cancelled');",
                    new
                    {
                        DeliveryId = deliveryId,
                        ContentText = contentText,
                        ScheduledUtc = Milliseconds(scheduledAtUtc)
                    });
                if (affected != 1)
                    throw new InvalidOperationException("discord_delivery_not_retryable");
                return Map(connection.QuerySingle<DeliveryRow>(
                    @"SELECT delivery_id, business_key, target_key, status, content_text,
                             content_summary, next_attempt_utc, retry_count,
                             created_utc, completed_utc
                      FROM discord_deliveries WHERE delivery_id = @DeliveryId;",
                    new { DeliveryId = deliveryId }));
            });
        }

        public bool CancelDelivery(string deliveryId, DateTimeOffset cancelledAtUtc)
        {
            RequireText(deliveryId, nameof(deliveryId));
            RequireUtc(cancelledAtUtc, nameof(cancelledAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE discord_deliveries
                  SET status = 'Cancelled', content_text = NULL,
                      next_attempt_utc = NULL, completed_utc = @CancelledUtc
                  WHERE delivery_id = @DeliveryId
                    AND status IN ('Pending', 'RetryScheduled');",
                new
                {
                    DeliveryId = deliveryId,
                    CancelledUtc = Milliseconds(cancelledAtUtc)
                }) == 1;
        }

        public void SaveBindingCode(DiscordBindingCode code)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));
            RequireText(code.CodeId, nameof(code));
            RequireText(code.CrossplatformId, nameof(code));
            RequireText(code.CodePrefix, nameof(code));
            if (code.CodeHash == null || code.CodeHash.Length == 0)
                throw new ArgumentException("A code hash is required.", nameof(code));
            RequireUtc(code.ExpiresAtUtc, nameof(code));
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO discord_binding_codes (
                      code_id, crossplatform_id, code_prefix, code_hash, expires_utc)
                  VALUES (@CodeId, @CrossplatformId, @CodePrefix, @CodeHash, @ExpiresUtc);",
                new
                {
                    code.CodeId,
                    code.CrossplatformId,
                    code.CodePrefix,
                    CodeHash = code.CodeHash.ToArray(),
                    ExpiresUtc = Milliseconds(code.ExpiresAtUtc)
                });
        }

        public DiscordBinding? TryConsumeBindingCode(
            byte[] codeHash,
            string discordSubject,
            DateTimeOffset consumedAtUtc)
        {
            if (codeHash == null || codeHash.Length == 0)
                throw new ArgumentException("A code hash is required.", nameof(codeHash));
            RequireText(discordSubject, nameof(discordSubject));
            RequireUtc(consumedAtUtc, nameof(consumedAtUtc));
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                var consumedUtc = Milliseconds(consumedAtUtc);
                var code = connection.QuerySingleOrDefault<BindingCodeRow>(
                    @"SELECT code_id, crossplatform_id
                      FROM discord_binding_codes
                      WHERE code_hash = @CodeHash AND consumed_utc IS NULL
                        AND expires_utc > @ConsumedUtc;",
                    new { CodeHash = codeHash, ConsumedUtc = consumedUtc });
                if (code == null) return null;
                var consumed = connection.Execute(
                    @"UPDATE discord_binding_codes
                      SET consumed_utc = @ConsumedUtc
                      WHERE code_id = @CodeId AND consumed_utc IS NULL
                        AND expires_utc > @ConsumedUtc;",
                    new { CodeId = code.code_id, ConsumedUtc = consumedUtc });
                if (consumed != 1) return null;
                connection.Execute(
                    @"INSERT INTO discord_bindings (
                          discord_subject, crossplatform_id, active, created_utc, updated_utc)
                      VALUES (@DiscordSubject, @CrossplatformId, 1, @ConsumedUtc, @ConsumedUtc)
                      ON CONFLICT(discord_subject) DO UPDATE SET
                          crossplatform_id = excluded.crossplatform_id,
                          active = 1,
                          updated_utc = excluded.updated_utc;",
                    new
                    {
                        DiscordSubject = discordSubject,
                        CrossplatformId = code.crossplatform_id,
                        ConsumedUtc = consumedUtc
                    });
                var row = connection.QuerySingle<BindingRow>(
                    @"SELECT discord_subject, crossplatform_id, active,
                             created_utc, updated_utc
                      FROM discord_bindings WHERE discord_subject = @DiscordSubject;",
                    new { DiscordSubject = discordSubject });
                return new DiscordBinding(
                    row.discord_subject,
                    row.crossplatform_id,
                    row.active != 0,
                    Utc(row.created_utc),
                    Utc(row.updated_utc));
            });
        }

        public DiscordBinding? FindBinding(string discordSubject)
        {
            RequireText(discordSubject, nameof(discordSubject));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<BindingRow>(
                @"SELECT discord_subject, crossplatform_id, active,
                         created_utc, updated_utc
                  FROM discord_bindings WHERE discord_subject = @DiscordSubject;",
                new { DiscordSubject = discordSubject });
            return row == null
                ? null
                : new DiscordBinding(
                    row.discord_subject,
                    row.crossplatform_id,
                    row.active != 0,
                    Utc(row.created_utc),
                    Utc(row.updated_utc));
        }

        public IReadOnlyList<DiscordBinding> ListBindings()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<BindingRow>(
                    @"SELECT discord_subject, crossplatform_id, active,
                             created_utc, updated_utc
                      FROM discord_bindings
                      ORDER BY updated_utc DESC, discord_subject ASC;")
                .Select(row => new DiscordBinding(
                    row.discord_subject,
                    row.crossplatform_id,
                    row.active != 0,
                    Utc(row.created_utc),
                    Utc(row.updated_utc)))
                .ToArray();
        }

        public bool DisableBinding(string discordSubject, DateTimeOffset disabledAtUtc)
        {
            RequireText(discordSubject, nameof(discordSubject));
            RequireUtc(disabledAtUtc, nameof(disabledAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE discord_bindings
                  SET active = 0, updated_utc = @UpdatedUtc
                  WHERE discord_subject = @DiscordSubject AND active = 1;",
                new
                {
                    DiscordSubject = discordSubject,
                    UpdatedUtc = Milliseconds(disabledAtUtc)
                }) == 1;
        }

        public bool TryRegisterInteraction(DiscordInteraction interaction)
        {
            if (interaction == null) throw new ArgumentNullException(nameof(interaction));
            RequireText(interaction.InteractionId, nameof(interaction));
            RequireText(interaction.CommandKey, nameof(interaction));
            if (!string.Equals(
                    interaction.Status,
                    DiscordInteractionStatuses.Pending,
                    StringComparison.Ordinal) ||
                interaction.CompletedAtUtc.HasValue)
                throw new ArgumentException("discord_interaction_invalid", nameof(interaction));
            RequireUtc(interaction.ExpiresAtUtc, nameof(interaction));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"INSERT INTO discord_interactions (
                      interaction_id, command_key, status, expires_utc, completed_utc)
                  VALUES (@InteractionId, @CommandKey, @Status, @ExpiresUtc, NULL)
                  ON CONFLICT(interaction_id) DO NOTHING;",
                new
                {
                    interaction.InteractionId,
                    interaction.CommandKey,
                    interaction.Status,
                    ExpiresUtc = Milliseconds(interaction.ExpiresAtUtc)
                }) == 1;
        }

        public void CompleteInteraction(
            string interactionId,
            string status,
            DateTimeOffset completedAtUtc)
        {
            RequireText(interactionId, nameof(interactionId));
            RequireText(status, nameof(status));
            if (!DiscordInteractionStatuses.IsTerminal(status))
                throw new ArgumentException("discord_interaction_status_invalid", nameof(status));
            RequireUtc(completedAtUtc, nameof(completedAtUtc));
            using var connection = connectionFactory.Open();
            ExecuteImmediate(connection, () =>
            {
                var affected = connection.Execute(
                    @"UPDATE discord_interactions
                      SET status = @Status, completed_utc = @CompletedUtc
                      WHERE interaction_id = @InteractionId
                        AND status IN ('Pending', 'Running');",
                    new
                    {
                        InteractionId = interactionId,
                        Status = status,
                        CompletedUtc = Milliseconds(completedAtUtc)
                    });
                if (affected != 1)
                    throw new InvalidOperationException("discord_interaction_not_pending");
                connection.Execute(
                    "DELETE FROM discord_interaction_secrets WHERE interaction_id = @InteractionId;",
                    new { InteractionId = interactionId });
            });
        }

        public void SaveInteractionWithToken(DiscordInteraction interaction, string tokenValue) =>
            TrySaveInteractionWithToken(interaction, tokenValue);

        public bool TrySaveInteractionWithToken(DiscordInteraction interaction, string tokenValue)
        {
            if (interaction == null) throw new ArgumentNullException(nameof(interaction));
            RequireText(interaction.InteractionId, nameof(interaction));
            RequireText(interaction.CommandKey, nameof(interaction));
            RequireText(tokenValue, nameof(tokenValue));
            if (!string.Equals(
                    interaction.Status,
                    DiscordInteractionStatuses.Pending,
                    StringComparison.Ordinal) ||
                interaction.CompletedAtUtc.HasValue)
                throw new ArgumentException("discord_interaction_invalid", nameof(interaction));
            RequireUtc(interaction.ExpiresAtUtc, nameof(interaction));
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                var inserted = connection.Execute(
                    @"INSERT INTO discord_interactions (
                          interaction_id, command_key, status, expires_utc, completed_utc,
                          guild_id, channel_id, discord_subject, binding_code_hash)
                      VALUES (
                          @InteractionId, @CommandKey, @Status, @ExpiresUtc, NULL,
                          @GuildId, @ChannelId, @DiscordSubject, @BindingCodeHash)
                      ON CONFLICT(interaction_id) DO NOTHING;",
                    new
                    {
                        interaction.InteractionId,
                        interaction.CommandKey,
                        interaction.Status,
                        ExpiresUtc = Milliseconds(interaction.ExpiresAtUtc),
                        interaction.GuildId,
                        interaction.ChannelId,
                        interaction.DiscordSubject,
                        BindingCodeHash = interaction.BindingCodeHash?.ToArray()
                    });
                if (inserted != 1) return false;
                connection.Execute(
                    @"INSERT INTO discord_interaction_secrets (
                          interaction_id, token_value, expires_utc)
                      VALUES (@InteractionId, @TokenValue, @ExpiresUtc)
                      ON CONFLICT(interaction_id) DO UPDATE SET
                          token_value = excluded.token_value,
                          expires_utc = excluded.expires_utc;",
                    new
                    {
                        interaction.InteractionId,
                        TokenValue = tokenValue,
                        ExpiresUtc = Milliseconds(interaction.ExpiresAtUtc)
                    });
                return true;
            });
        }

        public DiscordInteraction? TryClaimNextInteraction(DateTimeOffset claimedAtUtc)
        {
            RequireUtc(claimedAtUtc, nameof(claimedAtUtc));
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                var claimedUtc = Milliseconds(claimedAtUtc);
                ExpireInteractions(connection, claimedUtc);
                var candidate = connection.QuerySingleOrDefault<InteractionRow>(
                    @"SELECT interaction_id, command_key, status, expires_utc, completed_utc,
                             guild_id, channel_id, discord_subject, binding_code_hash
                      FROM discord_interactions
                      WHERE status = 'Pending' AND expires_utc > @ClaimedUtc
                        AND discord_subject IS NOT NULL
                      ORDER BY expires_utc ASC, interaction_id ASC
                      LIMIT 1;",
                    new { ClaimedUtc = claimedUtc });
                if (candidate == null) return null;
                var affected = connection.Execute(
                    @"UPDATE discord_interactions
                      SET status = 'Running', completed_utc = NULL
                      WHERE interaction_id = @InteractionId
                        AND status = 'Pending' AND expires_utc > @ClaimedUtc;",
                    new { InteractionId = candidate.interaction_id, ClaimedUtc = claimedUtc });
                if (affected != 1)
                    throw new InvalidOperationException("discord_interaction_claim_conflict");
                candidate.status = DiscordInteractionStatuses.Running;
                candidate.completed_utc = null;
                return Map(candidate);
            });
        }

        public int RecoverRunningInteractions(DateTimeOffset recoveredAtUtc)
        {
            RequireUtc(recoveredAtUtc, nameof(recoveredAtUtc));
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                var recoveredUtc = Milliseconds(recoveredAtUtc);
                ExpireInteractions(connection, recoveredUtc);
                var recoveredInteractionIds = connection.Query<string>(
                    @"SELECT interaction_id
                      FROM discord_interactions
                      WHERE status = 'Running' AND expires_utc > @RecoveredUtc;",
                    new { RecoveredUtc = recoveredUtc })
                    .ToArray();
                if (recoveredInteractionIds.Length == 0) return 0;

                var recovered = connection.Execute(
                    @"UPDATE discord_interactions
                      SET status = @Status, completed_utc = @RecoveredUtc
                      WHERE interaction_id IN @InteractionIds
                        AND status = 'Running' AND expires_utc > @RecoveredUtc;",
                    new
                    {
                        Status = DiscordInteractionStatuses.ResultUnknown,
                        RecoveredUtc = recoveredUtc,
                        InteractionIds = recoveredInteractionIds
                    });
                connection.Execute(
                    @"DELETE FROM discord_interaction_secrets
                      WHERE interaction_id IN @InteractionIds;",
                    new { InteractionIds = recoveredInteractionIds });
                return recovered;
            });
        }

        public DiscordInteractionToken? GetInteractionToken(
            string interactionId,
            DateTimeOffset observedAtUtc)
        {
            RequireText(interactionId, nameof(interactionId));
            RequireUtc(observedAtUtc, nameof(observedAtUtc));
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                var observedUtc = Milliseconds(observedAtUtc);
                connection.Execute(
                    @"DELETE FROM discord_interaction_secrets
                      WHERE interaction_id = @InteractionId
                        AND (expires_utc <= @ObservedUtc OR NOT EXISTS (
                            SELECT 1 FROM discord_interactions
                            WHERE discord_interactions.interaction_id = @InteractionId
                              AND status IN ('Pending', 'Running')));",
                    new { InteractionId = interactionId, ObservedUtc = observedUtc });
                var row = connection.QuerySingleOrDefault<InteractionSecretRow>(
                    @"SELECT secret.interaction_id, secret.token_value, secret.expires_utc
                      FROM discord_interaction_secrets AS secret
                      INNER JOIN discord_interactions AS interaction
                          ON interaction.interaction_id = secret.interaction_id
                      WHERE secret.interaction_id = @InteractionId
                        AND secret.expires_utc > @ObservedUtc
                        AND interaction.status IN ('Pending', 'Running');",
                    new { InteractionId = interactionId, ObservedUtc = observedUtc });
                return row == null
                    ? null
                    : new DiscordInteractionToken(
                        row.interaction_id,
                        row.token_value,
                        Utc(row.expires_utc));
            });
        }

        public int ClearExpiredInteractionTokens(DateTimeOffset observedAtUtc)
        {
            RequireUtc(observedAtUtc, nameof(observedAtUtc));
            using var connection = connectionFactory.Open();
            return ExecuteImmediate(connection, () =>
            {
                var observedUtc = Milliseconds(observedAtUtc);
                var deleted = connection.Execute(
                    "DELETE FROM discord_interaction_secrets WHERE expires_utc <= @ObservedUtc;",
                    new { ObservedUtc = observedUtc });
                ExpireInteractions(connection, observedUtc);
                return deleted;
            });
        }

        public bool TryRegisterBridgeMessage(
            string bridgeMessageId,
            string source,
            string sourceMessageId,
            DateTimeOffset expiresAtUtc)
        {
            RequireText(bridgeMessageId, nameof(bridgeMessageId));
            RequireText(source, nameof(source));
            RequireText(sourceMessageId, nameof(sourceMessageId));
            RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"INSERT INTO discord_bridge_messages (
                      bridge_message_id, source, source_message_id, expires_utc)
                  VALUES (@BridgeMessageId, @Source, @SourceMessageId, @ExpiresUtc)
                  ON CONFLICT(source, source_message_id) DO NOTHING;",
                new
                {
                    BridgeMessageId = bridgeMessageId,
                    Source = source,
                    SourceMessageId = sourceMessageId,
                    ExpiresUtc = Milliseconds(expiresAtUtc)
                }) == 1;
        }

        private static object DeliveryParameters(DiscordDelivery delivery) => new
        {
            delivery.DeliveryId,
            delivery.BusinessKey,
            delivery.TargetKey,
            Status = delivery.Status.ToString(),
            delivery.ContentText,
            delivery.ContentSummary,
            NextAttemptUtc = delivery.Status == DiscordDeliveryStatus.RetryScheduled
                ? NullableMilliseconds(delivery.NextAttemptAtUtc)
                : null,
            delivery.RetryCount,
            CreatedUtc = Milliseconds(delivery.CreatedAtUtc),
            CompletedUtc = NullableMilliseconds(delivery.CompletedAtUtc)
        };

        private static DiscordIntegrationSettings Map(SettingsRow row) => new(
            row.version,
            row.enabled != 0,
            Enum.TryParse<DiscordIntegrationMode>(row.mode, out var mode)
                ? mode
                : throw new InvalidOperationException("discord_mode_invalid"),
            row.application_id,
            row.guild_id,
            row.public_channel_id,
            row.bridge_game_to_discord != 0,
            row.bridge_discord_to_game != 0,
            row.proxy_enabled != 0,
            row.proxy_uri,
            Utc(row.updated_utc));

        private static DiscordDelivery Map(DeliveryRow row) => new(
            row.delivery_id,
            row.business_key,
            row.target_key,
            ParseStatus(row.status),
            row.content_text,
            row.content_summary,
            NullableUtc(row.next_attempt_utc),
            row.retry_count,
            Utc(row.created_utc),
            NullableUtc(row.completed_utc));

        private static DiscordDeliveryStatus ParseStatus(string status) =>
            Enum.TryParse<DiscordDeliveryStatus>(status, out var parsed)
                ? parsed
                : throw new InvalidOperationException("discord_delivery_status_invalid");

        private static DiscordInteraction Map(InteractionRow row) => new(
            row.interaction_id,
            row.command_key,
            row.status,
            Utc(row.expires_utc),
            NullableUtc(row.completed_utc),
            row.guild_id,
            row.channel_id,
            row.discord_subject,
            row.binding_code_hash?.ToArray());

        private static void ExpireInteractions(SqliteConnection connection, long observedUtc)
        {
            connection.Execute(
                @"DELETE FROM discord_interaction_secrets WHERE expires_utc <= @ObservedUtc;",
                new { ObservedUtc = observedUtc });
            connection.Execute(
                @"UPDATE discord_interactions
                  SET status = 'Expired', completed_utc = @ObservedUtc
                  WHERE status IN ('Pending', 'Running') AND expires_utc <= @ObservedUtc;",
                new { ObservedUtc = observedUtc });
        }

        private static T ExecuteImmediate<T>(SqliteConnection connection, Func<T> action)
        {
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var result = action();
                connection.Execute("COMMIT;");
                return result;
            }
            catch
            {
                connection.Execute("ROLLBACK;");
                throw;
            }
        }

        private static void ExecuteImmediate(SqliteConnection connection, Action action) =>
            ExecuteImmediate(connection, () =>
            {
                action();
                return true;
            });

        private static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A value is required.", parameterName);
        }

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }

        private static long Milliseconds(DateTimeOffset value)
        {
            RequireUtc(value, nameof(value));
            return value.ToUnixTimeMilliseconds();
        }

        private static long? NullableMilliseconds(DateTimeOffset? value) =>
            value.HasValue ? Milliseconds(value.Value) : (long?)null;

        private static DateTimeOffset Utc(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);

        private static DateTimeOffset? NullableUtc(long? value) =>
            value.HasValue ? Utc(value.Value) : (DateTimeOffset?)null;

        private sealed class SettingsRow
        {
            public long version { get; set; }
            public long enabled { get; set; }
            public string mode { get; set; } = string.Empty;
            public string? application_id { get; set; }
            public string? guild_id { get; set; }
            public string? public_channel_id { get; set; }
            public long bridge_game_to_discord { get; set; }
            public long bridge_discord_to_game { get; set; }
            public long proxy_enabled { get; set; }
            public string? proxy_uri { get; set; }
            public long updated_utc { get; set; }
        }

        private sealed class SecretRow
        {
            public string secret_key { get; set; } = string.Empty;
            public string secret_value { get; set; } = string.Empty;
            public string fingerprint { get; set; } = string.Empty;
            public long updated_utc { get; set; }
        }

        private sealed class SecretMetadataRow
        {
            public string secret_key { get; set; } = string.Empty;
            public string fingerprint { get; set; } = string.Empty;
            public long updated_utc { get; set; }
        }

        private sealed class TargetRow
        {
            public string target_key { get; set; } = string.Empty;
            public string delivery_mode { get; set; } = string.Empty;
            public string? channel_id { get; set; }
            public long enabled { get; set; }
        }

        private sealed class CommandRow
        {
            public string command_key { get; set; } = string.Empty;
            public long enabled { get; set; }
            public long remote_allowed { get; set; }
        }

        private sealed class DeliveryRow
        {
            public string delivery_id { get; set; } = string.Empty;
            public string business_key { get; set; } = string.Empty;
            public string target_key { get; set; } = string.Empty;
            public string status { get; set; } = string.Empty;
            public string? content_text { get; set; }
            public string content_summary { get; set; } = string.Empty;
            public long? next_attempt_utc { get; set; }
            public int retry_count { get; set; }
            public long created_utc { get; set; }
            public long? completed_utc { get; set; }
        }

        private sealed class AttemptRow
        {
            public string delivery_id { get; set; } = string.Empty;
            public int attempt_no { get; set; }
            public string status { get; set; } = string.Empty;
            public long started_utc { get; set; }
            public long? completed_utc { get; set; }
            public string? error_code { get; set; }
        }

        private sealed class BindingCodeRow
        {
            public string code_id { get; set; } = string.Empty;
            public string crossplatform_id { get; set; } = string.Empty;
        }

        private sealed class BindingRow
        {
            public string discord_subject { get; set; } = string.Empty;
            public string crossplatform_id { get; set; } = string.Empty;
            public long active { get; set; }
            public long created_utc { get; set; }
            public long updated_utc { get; set; }
        }

        private sealed class InteractionSecretRow
        {
            public string interaction_id { get; set; } = string.Empty;
            public string token_value { get; set; } = string.Empty;
            public long expires_utc { get; set; }
        }

        private sealed class InteractionRow
        {
            public string interaction_id { get; set; } = string.Empty;
            public string command_key { get; set; } = string.Empty;
            public string status { get; set; } = string.Empty;
            public long expires_utc { get; set; }
            public long? completed_utc { get; set; }
            public string? guild_id { get; set; }
            public string? channel_id { get; set; }
            public string? discord_subject { get; set; }
            public byte[]? binding_code_hash { get; set; }
        }
    }
}
