using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LSTY.SevenDPanel.Application.Chat;
using Newtonsoft.Json;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public class ChatMuteWriteRequest
    {
        public string? DisplayName { get; set; }
        [JsonProperty(Required = Required.Always)] public string? Reason { get; set; }
        public string? MutedUntilUtc { get; set; }
        public string? CorrelationId { get; set; }
        internal DateTimeOffset? ToMutedUntilUtc()
        {
            if (string.IsNullOrWhiteSpace(MutedUntilUtc)) return null;
            if (!DateTimeOffset.TryParse(MutedUntilUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) || parsed.Offset != TimeSpan.Zero)
                throw new ArgumentException("The mute deadline must be UTC.", nameof(MutedUntilUtc));
            return parsed;
        }
    }

    public sealed class CreateChatMuteRequest : ChatMuteWriteRequest
    {
        [JsonProperty(Required = Required.Always)] public string? CrossplatformId { get; set; }
    }

    public sealed class ChatMuteHttpResponse
    {
        public ChatMuteHttpResponse(ChatMuteRecord record)
        {
            CrossplatformId = record.CrossplatformId; DisplayName = record.DisplayName; Reason = record.Reason;
            MutedUntilUtc = record.MutedUntilUtc?.ToString("O", CultureInfo.InvariantCulture);
            CreatedBy = record.CreatedBy; CreatedAtUtc = record.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            UpdatedBy = record.UpdatedBy; UpdatedAtUtc = record.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        }
        public string CrossplatformId { get; }
        public string? DisplayName { get; }
        public string Reason { get; }
        public string? MutedUntilUtc { get; }
        public string CreatedBy { get; }
        public string CreatedAtUtc { get; }
        public string UpdatedBy { get; }
        public string UpdatedAtUtc { get; }
    }

    public sealed class ChatMutePageHttpResponse
    {
        public ChatMutePageHttpResponse(ChatMutePage page)
        {
            Mutes = page.Records.Select(record => new ChatMuteHttpResponse(record)).ToArray();
            NextCursorUpdatedAtUtc = page.NextCursor?.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            NextCursorCrossplatformId = page.NextCursor?.CrossplatformId;
        }
        public IReadOnlyList<ChatMuteHttpResponse> Mutes { get; }
        public string? NextCursorUpdatedAtUtc { get; }
        public string? NextCursorCrossplatformId { get; }
    }
}
