using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Diagnostics
{
    public sealed class ChatCommandTestConsoleCommand : ConsoleCmdAbstract
    {
        public override int DefaultPermissionLevel => 0;

        public override string[] getCommands() => new[] { "7dp-test" };

        public override string getDescription() =>
            "Runs the guarded 7DPanel chat-command diagnostics.";

        public override string getHelp() =>
            "Usage: 7dp-test chat <status|virtual|boundary|all>";

        public override void Execute(List<string> parameters, CommandSenderInfo senderInfo)
        {
            var request = parameters ?? new List<string>();
            foreach (var line in ChatCommandTestConsoleBridge.Execute(request))
                Log.Out("[7DPanel test] " + line);
        }
    }

    internal static class ChatCommandTestConsoleBridge
    {
        private static readonly object Sync = new object();
        private static Func<IReadOnlyList<string>, IReadOnlyList<string>>? execute;

        public static IDisposable Register(
            Func<IReadOnlyList<string>, IReadOnlyList<string>> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (Sync)
            {
                if (execute != null)
                    throw new InvalidOperationException("The chat-command test console bridge is already registered.");
                execute = handler;
                return new Registration(handler);
            }
        }

        public static IReadOnlyList<string> Execute(IReadOnlyList<string> parameters)
        {
            Func<IReadOnlyList<string>, IReadOnlyList<string>>? handler;
            lock (Sync) handler = execute;
            if (handler == null)
                return new[] { "Diagnostics are unavailable because the Mod runtime is not started." };

            try
            {
                return handler(parameters ?? Array.Empty<string>()) ??
                       new[] { "Diagnostics returned no result." };
            }
            catch (Exception exception)
            {
                return new[] { "Diagnostics failed: " + exception.GetType().Name + "." };
            }
        }

        private sealed class Registration : IDisposable
        {
            private Func<IReadOnlyList<string>, IReadOnlyList<string>>? owner;

            public Registration(Func<IReadOnlyList<string>, IReadOnlyList<string>> owner) =>
                this.owner = owner;

            public void Dispose()
            {
                lock (Sync)
                {
                    if (owner != null && ReferenceEquals(execute, owner)) execute = null;
                    owner = null;
                }
            }
        }
    }
}
