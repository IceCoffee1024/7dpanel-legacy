using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ConsoleCommandTests
    {
        [Fact]
        public async Task Version_command_is_normalized_before_dispatch()
        {
            var gateway = new RecordingConsoleGateway();
            var useCase = new ExecuteConsoleCommandUseCase(gateway);

            var result = await useCase.ExecuteAsync(
                "  VERSION  ",
                TestContext.Current.CancellationToken);

            Assert.Equal("version", gateway.Command);
            Assert.Equal("version", result.Command);
            Assert.Equal(new[] { "version output" }, result.Output);
        }

        [Fact]
        public async Task Unsupported_command_is_rejected_before_dispatch()
        {
            var gateway = new RecordingConsoleGateway();
            var useCase = new ExecuteConsoleCommandUseCase(gateway);

            var exception = await Assert.ThrowsAsync<ConsoleCommandNotSupportedException>(() =>
                useCase.ExecuteAsync("kick player", TestContext.Current.CancellationToken));

            Assert.Equal("kick player", exception.Command);
            Assert.Null(gateway.Command);
        }

        private sealed class RecordingConsoleGateway : IRestrictedConsoleGateway
        {
            public string? Command { get; private set; }

            public Task<ConsoleCommandResult> ExecuteVersionAsync(
                CancellationToken cancellationToken)
            {
                const string command = ExecuteConsoleCommandUseCase.VersionCommand;
                Command = command;
                return Task.FromResult(new ConsoleCommandResult(
                    command,
                    new[] { "version output" }));
            }
        }
    }
}
