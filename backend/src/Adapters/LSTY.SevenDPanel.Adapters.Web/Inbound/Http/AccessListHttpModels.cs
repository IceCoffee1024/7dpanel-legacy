using System;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class BanUpsertHttpRequest
    {
        public string? DisplayName { get; set; }
        public DateTimeOffset? BannedUntilUtc { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class WhitelistUpsertHttpRequest
    {
        public string? DisplayName { get; set; }
    }

    public sealed class BanEntryHttpResponse
    {
        public BanEntryHttpResponse(BanEntry entry)
        {
            PlayerId = entry.PlayerId;
            DisplayName = entry.DisplayName;
            BannedUntilUtc = entry.BannedUntilUtc;
            Reason = entry.Reason;
        }

        public string PlayerId { get; }
        public string DisplayName { get; }
        public DateTimeOffset? BannedUntilUtc { get; }
        public string? Reason { get; }
    }

    public sealed class WhitelistEntryHttpResponse
    {
        public WhitelistEntryHttpResponse(WhitelistEntry entry)
        {
            PlayerId = entry.PlayerId;
            DisplayName = entry.DisplayName;
        }

        public string PlayerId { get; }
        public string DisplayName { get; }
    }
}
