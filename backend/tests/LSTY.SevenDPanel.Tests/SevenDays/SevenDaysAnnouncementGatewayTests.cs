using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Announcements;
using LSTY.SevenDPanel.Application.Announcements;
using Xunit;

namespace LSTY.SevenDPanel.Tests.SevenDays
{
    public sealed class SevenDaysAnnouncementGatewayTests
    {
        [Fact]
        public async Task Announcement_service_validates_plain_text_before_calling_the_gateway()
        {
            var gateway = new RecordingAnnouncementGateway();
            var service = new AnnouncementService(gateway);

            Assert.Equal(
                "announcement_invalid",
                (await Assert.ThrowsAsync<AnnouncementValidationException>(() =>
                    service.SendAsync("", CancellationToken.None))).Code);
            Assert.Equal(
                "announcement_invalid",
                (await Assert.ThrowsAsync<AnnouncementValidationException>(() =>
                    service.SendAsync(new string('a', 501), CancellationToken.None))).Code);
            Assert.Empty(gateway.Messages);
        }

        [Fact]
        public async Task Announcement_service_calls_the_typed_gateway_with_exact_plain_text()
        {
            var gateway = new RecordingAnnouncementGateway();
            var service = new AnnouncementService(gateway);
            const string text = "hello survivors";

            await service.SendAsync(text, CancellationToken.None);

            Assert.Equal(text, Assert.Single(gateway.Messages).MessageText);
        }

        [Fact]
        public async Task Gateway_escapes_plain_text_into_one_fixed_say_command_on_the_game_thread()
        {
            string? operationName = null;
            TimeSpan? startTimeout = null;
            CancellationToken observedCancellation = default;
            var commands = new List<string>();
            using var cancellation = new CancellationTokenSource();
            var gateway = new SevenDaysAnnouncementGateway(
                (operation, action, timeout, cancellationToken) =>
                {
                    operationName = operation;
                    startTimeout = timeout;
                    observedCancellation = cancellationToken;
                    action();
                    return Task.CompletedTask;
                },
                commands.Add);

            await gateway.SendAsync(
                new AnnouncementMessage("hello \"survivors\"\\path\r\nshutdown"),
                cancellation.Token);

            Assert.Equal("7DPanel.Announcements.Send", operationName);
            Assert.Equal(TimeSpan.FromSeconds(5), startTimeout);
            Assert.Equal(cancellation.Token, observedCancellation);
            var command = Assert.Single(commands);
            Assert.Equal(
                "say \"hello \\\"survivors\\\"\\\\path\\r\\nshutdown\"",
                command);
            Assert.DoesNotContain('\r', command);
            Assert.DoesNotContain('\n', command);
        }

        private sealed class RecordingAnnouncementGateway : IAnnouncementGateway
        {
            public List<AnnouncementMessage> Messages { get; } = new();

            public Task SendAsync(
                AnnouncementMessage message,
                CancellationToken cancellationToken)
            {
                Messages.Add(message);
                return Task.CompletedTask;
            }
        }
    }
}
