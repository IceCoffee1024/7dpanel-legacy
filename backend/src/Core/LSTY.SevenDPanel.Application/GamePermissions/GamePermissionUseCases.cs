using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GamePermissionUseCases
    {
        private readonly IGamePermissionControl control;
        private readonly IRecentActivityWriter activityWriter;

        public GamePermissionUseCases(IGamePermissionControl control, IRecentActivityWriter activityWriter)
        {
            this.control = control ?? throw new ArgumentNullException(nameof(control));
            this.activityWriter = activityWriter ?? throw new ArgumentNullException(nameof(activityWriter));
        }

        public Task<IReadOnlyList<GameAdminEntry>> GetAdminsAsync(CancellationToken cancellationToken) =>
            control.GetAdminsAsync(cancellationToken);

        public Task<IReadOnlyList<CommandPermissionEntry>> GetCommandsAsync(CancellationToken cancellationToken) =>
            control.GetCommandsAsync(cancellationToken);

        public async Task<GamePermissionMutationResult> UpsertAdminAsync(
            string actorSubject,
            GameAdminEntry entry,
            CancellationToken cancellationToken)
        {
            ValidateActor(actorSubject);
            if (entry == null) return GamePermissionMutationResult.Invalid("missing_entry");
            var playerId = NormalizePlayerId(entry.PlayerId);
            var displayName = NormalizeDisplayName(entry.DisplayName);
            if (playerId == null || displayName == null || !IsValidLevel(entry.PermissionLevel))
                return GamePermissionMutationResult.Invalid("invalid_admin");

            var result = await control.UpsertAdminAsync(
                new GameAdminEntry(playerId, displayName, entry.PermissionLevel), cancellationToken).ConfigureAwait(false);
            await RecordAsync(actorSubject, "admin", "upsert", playerId, entry.PermissionLevel, result, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }

        public async Task<GamePermissionMutationResult> RemoveAdminAsync(
            string actorSubject,
            string playerId,
            CancellationToken cancellationToken)
        {
            ValidateActor(actorSubject);
            var normalized = NormalizePlayerId(playerId);
            if (normalized == null) return GamePermissionMutationResult.Invalid("invalid_player_id");
            var result = await control.RemoveAdminAsync(normalized, cancellationToken).ConfigureAwait(false);
            await RecordAsync(actorSubject, "admin", "remove", normalized, null, result, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }

        public async Task<GamePermissionMutationResult> UpsertCommandAsync(
            string actorSubject,
            CommandPermissionRequest request,
            CancellationToken cancellationToken)
        {
            ValidateActor(actorSubject);
            if (request == null) return GamePermissionMutationResult.Invalid("missing_request");
            var command = NormalizeCommand(request.Command);
            if (command == null || !IsValidLevel(request.PermissionLevel))
                return GamePermissionMutationResult.Invalid("invalid_command");
            var result = await control.UpsertCommandAsync(command, request.PermissionLevel, cancellationToken)
                .ConfigureAwait(false);
            await RecordAsync(actorSubject, "command", "upsert", command, request.PermissionLevel, result, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }

        public async Task<GamePermissionMutationResult> RemoveCommandAsync(
            string actorSubject,
            string command,
            CancellationToken cancellationToken)
        {
            ValidateActor(actorSubject);
            var normalized = NormalizeCommand(command);
            if (normalized == null) return GamePermissionMutationResult.Invalid("invalid_command");
            var result = await control.RemoveCommandAsync(normalized, cancellationToken).ConfigureAwait(false);
            await RecordAsync(actorSubject, "command", "remove", normalized, null, result, cancellationToken)
                .ConfigureAwait(false);
            return result;
        }

        private Task RecordAsync(
            string actorSubject,
            string targetType,
            string action,
            string target,
            int? level,
            GamePermissionMutationResult result,
            CancellationToken cancellationToken) =>
            activityWriter.RecordGamePermissionChangedAsync(
                actorSubject, targetType, action, target, level,
                result.Status.ToString().ToLowerInvariant(), DateTimeOffset.UtcNow, cancellationToken);

        private static bool IsValidLevel(int level) => level >= 0 && level <= 2000;

        private static string? NormalizePlayerId(string? playerId)
        {
            var normalized = playerId?.Trim();
            return normalized == null || normalized.Length == 0 || normalized.Length > 160 ? null : normalized;
        }

        private static string? NormalizeDisplayName(string? displayName)
        {
            var normalized = displayName?.Trim() ?? string.Empty;
            return normalized.Length > 80 ? null : normalized;
        }

        private static string? NormalizeCommand(string? command)
        {
            var normalized = command?.Trim();
            if (normalized == null || normalized.Length == 0 || normalized.Length > 128 || normalized.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
                return null;
            return normalized;
        }

        private static void ValidateActor(string actorSubject)
        {
            if (string.IsNullOrWhiteSpace(actorSubject))
                throw new ArgumentException("An actor subject is required.", nameof(actorSubject));
        }
    }

    public interface IGamePermissionActivityWriter
    {
        Task RecordGamePermissionChangedAsync(
            string actorSubject,
            string targetType,
            string action,
            string target,
            int? permissionLevel,
            string outcome,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken);
    }

    public static class GamePermissionActivityWriterExtensions
    {
        public static Task RecordGamePermissionChangedAsync(
            this IRecentActivityWriter writer,
            string actorSubject,
            string targetType,
            string action,
            string target,
            int? permissionLevel,
            string outcome,
            DateTimeOffset occurredAtUtc,
            CancellationToken cancellationToken)
        {
            return writer is IGamePermissionActivityWriter gamePermissionWriter
                ? gamePermissionWriter.RecordGamePermissionChangedAsync(
                    actorSubject, targetType, action, target, permissionLevel, outcome, occurredAtUtc, cancellationToken)
                : Task.CompletedTask;
        }
    }
}
