using System;
using System.Threading;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands
{
    internal static class ConsoleCommandSourceContext
    {
        [ThreadStatic]
        private static SourceValue? current;

        public static string? Source => current?.Source;
        public static string? ActorSubject => current?.ActorSubject;

        public static IDisposable Push(string source, string? actorSubject)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("A console command source is required.", nameof(source));
            var previous = current;
            current = new SourceValue(source, actorSubject);
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private SourceValue? previous;
            private int disposed;

            public Scope(SourceValue? previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                current = previous;
                previous = null;
            }
        }

        private sealed class SourceValue
        {
            public SourceValue(string source, string? actorSubject)
            {
                Source = source;
                ActorSubject = actorSubject;
            }

            public string Source { get; }
            public string? ActorSubject { get; }
        }
    }
}