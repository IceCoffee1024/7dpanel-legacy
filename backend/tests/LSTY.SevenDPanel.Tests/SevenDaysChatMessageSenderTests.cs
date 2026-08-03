using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Chat;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Chat;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Community")]
    [Trait("Boundary", "SevenDays")]
    public sealed class SevenDaysChatMessageSenderTests
    {
        [Fact]
        public async Task Sender_preserves_fifo_and_uses_configured_names()
        {
            var dispatched = new List<SevenDaysChatDispatch>();
            using var sender = CreateSender(
                dispatch: (request, _) =>
                {
                    lock (dispatched) dispatched.Add(request);
                    return Task.FromResult(ChatSendStatus.Accepted);
                });

            var first = sender.SendGlobalAsync("one", CancellationToken.None);
            var second = sender.SendGlobalAsync("two", CancellationToken.None);
            Assert.Equal(ChatSendStatus.Accepted, (await first).Status);
            Assert.Equal(ChatSendStatus.Accepted, (await second).Status);

            Assert.Collection(dispatched,
                item => { Assert.Equal("one", item.Message); Assert.Equal("Global Server", item.SenderName); },
                item => Assert.Equal("two", item.Message));
        }

        [Fact]
        public async Task Private_send_requires_an_exact_current_crossplatform_identity()
        {
            SevenDaysChatDispatch? dispatched = null;
            using var sender = CreateSender(
                players: new[] { Player(7, "EOS_Alice") },
                dispatch: (request, _) =>
                {
                    dispatched = request;
                    return Task.FromResult(ChatSendStatus.Accepted);
                });

            Assert.Equal(ChatSendStatus.TargetOffline,
                (await sender.SendPrivateAsync("eos_alice", "hello", CancellationToken.None)).Status);
            Assert.Equal(ChatSendStatus.Accepted,
                (await sender.SendPrivateAsync("EOS_Alice", "hello", CancellationToken.None)).Status);
            Assert.Equal(7, dispatched?.TargetEntityId);
            Assert.Equal("Whisper Server", dispatched?.SenderName);
        }

        [Fact]
        public async Task Full_queue_fails_fast_without_reordering_accepted_work()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sender = CreateSender(
                capacity: 1,
                dispatch: async (_, __) =>
                {
                    entered.TrySetResult(true);
                    await release.Task;
                    return ChatSendStatus.Accepted;
                });

            var running = sender.SendGlobalAsync("running", CancellationToken.None);
            await entered.Task;
            var queued = sender.SendGlobalAsync("queued", CancellationToken.None);
            var rejected = await sender.SendGlobalAsync("rejected", CancellationToken.None);

            Assert.Equal(ChatSendStatus.QueueFull, rejected.Status);
            release.TrySetResult(true);
            Assert.Equal(ChatSendStatus.Accepted, (await running).Status);
            Assert.Equal(ChatSendStatus.Accepted, (await queued).Status);
        }

        private static SevenDaysChatMessageSender CreateSender(
            IReadOnlyList<PlayerSnapshot>? players = null,
            Func<SevenDaysChatDispatch, CancellationToken, Task<ChatSendStatus>>? dispatch = null,
            int capacity = 16) =>
            new SevenDaysChatMessageSender(
                () => new ChatSettings
                {
                    IsEnabled = true,
                    GlobalServerName = "Global Server",
                    WhisperServerName = "Whisper Server",
                    CommandPrefixes = new[] { "/" },
                    ExcludeCommandsFromHistory = true,
                    HistoryRetentionDays = 30
                },
                new OnlineQuery(players ?? Array.Empty<PlayerSnapshot>()),
                dispatch ?? ((_, __) => Task.FromResult(ChatSendStatus.Accepted)),
                capacity);

        private static PlayerSnapshot Player(int entityId, string crossplatformId) =>
            new PlayerSnapshot(
                entityId, "Alice", new PlayerPlatformIdentity("Steam_1", "Steam"),
                new PlayerPlatformIdentity(crossplatformId, "EOS"), PlayerDeviceType.Windows,
                null, 1, null, null, 0, new PlayerPosition(0, 0, 0), false,
                100, 100, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow);

        [Trait("Capability", "Community")]

        [Trait("Boundary", "SevenDays")]

        private sealed class OnlineQuery : IOnlinePlayerQuery
        {
            private readonly OnlinePlayersSnapshot snapshot;
            public OnlineQuery(IReadOnlyList<PlayerSnapshot> players) => snapshot = new OnlinePlayersSnapshot(players);
            public Task<OnlinePlayersSnapshot> GetOnlineAsync(CancellationToken cancellationToken) =>
                Task.FromResult(snapshot);
        }
    }
}
