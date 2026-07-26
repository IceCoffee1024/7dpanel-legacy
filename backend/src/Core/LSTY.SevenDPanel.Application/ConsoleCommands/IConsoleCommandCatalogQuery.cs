using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public interface IConsoleCommandCatalogQuery
    {
        Task<ConsoleCommandCatalog> GetCatalogAsync(CancellationToken cancellationToken);
    }

    public sealed class ConsoleCommandCatalogUnavailableException : Exception
    {
        public ConsoleCommandCatalogUnavailableException()
            : base("The console command catalog is unavailable.")
        {
        }
    }
}
