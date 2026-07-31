namespace LSTY.SevenDPanel.Application.Backups
{
    public interface IWorldRestoreRuntimeEvidenceSource
    {
        WorldRestoreRuntimeEvidence Capture();
    }

    public sealed record WorldRestoreRuntimeEvidence(
        bool IsMainThread,
        bool IsDedicatedServer,
        bool HasGameManager,
        bool IsWorldOpen,
        string? WorldName,
        string? WorldDirectory,
        string? GameVersion);
}

