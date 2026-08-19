using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "SevenDays")]
    public sealed class SevenDaysChatRuntimeTests
    {
        [Fact]
        public void Runtime_StartsWriterLoadsStateSubscribesThenStartsInner()
        {
            var calls = new List<string>();
            ChatHistoryWriteService? writer = null;
            var state = new ChatRuntimeState(
                new SettingsStore(calls, () =>
                {
                    Assert.True(writer!.TryRecord(Message(1)));
                    calls.Add("writer-start");
                }),
                new ColoredStore(calls));
            writer = new ChatHistoryWriteService(new HistoryStore(), 4, TimeSpan.FromSeconds(1));
            var inner = new RecordingRuntime(calls);
            var runtime = new SevenDaysChatRuntime(
                state, writer, () => { calls.Add("subscribe"); return new CallbackDisposable(() => calls.Add("unsubscribe")); }, inner);

            runtime.Start();
            runtime.Stop();

            Assert.Equal(new[] { "writer-start", "chat-settings", "colored-settings", "profiles", "subscribe", "inner-start", "unsubscribe", "inner-stop" }, calls);
        }

        [Fact]
        public void Runtime_state_loads_active_mutes_into_the_same_snapshot_as_chat_configuration()
        {
            var mute = new ChatMuteRecord("EOS_1", "Alice", "reason", null, "owner", DateTimeOffset.UtcNow, "owner", DateTimeOffset.UtcNow);
            var state = new ChatRuntimeState(new SettingsStore(new List<string>()), new ColoredStore(new List<string>()), new MuteStore(mute));

            state.Load();

            Assert.True(state.Current.Mutes.ContainsKey("EOS_1"));
        }

        [Fact]
        public void Runtime_direct_colored_configuration_normalizes_values()
        {
            var state = new ChatRuntimeState(new SettingsStore(new List<string>()), new ColoredStore(new List<string>()));

            state.ApplyColoredChatSettings(new ColoredChatSettings
            {
                IsEnabled = true,
                GlobalDefaultColor = " aabbcc ",
                WhisperDefaultColor = "  ",
                PlayerColorTagPermission = PlayerColorTagPermission.AdminOnly
            });

            Assert.Equal("AABBCC", state.Current.ColoredSettings.GlobalDefaultColor);
            Assert.Null(state.Current.ColoredSettings.WhisperDefaultColor);
            Assert.Equal(PlayerColorTagPermission.AdminOnly, state.Current.ColoredSettings.PlayerColorTagPermission);
        }

        [Fact]
        public void Runtime_concurrent_profile_updates_retry_a_stale_cas_read_deterministically()
        {
            var state = new ChatRuntimeState(new SettingsStore(new List<string>()), new ColoredStore(new List<string>()));
            using var firstRead = new ManualResetEventSlim(false);
            using var secondCommitted = new ManualResetEventSlim(false);
            var firstDelegateCalls = 0;

            var first = Task.Run(() => InvokeUpdate(state, current =>
            {
                if (Interlocked.Increment(ref firstDelegateCalls) == 1)
                {
                    firstRead.Set();
                    if (!secondCommitted.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("The controlled CAS interleaving did not complete.");
                }

                return AddProfile(current, Profile("EOS_1", "Alpha"));
            }));

            try
            {
                Assert.True(firstRead.Wait(TimeSpan.FromSeconds(5)), "The first CAS read was not reached.");
                var second = Task.Run(() => InvokeUpdate(state, current =>
                    AddProfile(current, Profile("EOS_2", "Beta"))));
                Assert.True(second.Wait(TimeSpan.FromSeconds(5)), "The competing CAS update did not complete.");
                second.GetAwaiter().GetResult();
                secondCommitted.Set();
                Assert.True(first.Wait(TimeSpan.FromSeconds(5)), "The first CAS update did not complete.");
            }
            finally
            {
                secondCommitted.Set();
            }

            Assert.Equal(2, firstDelegateCalls);
            Assert.True(state.Current.Profiles.ContainsKey("EOS_1"));
            Assert.True(state.Current.Profiles.ContainsKey("EOS_2"));
            Assert.Equal("Alpha", state.Current.Profiles["EOS_1"].CustomName);
            Assert.Equal("Beta", state.Current.Profiles["EOS_2"].CustomName);
        }

        [Fact]
        public void Writer_DoesNotBlockWhenQueueIsFull()
        {
            using var gate = new ManualResetEventSlim(false);
            var store = new BlockingHistoryStore(gate);
            var writer = new ChatHistoryWriteService(store, 1, TimeSpan.FromMilliseconds(100));
            writer.Start();
            Assert.True(writer.TryRecord(Message(1)));
            Assert.True(store.AppendStarted.Wait(TimeSpan.FromSeconds(5)), "The writer did not start appending.");
            Assert.True(writer.TryRecord(Message(2)));

            Assert.False(writer.TryRecord(Message(3)));
            gate.Set();
            writer.Stop();
            Assert.True(writer.DroppedFullCount >= 1);
        }

        private static ChatMessage Message(long sequence) => new ChatMessage
        {
            Sequence = sequence,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            EntityId = 1,
            SenderName = "player",
            Channel = ChatChannel.Global,
            SourceKind = ChatSourceKind.Player,
            Message = "hello"
        };

        private static void InvokeUpdate(
            ChatRuntimeState state,
            Func<ChatRuntimeSnapshot, ChatRuntimeSnapshot> update)
        {
            var method = typeof(ChatRuntimeState).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(state, new object[] { update });
        }

        private static ChatRuntimeSnapshot AddProfile(ChatRuntimeSnapshot current, ColoredChatProfile profile)
        {
            var profiles = new Dictionary<string, ColoredChatProfile>(StringComparer.Ordinal);
            foreach (var pair in current.Profiles)
                profiles.Add(pair.Key, pair.Value);
            profiles[profile.CrossplatformId] = profile;
            return new ChatRuntimeSnapshot(current.ChatSettings, current.ColoredSettings, profiles, current.Mutes);
        }

        private static ColoredChatProfile Profile(string crossplatformId, string customName) => new ColoredChatProfile
        {
            CrossplatformId = crossplatformId,
            CustomName = customName,
            NameColor = "aabbcc",
            TextColor = null,
            Description = "note",
            CreatedAtUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
        };

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class SettingsStore : IChatSettingsStore
        {
            private readonly List<string> calls;
            private readonly Action? beforeGet;

            public SettingsStore(List<string> calls, Action? beforeGet = null)
            {
                this.calls = calls;
                this.beforeGet = beforeGet;
            }

            public ChatSettings Get()
            {
                beforeGet?.Invoke();
                calls.Add("chat-settings");
                return new ChatSettings { IsEnabled = true, CommandPrefixes = new[] { "/" }, ExcludeCommandsFromHistory = true, HistoryRetentionDays = 30 };
            }
            public ChatSettings Save(ChatSettings settings) => settings;
            public ChatSettings Reset() => Get();
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class ColoredStore : IColoredChatStore
        {
            private readonly List<string> calls;
            public ColoredStore(List<string> calls) => this.calls = calls;
            public ColoredChatSettings GetSettings() { calls.Add("colored-settings"); return new ColoredChatSettings { IsEnabled = false, PlayerColorTagPermission = PlayerColorTagPermission.None }; }
            public IReadOnlyList<ColoredChatProfile> GetAllProfiles() { calls.Add("profiles"); return Array.Empty<ColoredChatProfile>(); }
            public ColoredChatSettings SaveSettings(ColoredChatSettings settings) => settings;
            public ColoredChatSettings ResetSettings() => GetSettings();
            public ColoredChatProfilePage GetProfiles(ColoredChatProfileQuery query) => throw new NotSupportedException();
            public bool TryCreateProfile(ColoredChatProfile profile) => false;
            public bool TryUpdateProfile(ColoredChatProfile profile) => false;
            public bool TryDeleteProfile(string crossplatformId) => false;
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class MuteStore : IChatMuteStore
        {
            private readonly ChatMuteRecord mute;
            public MuteStore(ChatMuteRecord mute) => this.mute = mute;
            public ChatMutePage GetPage(int pageSize, ChatMuteCursor? cursor) => new ChatMutePage(new[] { mute }, null);
            public ChatMuteRecord? Find(string crossplatformId) => mute;
            public IReadOnlyList<ChatMuteRecord> Create(ChatMuteRecord record, ChatMuteOperation operation) => new[] { record };
            public IReadOnlyList<ChatMuteRecord> Update(ChatMuteRecord record, ChatMuteOperation operation) => new[] { record };
            public IReadOnlyList<ChatMuteRecord> Release(string crossplatformId, ChatMuteOperation operation) => Array.Empty<ChatMuteRecord>();
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private class HistoryStore : IChatHistoryStore
        {
            public virtual void Append(ChatMessage message) { }
            public void AppendGap(ChatHistoryGap gap) { }
            public ChatHistoryPage GetHistory(ChatHistoryQuery query) => throw new NotSupportedException();
            public int DeleteBefore(DateTimeOffset cutoffUtc, int maximumDeletes) => 0;
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class BlockingHistoryStore : HistoryStore
        {
            private readonly ManualResetEventSlim gate;
            public BlockingHistoryStore(ManualResetEventSlim gate) => this.gate = gate;
            public ManualResetEventSlim AppendStarted { get; } = new ManualResetEventSlim(false);
            public override void Append(ChatMessage message)
            {
                AppendStarted.Set();
                gate.Wait();
            }
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly List<string> calls;
            public RecordingRuntime(List<string> calls) => this.calls = calls;
            public void Start() => calls.Add("inner-start");
            public void MarkGameReady() { }
            public void Stop() => calls.Add("inner-stop");
        }

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class CallbackDisposable : IDisposable
        {
            private Action? callback;
            public CallbackDisposable(Action callback) => this.callback = callback;
            public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
        }
    }
}
