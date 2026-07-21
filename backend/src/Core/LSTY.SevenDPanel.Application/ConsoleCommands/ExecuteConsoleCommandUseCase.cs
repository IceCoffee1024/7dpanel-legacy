using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public sealed class ExecuteConsoleCommandUseCase
    {
        public const string VersionCommand = "version";

        private readonly IRestrictedConsoleGateway gateway;

        public ExecuteConsoleCommandUseCase(IRestrictedConsoleGateway gateway)
        {
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public Task<ConsoleCommandResult> ExecuteAsync(
            string command,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("A console command is required.", nameof(command));

            var normalizedCommand = command.Trim();
            if (!string.Equals(
                    normalizedCommand,
                    VersionCommand,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ConsoleCommandNotSupportedException(normalizedCommand);
            }

            return gateway.ExecuteVersionAsync(cancellationToken);
        }
    }

    public sealed class ConsoleCommandNotSupportedException : Exception
    {
        public ConsoleCommandNotSupportedException(string command)
            : base("The console command is not supported: " + command)
        {
            Command = command;
        }

        public string Command { get; }
    }

    public sealed class ConsoleCommandBusyException : Exception
    {
        public ConsoleCommandBusyException()
            : base("Another console command is already in progress.")
        {
        }
    }
}
