using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class ConsoleCommandTests
    {
        [Fact]
        public async Task Arbitrary_command_is_forwarded_without_normalization()
        {
            var gateway = new RecordingConsoleGateway();
            var useCase = new ExecuteConsoleCommandUseCase(gateway);
            const string rawCommand = "  say \"Hello  world\"  ";

            var result = await useCase.ExecuteAsync(
                new ConsoleCommandRequest("owner", rawCommand),
                TestContext.Current.CancellationToken);

            var request = Assert.Single(gateway.Requests);
            Assert.Equal("owner", request.ActorSubject);
            Assert.Equal(rawCommand, request.Command);
            Assert.Equal(rawCommand, result.Command);
            Assert.Equal(new[] { "command output" }, result.Output);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Empty_actor_is_rejected_before_dispatch(string actorSubject)
        {
            var gateway = new RecordingConsoleGateway();
            var useCase = new ExecuteConsoleCommandUseCase(gateway);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                useCase.ExecuteAsync(
                    new ConsoleCommandRequest(actorSubject, "version"),
                    TestContext.Current.CancellationToken));

            Assert.Empty(gateway.Requests);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Empty_command_is_rejected_before_dispatch(string command)
        {
            var gateway = new RecordingConsoleGateway();
            var useCase = new ExecuteConsoleCommandUseCase(gateway);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                useCase.ExecuteAsync(
                    new ConsoleCommandRequest("owner", command),
                    TestContext.Current.CancellationToken));

            Assert.Empty(gateway.Requests);
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingConsoleGateway : IConsoleCommandGateway
        {
            public List<ConsoleCommandRequest> Requests { get; } = new();

            public Task<ConsoleCommandResult> ExecuteAsync(
                ConsoleCommandRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(new ConsoleCommandResult(
                    request.Command,
                    new[] { "command output" }));
            }
        }
    }
}
