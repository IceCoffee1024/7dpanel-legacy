using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application.ConsoleCommands;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands
{
    public sealed class SevenDaysRestrictedConsoleGateway : IRestrictedConsoleGateway
    {
        private static readonly TimeSpan MainThreadStartTimeout = TimeSpan.FromSeconds(5);
        private int inFlight;

        public async Task<ConsoleCommandResult> ExecuteVersionAsync(
            CancellationToken cancellationToken)
        {
            const string command = ExecuteConsoleCommandUseCase.VersionCommand;
            if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
                throw new ConsoleCommandBusyException();

            try
            {
                return await GameThreadDispatcher.Enqueue(
                        "7DPanel.Console." + command,
                        () =>
                        {
                            var output = SdtdConsole.Instance.ExecuteSync(command, null);
                            return new ConsoleCommandResult(
                                command,
                                output ?? (IEnumerable<string>)Array.Empty<string>());
                        },
                        MainThreadStartTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref inFlight, 0);
            }
        }
    }
}
