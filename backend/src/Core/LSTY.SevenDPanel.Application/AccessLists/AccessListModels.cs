using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed record BanEntry(
        string PlayerId,
        string DisplayName,
        DateTimeOffset? BannedUntilUtc,
        string? Reason);

    public sealed record WhitelistEntry(string PlayerId, string DisplayName);

    public sealed record BanRequest(
        string PlayerId,
        string DisplayName,
        DateTimeOffset? BannedUntilUtc,
        string? Reason);

    public sealed record WhitelistRequest(string PlayerId, string DisplayName);

    public enum AccessListMutationStatus
    {
        Succeeded,
        NotFound,
        Conflict,
        GameNotReady,
        NativeRejected,
        Unknown
    }

    public sealed class AccessListMutationResult
    {
        private AccessListMutationResult(AccessListMutationStatus status, string? detail)
        {
            Status = status;
            Detail = detail;
        }

        public AccessListMutationStatus Status { get; }
        public string? Detail { get; }

        public static AccessListMutationResult Succeeded() =>
            new AccessListMutationResult(AccessListMutationStatus.Succeeded, null);

        public static AccessListMutationResult NotFound(string? detail = null) =>
            new AccessListMutationResult(AccessListMutationStatus.NotFound, detail);

        public static AccessListMutationResult Conflict(string? detail = null) =>
            new AccessListMutationResult(AccessListMutationStatus.Conflict, detail);

        public static AccessListMutationResult GameNotReady(string? detail = null) =>
            new AccessListMutationResult(AccessListMutationStatus.GameNotReady, detail);

        public static AccessListMutationResult NativeRejected(string? detail = null) =>
            new AccessListMutationResult(AccessListMutationStatus.NativeRejected, detail);

        public static AccessListMutationResult Unknown(string? detail = null) =>
            new AccessListMutationResult(AccessListMutationStatus.Unknown, detail);
    }

    public sealed class AccessListGameNotReadyException : Exception
    {
        public AccessListGameNotReadyException()
            : base("The game access-list service is not ready.")
        {
        }
    }
}
