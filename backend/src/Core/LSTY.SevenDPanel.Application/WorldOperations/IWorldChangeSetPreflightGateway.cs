using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public interface IWorldChangeSetPreflightGateway
    {
        Task<WorldChangeSetRuntimeHashResult> ReadCurrentRegionHashAsync(
            WorldChangeSetDescriptor descriptor,
            CancellationToken cancellationToken);
    }

    public sealed record WorldChangeSetRuntimeHashResult(
        string? CurrentRegionHash,
        string? ErrorCode)
    {
        public static WorldChangeSetRuntimeHashResult Available(string currentRegionHash) =>
            new WorldChangeSetRuntimeHashResult(
                WorldChangeSetValidation.RequireHash(
                    currentRegionHash,
                    nameof(currentRegionHash)),
                null);

        public static WorldChangeSetRuntimeHashResult Unavailable(string errorCode)
        {
            if (string.IsNullOrWhiteSpace(errorCode))
                throw new ArgumentException("An error code is required.", nameof(errorCode));
            return new WorldChangeSetRuntimeHashResult(null, errorCode);
        }
    }

    public sealed record UndoWorldChangeSetPreflight(
        string SourceOperationId,
        string ChangeSetId,
        string WorldId,
        string WorldVersion,
        string AfterHash,
        string? CurrentRegionHash,
        bool? CurrentHashMatches,
        string Status);
}

