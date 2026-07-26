using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GameAdminEntry
    {
        public GameAdminEntry(string playerId, string displayName, int permissionLevel)
        {
            PlayerId = playerId;
            DisplayName = displayName;
            PermissionLevel = permissionLevel;
        }

        public string PlayerId { get; }
        public string DisplayName { get; }
        public int PermissionLevel { get; }
    }

    public sealed class CommandPermissionEntry
    {
        public CommandPermissionEntry(string command, int permissionLevel, string? description)
        {
            Command = command;
            PermissionLevel = permissionLevel;
            Description = description;
        }

        public string Command { get; }
        public int PermissionLevel { get; }
        public string? Description { get; }
    }

    public sealed class CommandPermissionRequest
    {
        public CommandPermissionRequest(string command, int permissionLevel)
        {
            Command = command;
            PermissionLevel = permissionLevel;
        }

        public string Command { get; }
        public int PermissionLevel { get; }
    }

    public enum GamePermissionMutationStatus
    {
        Succeeded,
        Invalid,
        NotFound,
        Conflict,
        GameNotReady,
        NativeRejected,
        Unknown
    }

    public sealed class GamePermissionMutationResult
    {
        private GamePermissionMutationResult(GamePermissionMutationStatus status, string? reason)
        {
            Status = status;
            Reason = reason;
        }

        public GamePermissionMutationStatus Status { get; }
        public string? Reason { get; }

        public static GamePermissionMutationResult Succeeded() => new GamePermissionMutationResult(GamePermissionMutationStatus.Succeeded, null);
        public static GamePermissionMutationResult Invalid(string? reason = null) => new GamePermissionMutationResult(GamePermissionMutationStatus.Invalid, reason);
        public static GamePermissionMutationResult NotFound(string? reason = null) => new GamePermissionMutationResult(GamePermissionMutationStatus.NotFound, reason);
        public static GamePermissionMutationResult Conflict(string? reason = null) => new GamePermissionMutationResult(GamePermissionMutationStatus.Conflict, reason);
        public static GamePermissionMutationResult GameNotReady(string? reason = null) => new GamePermissionMutationResult(GamePermissionMutationStatus.GameNotReady, reason);
        public static GamePermissionMutationResult NativeRejected(string? reason = null) => new GamePermissionMutationResult(GamePermissionMutationStatus.NativeRejected, reason);
        public static GamePermissionMutationResult Unknown(string? reason = null) => new GamePermissionMutationResult(GamePermissionMutationStatus.Unknown, reason);
    }

    public sealed class GamePermissionGameNotReadyException : Exception
    {
        public GamePermissionGameNotReadyException() : base("The game permission service is not ready.") { }
    }
}
