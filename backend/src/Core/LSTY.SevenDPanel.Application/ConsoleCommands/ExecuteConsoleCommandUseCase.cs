using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public sealed class ExecuteConsoleCommandUseCase
    {
        private readonly IConsoleCommandGateway gateway;

        public ExecuteConsoleCommandUseCase(IConsoleCommandGateway gateway)
        {
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public Task<ConsoleCommandResult> ExecuteAsync(
            ConsoleCommandRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return gateway.ExecuteAsync(request, cancellationToken);
        }
    }
}
