using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ServerOperations
{
    internal delegate Task DispatchShutdown(
        string operationName,
        Action action,
        TimeSpan startTimeout,
        CancellationToken cancellationToken);

    public sealed class SevenDaysShutdownServerGateway : IShutdownServerGateway
    {
        private static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(5);

        private readonly DispatchShutdown dispatch;
        private readonly Action<string> executeConsoleCommand;

        public SevenDaysShutdownServerGateway()
            : this(DispatchOnGameThreadAsync, ExecuteConsoleCommand)
        {
        }

        internal SevenDaysShutdownServerGateway(
            DispatchShutdown dispatch,
            Action<string> executeConsoleCommand)
        {
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.executeConsoleCommand = executeConsoleCommand ??
                throw new ArgumentNullException(nameof(executeConsoleCommand));
        }

        public Task RequestShutdownAsync(CancellationToken cancellationToken)
        {
            return dispatch(
                "7DPanel.ServerOperations.Shutdown",
                () => executeConsoleCommand("shutdown"),
                DefaultStartTimeout,
                cancellationToken);
        }

        private static async Task DispatchOnGameThreadAsync(
            string operationName,
            Action action,
            TimeSpan startTimeout,
            CancellationToken cancellationToken)
        {
            await GameThreadDispatcher.Enqueue(
                    operationName,
                    () =>
                    {
                        action();
                        return true;
                    },
                    startTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static void ExecuteConsoleCommand(string command)
        {
            using (ConsoleCommandSourceContext.Push("7dpanel-server-operation", null))
            {
                SdtdConsole.Instance.ExecuteSync(command, null);
            }
        }
    }
}
