using LSTY.SevenDPanel.Application.Mods;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class SetModStateHttpRequest
    {
        public bool? Enabled { get; set; }
    }

    public sealed class ModHttpResponse
    {
        public ModHttpResponse(ModView mod)
        {
            DirectoryId = mod.DirectoryId;
            Name = mod.Name;
            DisplayName = mod.DisplayName;
            Author = mod.Author;
            Version = mod.Version;
            Website = mod.Website;
            Description = mod.Description;
            IsLoadedNow = mod.IsLoadedNow;
            IsEnabledNextStart = mod.IsEnabledNextStart;
            IsProtected = mod.IsProtected;
        }

        public string DirectoryId { get; }
        public string Name { get; }
        public string DisplayName { get; }
        public string Author { get; }
        public string Version { get; }
        public string? Website { get; }
        public string? Description { get; }
        public bool? IsLoadedNow { get; }
        public bool IsEnabledNextStart { get; }
        public bool IsProtected { get; }
    }

    public sealed class ModStateHttpResponse
    {
        public ModStateHttpResponse(string directoryId, bool enabledNextStart, string outcome)
        {
            DirectoryId = directoryId;
            IsEnabledNextStart = enabledNextStart;
            Outcome = outcome;
        }

        public string DirectoryId { get; }
        public bool IsEnabledNextStart { get; }
        public string Outcome { get; }
    }
}
