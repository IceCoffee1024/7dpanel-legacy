using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application.ConsoleCommands;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands
{
    public sealed class SevenDaysConsoleCommandCatalogQuery : IConsoleCommandCatalogQuery
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private readonly Action<string> log;

        public SevenDaysConsoleCommandCatalogQuery()
            : this(null)
        {
        }

        public SevenDaysConsoleCommandCatalogQuery(Action<string>? log)
        {
            this.log = log ?? (_ => { });
        }

        public Task<ConsoleCommandCatalog> GetCatalogAsync(CancellationToken cancellationToken) =>
            GameThreadDispatcher.Enqueue(
                "7DPanel.Console.Catalog",
                CaptureOnGameThread,
                DispatchTimeout,
                cancellationToken);

        internal static ConsoleCommandCatalogEntry? TryReadEntry(
            Func<string[]?> readCommands,
            Func<string?> readPrimaryCommand,
            Func<string?> readDescription,
            Func<string?> readHelp,
            Func<string[], int> readPermissionLevel)
        {
            string[]? rawCommands;
            try { rawCommands = readCommands(); }
            catch { return null; }
            if (rawCommands == null) return null;

            var names = rawCommands
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (names.Length == 0) return null;

            string? preferred = null;
            try { preferred = readPrimaryCommand(); } catch { }
            var preferredName = string.IsNullOrWhiteSpace(preferred)
                ? null
                : preferred!.Trim();
            var name = names.FirstOrDefault(candidate =>
                preferredName != null &&
                string.Equals(candidate, preferredName, StringComparison.OrdinalIgnoreCase))
                ?? names[0];
            var aliases = names
                .Where(candidate => !string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            string? description = null;
            string? help = null;
            int? permissionLevel = null;
            try { description = NullIfWhiteSpace(readDescription()); } catch { }
            try { help = NullIfWhiteSpace(readHelp()); } catch { }
            try { permissionLevel = readPermissionLevel(rawCommands); } catch { }

            return new ConsoleCommandCatalogEntry(
                name,
                aliases,
                description,
                help,
                permissionLevel);
        }

        internal static IReadOnlyList<ConsoleCommandCatalogEntry> Sort(
            IEnumerable<ConsoleCommandCatalogEntry> entries) =>
            entries
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                .ToArray();

        private ConsoleCommandCatalog CaptureOnGameThread()
        {
            var console = global::SdtdConsole.Instance;
            var permissionCommands = global::GameManager.Instance?.adminTools?.Commands;
            if (console == null || permissionCommands == null)
                throw new ConsoleCommandCatalogUnavailableException();

            var entries = new List<ConsoleCommandCatalogEntry>();
            foreach (var command in console.GetCommands())
            {
                if (command == null) continue;
                var entry = TryReadEntry(
                    command.GetCommands,
                    () => command.PrimaryCommand,
                    command.GetDescription,
                    command.GetHelp,
                    permissionCommands.GetCommandPermissionLevel);
                if (entry != null)
                {
                    entries.Add(entry);
                    continue;
                }

                try { log("A console command catalog entry was skipped because it had no valid name."); }
                catch { }
            }

            return new ConsoleCommandCatalog(DateTimeOffset.UtcNow, Sort(entries));
        }

        private static string? NullIfWhiteSpace(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
