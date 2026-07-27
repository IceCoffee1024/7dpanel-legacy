using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat
{
    public sealed class ChatRuntimeState : IChatMuteRuntimeConfiguration
    {
        private readonly IChatSettingsStore chatSettingsStore;
        private readonly IColoredChatStore coloredChatStore;
        private readonly IChatMuteStore? chatMuteStore;
        private ChatRuntimeSnapshot snapshot = ChatRuntimeSnapshot.Empty;

        public ChatRuntimeState(
            IChatSettingsStore chatSettingsStore,
            IColoredChatStore coloredChatStore,
            IChatMuteStore? chatMuteStore = null)
        {
            this.chatSettingsStore = chatSettingsStore ?? throw new ArgumentNullException(nameof(chatSettingsStore));
            this.coloredChatStore = coloredChatStore ?? throw new ArgumentNullException(nameof(coloredChatStore));
            this.chatMuteStore = chatMuteStore;
        }

        internal ChatRuntimeSnapshot Current => Volatile.Read(ref snapshot);

        public void Load()
        {
            var chat = ChatValidation.Normalize(chatSettingsStore.Get());
            var colored = ChatValidation.Normalize(coloredChatStore.GetSettings());
            var profiles = coloredChatStore.GetAllProfiles()
                .Select(ChatValidation.Normalize)
                .ToDictionary(profile => profile.CrossplatformId, StringComparer.Ordinal);
            var mutes = LoadCurrentMutes(DateTimeOffset.UtcNow);
            Interlocked.Exchange(ref snapshot, new ChatRuntimeSnapshot(chat, colored, profiles, mutes));
        }

        public void ApplyChatSettings(ChatSettings settings) =>
            Update(current => new ChatRuntimeSnapshot(ChatValidation.Normalize(settings), current.ColoredSettings, current.Profiles, current.Mutes));

        public void ApplyColoredChatSettings(ColoredChatSettings settings) =>
            Update(current => new ChatRuntimeSnapshot(current.ChatSettings, ChatValidation.Normalize(settings), current.Profiles, current.Mutes));

        public void UpsertProfile(ColoredChatProfile profile)
        {
            var normalized = ChatValidation.Normalize(profile);
            Update(current =>
            {
                var profiles = CopyProfiles(current.Profiles);
                profiles[normalized.CrossplatformId] = normalized;
                return new ChatRuntimeSnapshot(current.ChatSettings, current.ColoredSettings, profiles, current.Mutes);
            });
        }

        public void RemoveProfile(string crossplatformId)
        {
            var key = ChatValidation.RequireBusinessKey(crossplatformId, nameof(crossplatformId));
            Update(current =>
            {
                var profiles = CopyProfiles(current.Profiles);
                profiles.Remove(key);
                return new ChatRuntimeSnapshot(current.ChatSettings, current.ColoredSettings, profiles, current.Mutes);
            });
        }

        public void ReplaceMuteSnapshot(IReadOnlyDictionary<string, ChatMuteRecord> mutes)
        {
            if (mutes == null) throw new ArgumentNullException(nameof(mutes));
            Update(current => new ChatRuntimeSnapshot(current.ChatSettings, current.ColoredSettings, current.Profiles, mutes));
        }

        private IReadOnlyDictionary<string, ChatMuteRecord> LoadCurrentMutes(DateTimeOffset nowUtc)
        {
            if (chatMuteStore == null) return new Dictionary<string, ChatMuteRecord>(StringComparer.Ordinal);
            var records = new Dictionary<string, ChatMuteRecord>(StringComparer.Ordinal);
            ChatMuteCursor? cursor = null;
            do
            {
                var page = chatMuteStore.GetPage(100, cursor);
                foreach (var record in page.Records)
                    if (record.IsActiveAt(nowUtc)) records[record.CrossplatformId] = record;
                cursor = page.NextCursor;
            } while (cursor != null);
            return records;
        }

        private void Update(Func<ChatRuntimeSnapshot, ChatRuntimeSnapshot> update)
        {
            while (true)
            {
                var current = Current;
                var replacement = update(current);
                if (ReferenceEquals(Interlocked.CompareExchange(ref snapshot, replacement, current), current)) return;
            }
        }

        private static Dictionary<string, ColoredChatProfile> CopyProfiles(
            IReadOnlyDictionary<string, ColoredChatProfile> source)
        {
            var copy = new Dictionary<string, ColoredChatProfile>(StringComparer.Ordinal);
            foreach (var pair in source) copy[pair.Key] = pair.Value;
            return copy;
        }
    }

    internal sealed class ChatRuntimeSnapshot
    {
        public static readonly ChatRuntimeSnapshot Empty = new ChatRuntimeSnapshot(
            new ChatSettings { IsEnabled = false, CommandPrefixes = Array.Empty<string>(), ExcludeCommandsFromHistory = true, HistoryRetentionDays = 0 },
            new ColoredChatSettings { IsEnabled = false, PlayerColorTagPermission = PlayerColorTagPermission.None },
            new Dictionary<string, ColoredChatProfile>(StringComparer.Ordinal),
            new Dictionary<string, ChatMuteRecord>(StringComparer.Ordinal));

        public ChatRuntimeSnapshot(
            ChatSettings chatSettings,
            ColoredChatSettings coloredSettings,
            IReadOnlyDictionary<string, ColoredChatProfile> profiles,
            IReadOnlyDictionary<string, ChatMuteRecord> mutes)
        {
            ChatSettings = chatSettings;
            ColoredSettings = coloredSettings;
            var copy = new Dictionary<string, ColoredChatProfile>(StringComparer.Ordinal);
            foreach (var pair in profiles) copy[pair.Key] = pair.Value;
            Profiles = new ReadOnlyDictionary<string, ColoredChatProfile>(copy);
            var muteCopy = new Dictionary<string, ChatMuteRecord>(StringComparer.Ordinal);
            foreach (var pair in mutes) muteCopy[pair.Key] = pair.Value;
            Mutes = new ReadOnlyDictionary<string, ChatMuteRecord>(muteCopy);
        }

        public ChatSettings ChatSettings { get; }
        public ColoredChatSettings ColoredSettings { get; }
        public IReadOnlyDictionary<string, ColoredChatProfile> Profiles { get; }
        public IReadOnlyDictionary<string, ChatMuteRecord> Mutes { get; }
    }
}
