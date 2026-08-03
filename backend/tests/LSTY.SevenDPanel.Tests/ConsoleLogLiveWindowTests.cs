using System;
using System.Linq;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class ConsoleLogLiveWindowTests
    {
        [Fact]
        public void Named_events_share_one_monotonic_sequence()
        {
            var window = new ServerEventLiveWindow(4);
            var occurredAtUtc = new DateTime(2026, 7, 21, 8, 9, 10, DateTimeKind.Utc);

            var consoleLog = window.AppendConsoleLog(CreateEntry("one"));
            var chatMessage = window.AppendChatMessage(
                new DateTimeOffset(occurredAtUtc),
                42,
                "EOS_123",
                "Alice",
                "Global",
                "Player",
                "hello");
            var gameReady = window.AppendGameReady(occurredAtUtc);
            var serverStopping = window.AppendServerStopping(occurredAtUtc.AddMinutes(1));

            Assert.Equal(new[] { 1L, 2L, 3L, 4L },
                window.ReadAfter(null, 10).Entries.Select(entry => entry.Sequence));
            Assert.Equal(ServerEventNames.ConsoleLog, consoleLog.EventName);
            Assert.Equal(ServerEventNames.ChatMessage, chatMessage.EventName);
            Assert.Equal(ServerEventNames.GameReady, gameReady.EventName);
            Assert.Equal(ServerEventNames.ServerStopping, serverStopping.EventName);
            Assert.IsType<ConsoleLogEventData>(consoleLog.Data);
            var chatData = Assert.IsType<ChatMessageEventData>(chatMessage.Data);
            Assert.Equal(2L, chatData.Sequence);
            Assert.Equal(new DateTimeOffset(occurredAtUtc), chatData.OccurredAtUtc);
            Assert.Equal(42, chatData.EntityId);
            Assert.Equal("EOS_123", chatData.CrossplatformId);
            Assert.Equal("Alice", chatData.SenderName);
            Assert.Equal("Global", chatData.Channel);
            Assert.Equal("Player", chatData.SourceKind);
            Assert.Equal("hello", chatData.Message);
            Assert.IsType<GameReadyEventData>(gameReady.Data);
            Assert.IsType<ServerStoppingEventData>(serverStopping.Data);
        }

        [Fact]
        public void Append_assigns_sequence_and_preserves_entry_fields()
        {
            var window = new ServerEventLiveWindow(3);
            var timestamp = new DateTime(2026, 7, 20, 8, 9, 10, DateTimeKind.Utc);
            var entry = new ConsoleLogEntry(
                "formatted",
                "plain",
                "trace",
                ConsoleLogType.Warning,
                timestamp,
                1234L);

            var first = window.AppendConsoleLog(entry);
            var second = window.AppendConsoleLog(entry);
            var firstData = Assert.IsType<ConsoleLogEventData>(first.Data);

            Assert.Equal(1L, first.Sequence);
            Assert.Equal(2L, second.Sequence);
            Assert.Equal("formatted", firstData.FormattedMessage);
            Assert.Equal("plain", firstData.Message);
            Assert.Equal("trace", firstData.Trace);
            Assert.Equal("warning", firstData.LogType);
            Assert.Equal(timestamp, firstData.Timestamp);
            Assert.Equal(1234L, firstData.UptimeMilliseconds);
        }

        [Fact]
        public void Fixed_capacity_evicts_oldest_entries()
        {
            var window = new ServerEventLiveWindow(2);

            window.AppendConsoleLog(CreateEntry("one"));
            window.AppendConsoleLog(CreateEntry("two"));
            window.AppendConsoleLog(CreateEntry("three"));

            var result = window.ReadAfter(null, 10);

            Assert.Equal(2L, result.OldestSequence);
            Assert.Equal(3L, result.LatestSequence);
            Assert.Equal(
                new[] { "two", "three" },
                result.Entries.Select(entry => Assert.IsType<ConsoleLogEventData>(entry.Data).Message));
            Assert.False(result.HasGap);
        }

        [Fact]
        public void Read_after_honors_exclusive_cursor_and_limit()
        {
            var window = new ServerEventLiveWindow(5);
            for (var index = 1; index <= 5; index++)
                window.AppendConsoleLog(CreateEntry(index.ToString()));

            var result = window.ReadAfter(2L, 2);

            Assert.Equal(new[] { 3L, 4L }, result.Entries.Select(entry => entry.Sequence));
            Assert.Equal(1L, result.OldestSequence);
            Assert.Equal(5L, result.LatestSequence);
            Assert.False(result.HasGap);
        }

        [Fact]
        public void Recent_console_logs_returns_latest_logs_in_sequence_order_and_skips_lifecycle_events()
        {
            var window = new ServerEventLiveWindow(6);
            window.AppendConsoleLog(CreateEntry("one"));
            window.AppendGameReady(new DateTime(2026, 7, 26, 1, 2, 3, DateTimeKind.Utc));
            window.AppendConsoleLog(CreateEntry("two"));
            window.AppendServerStopping(new DateTime(2026, 7, 26, 1, 3, 3, DateTimeKind.Utc));
            window.AppendConsoleLog(CreateEntry("three"));

            var entries = window.ReadRecentConsoleLogs(2);

            Assert.Equal(new[] { 3L, 5L }, entries.Select(entry => entry.Sequence));
            Assert.Equal(new[] { "two", "three" }, entries.Select(entry => entry.Message));
        }

        [Fact]
        public void Recent_chat_messages_returns_latest_chat_only_in_sequence_order()
        {
            var window = new ServerEventLiveWindow(8);
            window.AppendChatMessage(
                DateTimeOffset.UtcNow, 1, null, string.Empty, "Unknown", "System", "one");
            window.AppendConsoleLog(CreateEntry("console"));
            window.AppendChatMessage(
                DateTimeOffset.UtcNow, 2, "EOS_2", "Two", "Party", "Player", "two");
            window.AppendGameReady(DateTime.UtcNow);
            window.AppendChatMessage(
                DateTimeOffset.UtcNow, 3, "EOS_3", "Three", "Whisper", "Administrator", "three");

            var entries = window.ReadRecentChatMessages(2);

            Assert.Equal(new[] { 3L, 5L }, entries.Select(entry => entry.Sequence));
            Assert.Equal(new[] { "two", "three" }, entries.Select(entry => entry.Message));
        }

        [Theory]
        [InlineData(1L, false)]
        [InlineData(0L, true)]
        public void Gap_is_reported_only_when_cursor_precedes_oldest_minus_one(
            long afterSequence,
            bool expectedGap)
        {
            var window = new ServerEventLiveWindow(2);
            window.AppendConsoleLog(CreateEntry("one"));
            window.AppendConsoleLog(CreateEntry("two"));
            window.AppendConsoleLog(CreateEntry("three"));

            var result = window.ReadAfter(afterSequence, 10);

            Assert.Equal(expectedGap, result.HasGap);
            Assert.Equal(new[] { 2L, 3L }, result.Entries.Select(entry => entry.Sequence));
        }

        [Fact]
        public void Empty_window_returns_no_sequences_or_entries()
        {
            var result = new ServerEventLiveWindow(2).ReadAfter(null, 10);

            Assert.Null(result.OldestSequence);
            Assert.Null(result.LatestSequence);
            Assert.Empty(result.Entries);
            Assert.False(result.HasGap);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Capacity_must_be_positive(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ServerEventLiveWindow(capacity));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Read_limit_must_be_positive(int limit)
        {
            var window = new ServerEventLiveWindow(2);

            Assert.Throws<ArgumentOutOfRangeException>(() => window.ReadAfter(null, limit));
        }

        private static ConsoleLogEntry CreateEntry(string message) =>
            new ConsoleLogEntry(
                "formatted:" + message,
                message,
                string.Empty,
                ConsoleLogType.Log,
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                0L);
    }
}
