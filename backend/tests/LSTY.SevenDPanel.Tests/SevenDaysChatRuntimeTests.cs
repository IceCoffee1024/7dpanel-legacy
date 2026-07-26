using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysChatRuntimeTests
    {
        [Fact]
        public void Runtime_StartsWriterLoadsStateSubscribesThenStartsInner()
        {
            var calls = new List<string>();
            var state = new ChatRuntimeState(new SettingsStore(calls), new ColoredStore(calls));
            var writer = new ChatHistoryWriteService(new HistoryStore(), 4, TimeSpan.FromSeconds(1));
            var inner = new RecordingRuntime(calls);
            var runtime = new SevenDaysChatRuntime(
                state, writer, () => { calls.Add("subscribe"); return new CallbackDisposable(() => calls.Add("unsubscribe")); }, inner);

            runtime.Start();
            runtime.Stop();

            Assert.Equal(new[] { "chat-settings", "colored-settings", "profiles", "subscribe", "inner-start", "unsubscribe", "inner-stop" }, calls);
        }

        [Fact]
        public void Writer_DoesNotBlockWhenQueueIsFull()
        {
            using var gate = new ManualResetEventSlim(false);
            var writer = new ChatHistoryWriteService(new BlockingHistoryStore(gate), 1, TimeSpan.FromMilliseconds(100));
            writer.Start();
            Assert.True(writer.TryRecord(Message(1)));
            SpinWait.SpinUntil(() => writer.QueueDepth == 0, TimeSpan.FromSeconds(1));
            Assert.True(writer.TryRecord(Message(2)));

            var started = DateTime.UtcNow;
            Assert.False(writer.TryRecord(Message(3)));
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromMilliseconds(100));
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

        private sealed class SettingsStore : IChatSettingsStore
        {
            private readonly List<string> calls;
            public SettingsStore(List<string> calls) => this.calls = calls;
            public ChatSettings Get() { calls.Add("chat-settings"); return new ChatSettings { IsEnabled = true, CommandPrefixes = new[] { "/" }, ExcludeCommandsFromHistory = true, HistoryRetentionDays = 30 }; }
            public ChatSettings Save(ChatSettings settings) => settings;
            public ChatSettings Reset() => Get();
        }

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

        private class HistoryStore : IChatHistoryStore
        {
            public virtual void Append(ChatMessage message) { }
            public void AppendGap(ChatHistoryGap gap) { }
            public ChatHistoryPage GetHistory(ChatHistoryQuery query) => throw new NotSupportedException();
            public int DeleteBefore(DateTimeOffset cutoffUtc, int maximumDeletes) => 0;
        }

        private sealed class BlockingHistoryStore : HistoryStore
        {
            private readonly ManualResetEventSlim gate;
            public BlockingHistoryStore(ManualResetEventSlim gate) => this.gate = gate;
            public override void Append(ChatMessage message) => gate.Wait();
        }

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly List<string> calls;
            public RecordingRuntime(List<string> calls) => this.calls = calls;
            public void Start() => calls.Add("inner-start");
            public void MarkGameReady() { }
            public void Stop() => calls.Add("inner-stop");
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action? callback;
            public CallbackDisposable(Action callback) => this.callback = callback;
            public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
        }
    }
}
