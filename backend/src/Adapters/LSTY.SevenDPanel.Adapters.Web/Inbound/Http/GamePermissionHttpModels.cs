using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class GameAdminUpsertHttpRequest
    {
        public string? DisplayName { get; set; }
        public int PermissionLevel { get; set; }
    }

    public sealed class CommandPermissionUpsertHttpRequest
    {
        public int PermissionLevel { get; set; }
    }

    public sealed class GameAdminHttpResponse
    {
        public GameAdminHttpResponse(GameAdminEntry entry)
        {
            PlayerId = entry.PlayerId;
            DisplayName = entry.DisplayName;
            PermissionLevel = entry.PermissionLevel;
        }
        public string PlayerId { get; }
        public string DisplayName { get; }
        public int PermissionLevel { get; }
    }

    public sealed class CommandPermissionHttpResponse
    {
        public CommandPermissionHttpResponse(CommandPermissionEntry entry)
        {
            Command = entry.Command;
            PermissionLevel = entry.PermissionLevel;
            Description = entry.Description;
        }
        public string Command { get; }
        public int PermissionLevel { get; }
        public string? Description { get; }
    }
}
