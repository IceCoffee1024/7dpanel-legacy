namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public interface IWorldChangeSetMetadataStore
    {
        WorldChangeSetDescriptor Create(WorldChangeSetDraft draft);
        WorldChangeSetDescriptor Read(string changeSetId);
        void MarkApplied(string changeSetId, string afterHash);
    }
}
