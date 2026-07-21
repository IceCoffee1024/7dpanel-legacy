using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public interface IRestrictedConsoleGateway
    {
        Task<ConsoleCommandResult> ExecuteVersionAsync(
            CancellationToken cancellationToken);
    }
}
