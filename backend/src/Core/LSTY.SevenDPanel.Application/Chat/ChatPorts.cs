using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.Chat
{
    public interface IChatHistoryStore
    {
        void Append(ChatMessage message);
        void AppendGap(ChatHistoryGap gap);
        ChatHistoryPage GetHistory(ChatHistoryQuery query);
        int DeleteBefore(DateTimeOffset cutoffUtc, int maximumDeletes);
    }

    public interface IChatSettingsStore
    {
        ChatSettings Get();
        ChatSettings Save(ChatSettings settings);
        ChatSettings Reset();
    }

    public interface IColoredChatStore
    {
        ColoredChatSettings GetSettings();
        ColoredChatSettings SaveSettings(ColoredChatSettings settings);
        ColoredChatSettings ResetSettings();
        ColoredChatProfilePage GetProfiles(ColoredChatProfileQuery query);
        IReadOnlyList<ColoredChatProfile> GetAllProfiles();
        bool TryCreateProfile(ColoredChatProfile profile);
        bool TryUpdateProfile(ColoredChatProfile profile);
        bool TryDeleteProfile(string crossplatformId);
    }

    public interface IChatMessageSender
    {
        Task<ChatSendResult> SendGlobalAsync(string message, CancellationToken cancellationToken);
        Task<ChatSendResult> SendPrivateAsync(
            string targetCrossplatformId,
            string message,
            CancellationToken cancellationToken);
    }

    public interface IChatRuntimeConfiguration
    {
        void ApplyChatSettings(ChatSettings settings);
        void ApplyColoredChatSettings(ColoredChatSettings settings);
        void UpsertProfile(ColoredChatProfile profile);
        void RemoveProfile(string crossplatformId);
    }

    public interface IChatMuteRuntimeConfiguration : IChatRuntimeConfiguration
    {
        void ReplaceMuteSnapshot(IReadOnlyDictionary<string, ChatMuteRecord> snapshot);
    }

    public interface IChatOperationAuditTrail
    {
        void Record(ChatOperationAuditEntry entry);
    }

    public enum ChatSendStatus
    {
        Accepted,
        Disabled,
        NotReady,
        QueueFull,
        TargetOffline,
        Cancelled,
        Unknown
    }

    public sealed class ChatSendResult
    {
        private ChatSendResult(ChatSendStatus status) => Status = status;
        public ChatSendStatus Status { get; }
        public static ChatSendResult Accepted() => new ChatSendResult(ChatSendStatus.Accepted);
        public static ChatSendResult Failed(ChatSendStatus status)
        {
            if (status == ChatSendStatus.Accepted)
                throw new ArgumentException("An accepted result must use Accepted().", nameof(status));
            if (!Enum.IsDefined(typeof(ChatSendStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            return new ChatSendResult(status);
        }
    }

    public enum ChatOperationKind
    {
        SendGlobal,
        SendPrivate,
        SaveSettings,
        ResetSettings,
        SaveColoredSettings,
        ResetColoredSettings,
        CreateProfile,
        UpdateProfile,
        DeleteProfile
    }

    public sealed class ChatOperationAuditEntry
    {
        internal ChatOperationAuditEntry(
            string actorSubject,
            ChatOperationKind operation,
            DateTimeOffset occurredAtUtc,
            string result,
            ChatChannel? channel,
            string? targetCrossplatformId,
            int? messageLength,
            string? businessKey,
            IReadOnlyList<string> changedFields)
        {
            ActorSubject = actorSubject;
            Operation = operation;
            OccurredAtUtc = occurredAtUtc;
            Result = result;
            Channel = channel;
            TargetCrossplatformId = targetCrossplatformId;
            MessageLength = messageLength;
            BusinessKey = businessKey;
            ChangedFields = changedFields;
        }

        public string ActorSubject { get; }
        public ChatOperationKind Operation { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public string Result { get; }
        public ChatChannel? Channel { get; }
        public string? TargetCrossplatformId { get; }
        public int? MessageLength { get; }
        public string? BusinessKey { get; }
        public IReadOnlyList<string> ChangedFields { get; }
    }

    public sealed class ColoredChatProfileConflictException : Exception
    {
        public ColoredChatProfileConflictException()
            : base("A colored chat profile already exists for this identity.") { }
    }

    public sealed class ColoredChatProfileNotFoundException : Exception
    {
        public ColoredChatProfileNotFoundException()
            : base("The colored chat profile does not exist.") { }
    }
}
