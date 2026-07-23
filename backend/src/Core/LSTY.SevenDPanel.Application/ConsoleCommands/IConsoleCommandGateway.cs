using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public interface IConsoleCommandGateway
    {
        Task<ConsoleCommandResult> ExecuteAsync(
            ConsoleCommandRequest request,
            CancellationToken cancellationToken);
    }

    public sealed class ConsoleCommandQueueFullException : System.Exception
    {
        public ConsoleCommandQueueFullException()
            : base("The console command queue is full.")
        {
        }
    }

    public sealed class ConsoleCommandUnavailableException : System.Exception
    {
        public ConsoleCommandUnavailableException()
            : base("The console command service is unavailable.")
        {
        }
    }
}