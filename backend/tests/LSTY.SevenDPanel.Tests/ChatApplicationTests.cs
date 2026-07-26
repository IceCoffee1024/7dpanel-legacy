using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Chat;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ChatApplicationTests
    {
        [Theory]
        [InlineData(" ")]
        [InlineData(501)]
        public void Message_validation_rejects_empty_or_oversized_content(object value)
        {
            var message = value is int length ? new string('x', length) : (string)value;

            Assert.Throws<ArgumentException>(() => ChatValidation.NormalizeMessage(message));
        }

        [Fact]
        public void Message_validation_trims_valid_content()
        {
            Assert.Equal("hello", ChatValidation.NormalizeMessage("  hello  "));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3651)]
        public void Chat_settings_reject_retention_outside_contract(int days)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                ChatValidation.Normalize(CreateChatSettings(historyRetentionDays: days)));
        }

        [Fact]
        public void Chat_settings_trim_names_and_deduplicate_single_character_prefixes()
        {
            var settings = ChatValidation.Normalize(CreateChatSettings(
                globalServerName: "  Server  ",
                whisperServerName: "   ",
                commandPrefixes: new[] { "/", "!", "/" }));

            Assert.Equal("Server", settings.GlobalServerName);
            Assert.Null(settings.WhisperServerName);
            Assert.Equal(new[] { "/", "!" }, settings.CommandPrefixes);
        }

        [Fact]
        public void Chat_settings_reject_multi_character_prefixes()
        {
            Assert.Throws<ArgumentException>(() =>
                ChatValidation.Normalize(CreateChatSettings(commandPrefixes: new[] { "//" })));
        }

        [Fact]
        public void Chat_settings_reject_whitespace_prefixes()
        {
            Assert.Throws<ArgumentException>(() =>
                ChatValidation.Normalize(CreateChatSettings(commandPrefixes: new[] { " " })));
        }

        [Theory]
        [InlineData("a0b1c2", "A0B1C2")]
        [InlineData("  ", null)]
        [InlineData(null, null)]
        public void Colors_are_normalized_to_optional_uppercase_rgb(string? input, string? expected)
        {
            Assert.Equal(expected, ChatValidation.NormalizeColor(input));
        }

        [Theory]
        [InlineData("#FFFFFF")]
        [InlineData("GG0000")]
        [InlineData("FFF")]
        public void Invalid_colors_are_rejected(string input)
        {
            Assert.Throws<ArgumentException>(() => ChatValidation.NormalizeColor(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData("EOS 123")]
        [InlineData(" EOS_123")]
        public void Profile_business_key_must_be_non_empty_and_contain_no_whitespace(string id)
        {
            Assert.Throws<ArgumentException>(() =>
                ChatValidation.Normalize(CreateProfile(id)));
        }

        [Fact]
        public async Task Global_send_trims_message_and_audits_metadata_without_body()
        {
            var sender = new RecordingSender();
            var audit = new RecordingAuditTrail();
            var useCase = new SendGlobalChatMessageUseCase(sender, audit, () => Utc(1));

            var result = await useCase.ExecuteAsync("owner", "  secret body  ", CancellationToken.None);

            Assert.Equal("secret body", sender.GlobalMessage);
            Assert.Equal(ChatSendStatus.Accepted, result.Status);
            var entry = Assert.Single(audit.Entries);
            Assert.Equal("owner", entry.ActorSubject);
            Assert.Equal(ChatChannel.Global, entry.Channel);
            Assert.Equal(11, entry.MessageLength);
            Assert.DoesNotContain(
                entry.GetType().GetProperties(),
                property => property.Name.IndexOf("Message", StringComparison.Ordinal) >= 0
                    && property.Name != nameof(ChatOperationAuditEntry.MessageLength));
            Assert.DoesNotContain("secret body", entry.ToString()!);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task Private_send_rejects_empty_target_before_sender_and_audit(string? target)
        {
            var sender = new RecordingSender();
            var audit = new RecordingAuditTrail();
            var useCase = new SendPrivateChatMessageUseCase(sender, audit, () => Utc(1));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                useCase.ExecuteAsync("owner", target!, "hello", CancellationToken.None));

            Assert.Equal(0, sender.CallCount);
            Assert.Empty(audit.Entries);
        }

        [Fact]
        public void Saving_settings_updates_runtime_only_after_store_success()
        {
            var order = new List<string>();
            var store = new RecordingSettingsStore(order);
            var runtime = new RecordingRuntimeConfiguration(order);
            var audit = new RecordingAuditTrail();
            var useCase = new SaveChatSettingsUseCase(store, runtime, audit, () => Utc(1));

            var result = useCase.Execute("owner", CreateChatSettings(globalServerName: " Server "));

            Assert.Equal(new[] { "store:settings", "runtime:settings" }, order);
            Assert.Same(store.Saved, result);
            Assert.Same(result, runtime.ChatSettings);
        }

        [Fact]
        public void Store_failure_does_not_update_runtime_or_audit_success()
        {
            var store = new RecordingSettingsStore { Failure = new InvalidOperationException("unavailable") };
            var runtime = new RecordingRuntimeConfiguration();
            var audit = new RecordingAuditTrail();
            var useCase = new SaveChatSettingsUseCase(store, runtime, audit, () => Utc(1));

            Assert.Throws<InvalidOperationException>(() =>
                useCase.Execute("owner", CreateChatSettings()));

            Assert.Null(runtime.ChatSettings);
            Assert.Empty(audit.Entries);
        }

        [Fact]
        public void Profile_create_conflict_has_stable_application_exception_and_no_runtime_update()
        {
            var store = new RecordingColoredChatStore { CreateResult = false };
            var runtime = new RecordingRuntimeConfiguration();
            var useCase = new CreateColoredChatProfileUseCase(
                store,
                runtime,
                new RecordingAuditTrail(),
                () => Utc(1));

            Assert.Throws<ColoredChatProfileConflictException>(() =>
                useCase.Execute("owner", CreateProfile("EOS_123")));

            Assert.Null(runtime.Profile);
        }

        [Fact]
        public void History_query_exposes_internal_keyset_without_public_cursor_encoding()
        {
            var keyset = new ChatHistoryKeyset(Utc(2), 42);
            var query = new ChatHistoryQuery(
                50,
                " EOS_123 ",
                " Alice ",
                ChatChannel.Whisper,
                ChatSourceKind.Player,
                Utc(1),
                Utc(3),
                keyset);

            Assert.Equal("EOS_123", query.CrossplatformId);
            Assert.Equal("Alice", query.SenderName);
            Assert.Same(keyset, query.Keyset);
            Assert.DoesNotContain(
                typeof(ChatHistoryQuery).GetProperties(),
                property => property.Name.Contains("Cursor"));
        }

        private static ChatSettings CreateChatSettings(
            string? globalServerName = null,
            string? whisperServerName = null,
            IEnumerable<string>? commandPrefixes = null,
            int historyRetentionDays = 30) =>
            new ChatSettings
            {
                IsEnabled = true,
                GlobalServerName = globalServerName,
                WhisperServerName = whisperServerName,
                CommandPrefixes = commandPrefixes?.ToArray() ?? new[] { "/" },
                ExcludeCommandsFromHistory = true,
                HistoryRetentionDays = historyRetentionDays
            };

        private static ColoredChatProfile CreateProfile(string id) =>
            new ColoredChatProfile
            {
                CrossplatformId = id,
                CustomName = "{playerName}",
                NameColor = "a0b1c2",
                TextColor = null,
                Description = "operator note",
                CreatedAtUtc = Utc(1),
                UpdatedAtUtc = Utc(1)
            };

        private static DateTimeOffset Utc(int hour) =>
            new DateTimeOffset(2026, 7, 26, hour, 0, 0, TimeSpan.Zero);

        private sealed class RecordingSender : IChatMessageSender
        {
            public string? GlobalMessage { get; private set; }
            public int CallCount { get; private set; }

            public Task<ChatSendResult> SendGlobalAsync(string message, CancellationToken cancellationToken)
            {
                CallCount++;
                GlobalMessage = message;
                return Task.FromResult(ChatSendResult.Accepted());
            }

            public Task<ChatSendResult> SendPrivateAsync(
                string targetCrossplatformId,
                string message,
                CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(ChatSendResult.Accepted());
            }
        }

        private sealed class RecordingAuditTrail : IChatOperationAuditTrail
        {
            public List<ChatOperationAuditEntry> Entries { get; } = new List<ChatOperationAuditEntry>();
            public void Record(ChatOperationAuditEntry entry) => Entries.Add(entry);
        }

        private sealed class RecordingSettingsStore : IChatSettingsStore
        {
            private readonly List<string>? order;

            public RecordingSettingsStore(List<string>? order = null) => this.order = order;
            public Exception? Failure { get; set; }
            public ChatSettings? Saved { get; private set; }
            public ChatSettings Current { get; set; } = CreateChatSettings();

            public ChatSettings Get() => Current;

            public ChatSettings Save(ChatSettings settings)
            {
                if (Failure != null) throw Failure;
                order?.Add("store:settings");
                return Saved = settings;
            }

            public ChatSettings Reset() => Current;
        }

        private sealed class RecordingColoredChatStore : IColoredChatStore
        {
            public bool CreateResult { get; set; } = true;
            public ColoredChatSettings GetSettings() => throw new NotSupportedException();
            public ColoredChatSettings SaveSettings(ColoredChatSettings settings) => settings;
            public ColoredChatSettings ResetSettings() => throw new NotSupportedException();
            public ColoredChatProfilePage GetProfiles(ColoredChatProfileQuery query) => throw new NotSupportedException();
            public IReadOnlyList<ColoredChatProfile> GetAllProfiles() => Array.Empty<ColoredChatProfile>();
            public bool TryCreateProfile(ColoredChatProfile profile) => CreateResult;
            public bool TryUpdateProfile(ColoredChatProfile profile) => false;
            public bool TryDeleteProfile(string crossplatformId) => false;
        }

        private sealed class RecordingRuntimeConfiguration : IChatRuntimeConfiguration
        {
            private readonly List<string>? order;

            public RecordingRuntimeConfiguration(List<string>? order = null) => this.order = order;
            public ChatSettings? ChatSettings { get; private set; }
            public ColoredChatProfile? Profile { get; private set; }

            public void ApplyChatSettings(ChatSettings settings)
            {
                order?.Add("runtime:settings");
                ChatSettings = settings;
            }

            public void ApplyColoredChatSettings(ColoredChatSettings settings) { }
            public void UpsertProfile(ColoredChatProfile profile) => Profile = profile;
            public void RemoveProfile(string crossplatformId) { }
        }
    }
}
