using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.Chat
{
    public sealed class SendGlobalChatMessageUseCase
    {
        private readonly IChatMessageSender sender;
        private readonly IChatOperationAuditTrail auditTrail;
        private readonly Func<DateTimeOffset> utcClock;

        public SendGlobalChatMessageUseCase(IChatMessageSender sender, IChatOperationAuditTrail auditTrail)
            : this(sender, auditTrail, () => DateTimeOffset.UtcNow) { }

        internal SendGlobalChatMessageUseCase(
            IChatMessageSender sender,
            IChatOperationAuditTrail auditTrail,
            Func<DateTimeOffset> utcClock)
        {
            this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public async Task<ChatSendResult> ExecuteAsync(
            string actorSubject,
            string message,
            CancellationToken cancellationToken)
        {
            var actor = ChatValidation.RequireActor(actorSubject);
            var normalizedMessage = ChatValidation.NormalizeMessage(message);
            var result = await sender.SendGlobalAsync(normalizedMessage, cancellationToken).ConfigureAwait(false);
            auditTrail.Record(ChatAuditEntries.Send(
                actor,
                ChatOperationKind.SendGlobal,
                ChatChannel.Global,
                null,
                normalizedMessage.Length,
                result.Status,
                utcClock()));
            return result;
        }
    }

    public sealed class SendPrivateChatMessageUseCase
    {
        private readonly IChatMessageSender sender;
        private readonly IChatOperationAuditTrail auditTrail;
        private readonly Func<DateTimeOffset> utcClock;

        public SendPrivateChatMessageUseCase(IChatMessageSender sender, IChatOperationAuditTrail auditTrail)
            : this(sender, auditTrail, () => DateTimeOffset.UtcNow) { }

        internal SendPrivateChatMessageUseCase(
            IChatMessageSender sender,
            IChatOperationAuditTrail auditTrail,
            Func<DateTimeOffset> utcClock)
        {
            this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public async Task<ChatSendResult> ExecuteAsync(
            string actorSubject,
            string targetCrossplatformId,
            string message,
            CancellationToken cancellationToken)
        {
            var actor = ChatValidation.RequireActor(actorSubject);
            var target = ChatValidation.RequireBusinessKey(
                targetCrossplatformId,
                nameof(targetCrossplatformId));
            var normalizedMessage = ChatValidation.NormalizeMessage(message);
            var result = await sender.SendPrivateAsync(target, normalizedMessage, cancellationToken).ConfigureAwait(false);
            auditTrail.Record(ChatAuditEntries.Send(
                actor,
                ChatOperationKind.SendPrivate,
                ChatChannel.Whisper,
                target,
                normalizedMessage.Length,
                result.Status,
                utcClock()));
            return result;
        }
    }

    public sealed class SaveChatSettingsUseCase
    {
        private readonly IChatSettingsStore store;
        private readonly IChatRuntimeConfiguration runtime;
        private readonly IChatOperationAuditTrail auditTrail;
        private readonly Func<DateTimeOffset> utcClock;

        public SaveChatSettingsUseCase(
            IChatSettingsStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail)
            : this(store, runtime, auditTrail, () => DateTimeOffset.UtcNow) { }

        internal SaveChatSettingsUseCase(
            IChatSettingsStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public ChatSettings Execute(string actorSubject, ChatSettings settings)
        {
            var actor = ChatValidation.RequireActor(actorSubject);
            var saved = store.Save(ChatValidation.Normalize(settings));
            runtime.ApplyChatSettings(saved);
            auditTrail.Record(ChatAuditEntries.Mutation(
                actor,
                ChatOperationKind.SaveSettings,
                null,
                new[] { "isEnabled", "globalServerName", "whisperServerName", "commandPrefixes", "allowNoPrefix", "commandParameterSeparator", "hideRegisteredCommandGlobalMessages", "excludeCommandsFromHistory", "historyRetentionDays" },
                utcClock()));
            return saved;
        }
    }

    public sealed class ResetChatSettingsUseCase
    {
        private readonly IChatSettingsStore store;
        private readonly IChatRuntimeConfiguration runtime;
        private readonly IChatOperationAuditTrail auditTrail;
        private readonly Func<DateTimeOffset> utcClock;

        public ResetChatSettingsUseCase(
            IChatSettingsStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail)
            : this(store, runtime, auditTrail, () => DateTimeOffset.UtcNow) { }

        internal ResetChatSettingsUseCase(
            IChatSettingsStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public ChatSettings Execute(string actorSubject)
        {
            var actor = ChatValidation.RequireActor(actorSubject);
            var settings = store.Reset();
            runtime.ApplyChatSettings(settings);
            auditTrail.Record(ChatAuditEntries.Mutation(actor, ChatOperationKind.ResetSettings, null, Array.Empty<string>(), utcClock()));
            return settings;
        }
    }

    public sealed class SaveColoredChatSettingsUseCase
    {
        private readonly IColoredChatStore store;
        private readonly IChatRuntimeConfiguration runtime;
        private readonly IChatOperationAuditTrail auditTrail;
        private readonly Func<DateTimeOffset> utcClock;

        public SaveColoredChatSettingsUseCase(
            IColoredChatStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail)
            : this(store, runtime, auditTrail, () => DateTimeOffset.UtcNow) { }

        internal SaveColoredChatSettingsUseCase(
            IColoredChatStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public ColoredChatSettings Execute(string actorSubject, ColoredChatSettings settings)
        {
            var actor = ChatValidation.RequireActor(actorSubject);
            var saved = store.SaveSettings(ChatValidation.Normalize(settings));
            runtime.ApplyColoredChatSettings(saved);
            auditTrail.Record(ChatAuditEntries.Mutation(
                actor,
                ChatOperationKind.SaveColoredSettings,
                null,
                new[] { "isEnabled", "defaultColors", "playerColorTagPermission" },
                utcClock()));
            return saved;
        }
    }

    public sealed class ResetColoredChatSettingsUseCase
    {
        private readonly IColoredChatStore store;
        private readonly IChatRuntimeConfiguration runtime;
        private readonly IChatOperationAuditTrail auditTrail;
        private readonly Func<DateTimeOffset> utcClock;

        public ResetColoredChatSettingsUseCase(
            IColoredChatStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail)
            : this(store, runtime, auditTrail, () => DateTimeOffset.UtcNow) { }

        internal ResetColoredChatSettingsUseCase(
            IColoredChatStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public ColoredChatSettings Execute(string actorSubject)
        {
            var actor = ChatValidation.RequireActor(actorSubject);
            var settings = store.ResetSettings();
            runtime.ApplyColoredChatSettings(settings);
            auditTrail.Record(ChatAuditEntries.Mutation(actor, ChatOperationKind.ResetColoredSettings, null, Array.Empty<string>(), utcClock()));
            return settings;
        }
    }

    public sealed class CreateColoredChatProfileUseCase
    {
        private readonly IColoredChatStore store;
        private readonly IChatRuntimeConfiguration runtime;
        private readonly IChatOperationAuditTrail auditTrail;
        private readonly Func<DateTimeOffset> utcClock;

        public CreateColoredChatProfileUseCase(
            IColoredChatStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail)
            : this(store, runtime, auditTrail, () => DateTimeOffset.UtcNow) { }

        internal CreateColoredChatProfileUseCase(
            IColoredChatStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public ColoredChatProfile Execute(string actorSubject, ColoredChatProfile profile)
        {
            var actor = ChatValidation.RequireActor(actorSubject);
            var normalized = ChatValidation.Normalize(profile);
            if (!store.TryCreateProfile(normalized))
                throw new ColoredChatProfileConflictException();
            runtime.UpsertProfile(normalized);
            auditTrail.Record(ChatAuditEntries.Mutation(actor, ChatOperationKind.CreateProfile, normalized.CrossplatformId, ProfileFields, utcClock()));
            return normalized;
        }

        internal static readonly string[] ProfileFields = { "customName", "nameColor", "textColor", "description" };
    }

    public sealed class UpdateColoredChatProfileUseCase
    {
        private readonly IColoredChatStore store;
        private readonly IChatRuntimeConfiguration runtime;
        private readonly IChatOperationAuditTrail auditTrail;
        private readonly Func<DateTimeOffset> utcClock;

        public UpdateColoredChatProfileUseCase(
            IColoredChatStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail)
            : this(store, runtime, auditTrail, () => DateTimeOffset.UtcNow) { }

        internal UpdateColoredChatProfileUseCase(
            IColoredChatStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public ColoredChatProfile Execute(string actorSubject, ColoredChatProfile profile)
        {
            var actor = ChatValidation.RequireActor(actorSubject);
            var normalized = ChatValidation.Normalize(profile);
            if (!store.TryUpdateProfile(normalized))
                throw new ColoredChatProfileNotFoundException();
            runtime.UpsertProfile(normalized);
            auditTrail.Record(ChatAuditEntries.Mutation(actor, ChatOperationKind.UpdateProfile, normalized.CrossplatformId, CreateColoredChatProfileUseCase.ProfileFields, utcClock()));
            return normalized;
        }
    }

    public sealed class DeleteColoredChatProfileUseCase
    {
        private readonly IColoredChatStore store;
        private readonly IChatRuntimeConfiguration runtime;
        private readonly IChatOperationAuditTrail auditTrail;
        private readonly Func<DateTimeOffset> utcClock;

        public DeleteColoredChatProfileUseCase(
            IColoredChatStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail)
            : this(store, runtime, auditTrail, () => DateTimeOffset.UtcNow) { }

        internal DeleteColoredChatProfileUseCase(
            IColoredChatStore store,
            IChatRuntimeConfiguration runtime,
            IChatOperationAuditTrail auditTrail,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public void Execute(string actorSubject, string crossplatformId)
        {
            var actor = ChatValidation.RequireActor(actorSubject);
            var key = ChatValidation.RequireBusinessKey(crossplatformId, nameof(crossplatformId));
            if (!store.TryDeleteProfile(key))
                throw new ColoredChatProfileNotFoundException();
            runtime.RemoveProfile(key);
            auditTrail.Record(ChatAuditEntries.Mutation(actor, ChatOperationKind.DeleteProfile, key, Array.Empty<string>(), utcClock()));
        }
    }

    internal static class ChatAuditEntries
    {
        public static ChatOperationAuditEntry Send(
            string actor,
            ChatOperationKind operation,
            ChatChannel channel,
            string? target,
            int messageLength,
            ChatSendStatus status,
            DateTimeOffset occurredAtUtc) =>
            new ChatOperationAuditEntry(
                actor,
                operation,
                ChatValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc)),
                status.ToString(),
                channel,
                target,
                messageLength,
                null,
                Array.Empty<string>());

        public static ChatOperationAuditEntry Mutation(
            string actor,
            ChatOperationKind operation,
            string? businessKey,
            IReadOnlyList<string> changedFields,
            DateTimeOffset occurredAtUtc) =>
            new ChatOperationAuditEntry(
                actor,
                operation,
                ChatValidation.RequireUtc(occurredAtUtc, nameof(occurredAtUtc)),
                "Succeeded",
                null,
                null,
                null,
                businessKey,
                changedFields);
    }
}
