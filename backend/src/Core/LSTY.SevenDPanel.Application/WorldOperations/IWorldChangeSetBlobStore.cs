namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public interface IWorldChangeSetBlobStore
    {
        WorldChangeSetBlobReceipt Write(WorldChangeSetBlobDraft draft);
        WorldChangeSetBlobReadResult Read(string storageResourceId, string expectedHash);
    }
}
