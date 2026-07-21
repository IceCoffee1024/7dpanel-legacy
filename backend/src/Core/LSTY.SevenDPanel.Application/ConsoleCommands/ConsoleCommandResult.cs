using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public sealed class ConsoleCommandResult
    {
        public ConsoleCommandResult(string command, IEnumerable<string> output)
        {
            if (string.IsNullOrWhiteSpace(command))
                throw new ArgumentException("A console command is required.", nameof(command));
            if (output == null) throw new ArgumentNullException(nameof(output));

            Command = command;
            Output = output.ToArray();
        }

        public string Command { get; }
        public IReadOnlyList<string> Output { get; }
    }
}
