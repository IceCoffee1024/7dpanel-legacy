using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Newtonsoft.Json;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public class SendChatMessageRequest
    {
        [JsonProperty(Required = Required.Always)]
        public string? Message { get; set; }
    }

    public sealed class SendPrivateChatMessageRequest : SendChatMessageRequest
    {
        [JsonProperty(Required = Required.Always)]
        public string? TargetCrossplatformId { get; set; }
    }

    public sealed class ChatSendResponse
    {
        public ChatSendResponse(ChatSendStatus status) { Status = status.ToString(); }
        public string Status { get; }
    }

    public sealed class ChatMessageHttpResponse
    {
        public ChatMessageHttpResponse(ChatMessage message)
            : this(message.Sequence, message.OccurredAtUtc, message.EntityId,
                message.CrossplatformId, message.SenderName, message.Channel.ToString(),
                message.SourceKind.ToString(), message.Message) { }

        public ChatMessageHttpResponse(ChatMessageEventData message)
            : this(message.Sequence, message.OccurredAtUtc, message.EntityId,
                message.CrossplatformId, message.SenderName, message.Channel,
                message.SourceKind, message.Message) { }

        private ChatMessageHttpResponse(long sequence, DateTimeOffset occurredAtUtc, int entityId,
            string? crossplatformId, string senderName, string channel, string sourceKind, string message)
        {
            Sequence = sequence;
            OccurredAtUtc = occurredAtUtc.ToString("O", CultureInfo.InvariantCulture);
            EntityId = entityId;
            CrossplatformId = crossplatformId;
            SenderName = senderName;
            Channel = channel;
            SourceKind = sourceKind;
            Message = message;
        }

        public long Sequence { get; }
        public string OccurredAtUtc { get; }
        public int EntityId { get; }
        public string? CrossplatformId { get; }
        public string SenderName { get; }
        public string Channel { get; }
        public string SourceKind { get; }
        public string Message { get; }
    }

    public sealed class RecentChatMessagesResponse
    {
        public RecentChatMessagesResponse(IEnumerable<ChatMessageEventData> messages) =>
            Messages = messages.Select(message => new ChatMessageHttpResponse(message)).ToArray();
        public IReadOnlyList<ChatMessageHttpResponse> Messages { get; }
    }

    public sealed class ChatHistoryGapHttpResponse
    {
        public ChatHistoryGapHttpResponse(ChatHistoryGap gap)
        {
            StartedAtUtc = gap.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            EndedAtUtc = gap.EndedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            DroppedMessageCount = gap.DroppedMessageCount;
            Reason = gap.Reason;
        }
        public string StartedAtUtc { get; }
        public string EndedAtUtc { get; }
        public long DroppedMessageCount { get; }
        public string Reason { get; }
    }

    public sealed class ChatHistoryHttpResponse
    {
        public ChatHistoryHttpResponse(ChatHistoryPage page, string? nextCursor)
        {
            Messages = page.Messages.Select(message => new ChatMessageHttpResponse(message)).ToArray();
            Gaps = page.Gaps.Select(gap => new ChatHistoryGapHttpResponse(gap)).ToArray();
            NextCursor = nextCursor;
        }
        public IReadOnlyList<ChatMessageHttpResponse> Messages { get; }
        public IReadOnlyList<ChatHistoryGapHttpResponse> Gaps { get; }
        public string? NextCursor { get; }
    }

    public sealed class ChatSettingsHttpModel
    {
        public ChatSettingsHttpModel() { }
        public ChatSettingsHttpModel(ChatSettings settings)
        {
            IsEnabled = settings.IsEnabled;
            GlobalServerName = settings.GlobalServerName;
            WhisperServerName = settings.WhisperServerName;
            CommandPrefixes = settings.CommandPrefixes.ToArray();
            ExcludeCommandsFromHistory = settings.ExcludeCommandsFromHistory;
            HistoryRetentionDays = settings.HistoryRetentionDays;
        }
        [JsonProperty(Required = Required.Always)] public bool IsEnabled { get; set; }
        public string? GlobalServerName { get; set; }
        public string? WhisperServerName { get; set; }
        [JsonProperty(Required = Required.Always)] public IReadOnlyList<string>? CommandPrefixes { get; set; }
        [JsonProperty(Required = Required.Always)] public bool ExcludeCommandsFromHistory { get; set; }
        [JsonProperty(Required = Required.Always)] public int HistoryRetentionDays { get; set; }
        internal ChatSettings ToApplication() => new ChatSettings
        {
            IsEnabled = IsEnabled,
            GlobalServerName = GlobalServerName,
            WhisperServerName = WhisperServerName,
            CommandPrefixes = CommandPrefixes ?? Array.Empty<string>(),
            ExcludeCommandsFromHistory = ExcludeCommandsFromHistory,
            HistoryRetentionDays = HistoryRetentionDays
        };
    }

    public sealed class ColoredChatSettingsHttpModel
    {
        public ColoredChatSettingsHttpModel() { }
        public ColoredChatSettingsHttpModel(ColoredChatSettings settings)
        {
            IsEnabled = settings.IsEnabled;
            GlobalDefaultColor = settings.GlobalDefaultColor;
            WhisperDefaultColor = settings.WhisperDefaultColor;
            FriendsDefaultColor = settings.FriendsDefaultColor;
            PartyDefaultColor = settings.PartyDefaultColor;
            AdminDefaultColor = settings.AdminDefaultColor;
            SystemDefaultColor = settings.SystemDefaultColor;
            PlayerColorTagPermission = settings.PlayerColorTagPermission.ToString();
        }
        [JsonProperty(Required = Required.Always)] public bool IsEnabled { get; set; }
        public string? GlobalDefaultColor { get; set; }
        public string? WhisperDefaultColor { get; set; }
        public string? FriendsDefaultColor { get; set; }
        public string? PartyDefaultColor { get; set; }
        public string? AdminDefaultColor { get; set; }
        public string? SystemDefaultColor { get; set; }
        [JsonProperty(Required = Required.Always)] public string? PlayerColorTagPermission { get; set; }
        internal ColoredChatSettings ToApplication()
        {
            if (!Enum.TryParse(PlayerColorTagPermission, false, out PlayerColorTagPermission permission) ||
                !Enum.IsDefined(typeof(PlayerColorTagPermission), permission))
                throw new ArgumentException("The player color tag permission is invalid.");
            return new ColoredChatSettings
            {
                IsEnabled = IsEnabled,
                GlobalDefaultColor = GlobalDefaultColor,
                WhisperDefaultColor = WhisperDefaultColor,
                FriendsDefaultColor = FriendsDefaultColor,
                PartyDefaultColor = PartyDefaultColor,
                AdminDefaultColor = AdminDefaultColor,
                SystemDefaultColor = SystemDefaultColor,
                PlayerColorTagPermission = permission
            };
        }
    }

    public class ColoredChatProfileWriteRequest
    {
        public string? CustomName { get; set; }
        public string? NameColor { get; set; }
        public string? TextColor { get; set; }
        public string? Description { get; set; }
    }

    public sealed class CreateColoredChatProfileRequest : ColoredChatProfileWriteRequest
    {
        [JsonProperty(Required = Required.Always)]
        public string? CrossplatformId { get; set; }
    }

    public sealed class ColoredChatProfileHttpResponse
    {
        public ColoredChatProfileHttpResponse(ColoredChatProfile profile)
        {
            CrossplatformId = profile.CrossplatformId;
            CustomName = profile.CustomName;
            NameColor = profile.NameColor;
            TextColor = profile.TextColor;
            Description = profile.Description;
            CreatedAtUtc = profile.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            UpdatedAtUtc = profile.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        }
        public string CrossplatformId { get; }
        public string? CustomName { get; }
        public string? NameColor { get; }
        public string? TextColor { get; }
        public string? Description { get; }
        public string CreatedAtUtc { get; }
        public string UpdatedAtUtc { get; }
    }

    public sealed class ColoredChatProfilesHttpResponse
    {
        public ColoredChatProfilesHttpResponse(ColoredChatProfilePage page, string? nextCursor)
        {
            Profiles = page.Profiles.Select(profile => new ColoredChatProfileHttpResponse(profile)).ToArray();
            NextCursor = nextCursor;
        }
        public IReadOnlyList<ColoredChatProfileHttpResponse> Profiles { get; }
        public string? NextCursor { get; }
    }
}
