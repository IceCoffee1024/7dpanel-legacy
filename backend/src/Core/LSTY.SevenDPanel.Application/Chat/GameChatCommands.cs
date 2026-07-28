using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LSTY.SevenDPanel.Application.Chat
{
    public sealed class GameChatCommandDescriptor
    {
        public GameChatCommandDescriptor(string name, IReadOnlyList<string> aliases)
            : this(name, name, aliases, true)
        {
        }

        public GameChatCommandDescriptor(
            string commandId,
            string name,
            IReadOnlyList<string> aliases,
            bool isEnabled)
        {
            CommandId = RequireName(commandId, nameof(commandId));
            Name = RequireName(name, nameof(name));
            if (aliases == null) throw new ArgumentNullException(nameof(aliases));
            Aliases = Array.AsReadOnly(aliases.Select(alias => RequireName(alias, nameof(aliases))).ToArray());
            if (Aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Aliases.Count)
                throw new ArgumentException("Command aliases must be unique.", nameof(aliases));
            if (Aliases.Contains(Name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("A command alias cannot repeat its name.", nameof(aliases));
            IsEnabled = isEnabled;
        }

        public string CommandId { get; }
        public string Name { get; }
        public IReadOnlyList<string> Aliases { get; }
        public bool IsEnabled { get; }

        private static string RequireName(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A command name is required.", parameterName);
            var normalized = value.Trim();
            if (normalized.Any(char.IsWhiteSpace))
                throw new ArgumentException("Command names cannot contain whitespace.", parameterName);
            return normalized;
        }
    }

    public sealed class GameChatCommandContext
    {
        public GameChatCommandContext(
            string crossplatformId,
            string displayName,
            IEnumerable<string> arguments)
        {
            CrossplatformId = ChatMuteRecord.RequireText(crossplatformId, nameof(crossplatformId));
            DisplayName = ChatMuteRecord.RequireText(displayName, nameof(displayName));
            Arguments = (arguments ?? throw new ArgumentNullException(nameof(arguments))).ToArray();
        }

        public string CrossplatformId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<string> Arguments { get; }
    }

    public sealed class GameChatCommandResult
    {
        private GameChatCommandResult(bool isHandled, string? code, IEnumerable<string> messages)
        {
            IsHandled = isHandled;
            Code = code;
            Messages = messages.ToArray();
        }

        public bool IsHandled { get; }
        public string? Code { get; }
        public IReadOnlyList<string> Messages { get; }

        public static GameChatCommandResult Unhandled() =>
            new GameChatCommandResult(false, null, Array.Empty<string>());

        public static GameChatCommandResult HelpSucceeded(IEnumerable<string> messages) =>
            Handled("chat.command.help.succeeded", messages);

        public static GameChatCommandResult InvalidArguments() =>
            Handled("chat.command.invalid_arguments", Array.Empty<string>());

        public static GameChatCommandResult Unavailable() =>
            Handled("chat.command.unavailable", Array.Empty<string>());

        public static GameChatCommandResult Failed() =>
            Handled("chat.command.failed", Array.Empty<string>());

        private static GameChatCommandResult Handled(string code, IEnumerable<string> messages) =>
            new GameChatCommandResult(true, code, messages ?? throw new ArgumentNullException(nameof(messages)));
    }

    public interface IGameChatCommandHandler
    {
        GameChatCommandDescriptor Descriptor { get; }
        GameChatCommandResult Handle(GameChatCommandContext context);
    }

    public sealed class GameChatCommandCatalog
    {
        private Snapshot snapshot;

        public GameChatCommandCatalog(IEnumerable<IGameChatCommandHandler> handlers)
        {
            snapshot = BuildSnapshot(handlers);
        }

        public IReadOnlyList<GameChatCommandDescriptor> Commands => Volatile.Read(ref snapshot).Commands;

        public void Replace(IEnumerable<IGameChatCommandHandler> handlers)
        {
            var replacement = BuildSnapshot(handlers);
            Interlocked.Exchange(ref snapshot, replacement);
        }

        public void Rebuild(
            Func<IReadOnlyList<IGameChatCommandHandler>, IEnumerable<IGameChatCommandHandler>> rebuild)
        {
            if (rebuild == null) throw new ArgumentNullException(nameof(rebuild));
            while (true)
            {
                var current = Volatile.Read(ref snapshot);
                var replacementHandlers = rebuild(current.Handlers)
                    ?? throw new InvalidOperationException("The command catalog rebuild returned null handlers.");
                var replacement = BuildSnapshot(replacementHandlers);
                if (ReferenceEquals(Interlocked.CompareExchange(ref snapshot, replacement, current), current))
                    return;
            }
        }

        public GameChatCommandResult Handle(string commandName, GameChatCommandContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var current = Volatile.Read(ref snapshot);
            if (string.IsNullOrWhiteSpace(commandName) ||
                !current.HandlersByName.TryGetValue(commandName.Trim(), out var handler))
                return GameChatCommandResult.Unhandled();
            try
            {
                return handler.Handle(context) ?? GameChatCommandResult.Failed();
            }
            catch
            {
                return GameChatCommandResult.Failed();
            }
        }

        private static Snapshot BuildSnapshot(IEnumerable<IGameChatCommandHandler> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));
            var byName = new Dictionary<string, IGameChatCommandHandler>(StringComparer.OrdinalIgnoreCase);
            var handlerList = new List<IGameChatCommandHandler>();
            var descriptors = new List<GameChatCommandDescriptor>();
            foreach (var handler in handlers)
            {
                if (handler == null) throw new ArgumentException("Command handlers cannot be null.", nameof(handlers));
                if (handler.Descriptor == null)
                    throw new ArgumentException("Command handler descriptors cannot be null.", nameof(handlers));
                handlerList.Add(handler);
                descriptors.Add(handler.Descriptor);
                Add(byName, handler.Descriptor.Name, handler);
                foreach (var alias in handler.Descriptor.Aliases) Add(byName, alias, handler);
            }

            return new Snapshot(
                byName,
                handlerList.AsReadOnly(),
                descriptors.AsReadOnly());
        }

        private static void Add(
            IDictionary<string, IGameChatCommandHandler> handlers,
            string name,
            IGameChatCommandHandler handler)
        {
            if (handlers.ContainsKey(name))
                throw new ArgumentException("Command names and aliases must be unique.", nameof(handler));
            handlers.Add(name, handler);
        }

        private sealed class Snapshot
        {
            public Snapshot(
                IReadOnlyDictionary<string, IGameChatCommandHandler> handlersByName,
                IReadOnlyList<IGameChatCommandHandler> handlers,
                IReadOnlyList<GameChatCommandDescriptor> commands)
            {
                HandlersByName = handlersByName;
                Handlers = handlers;
                Commands = commands;
            }

            public IReadOnlyDictionary<string, IGameChatCommandHandler> HandlersByName { get; }
            public IReadOnlyList<IGameChatCommandHandler> Handlers { get; }
            public IReadOnlyList<GameChatCommandDescriptor> Commands { get; }
        }
    }

    public sealed class HelpGameChatCommandHandler : IGameChatCommandHandler
    {
        private readonly Func<bool> isAvailable;
        private readonly Func<IReadOnlyList<GameChatCommandDescriptor>>? getCommands;

        public HelpGameChatCommandHandler(
            Func<bool> isAvailable,
            Func<IReadOnlyList<GameChatCommandDescriptor>>? getCommands = null)
        {
            this.isAvailable = isAvailable ?? throw new ArgumentNullException(nameof(isAvailable));
            this.getCommands = getCommands;
            Descriptor = new GameChatCommandDescriptor(
                "Help", "help", Array.Empty<string>(), true);
        }

        public GameChatCommandDescriptor Descriptor { get; }

        public GameChatCommandResult Handle(GameChatCommandContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Arguments.Count != 0) return GameChatCommandResult.InvalidArguments();
            if (!isAvailable()) return GameChatCommandResult.Unavailable();
            var commands = getCommands?.Invoke();
            return GameChatCommandResult.HelpSucceeded(commands == null
                ? new[] { Descriptor.Name }
                : commands
                    .Where(command => command.IsEnabled)
                    .Select(command => command.Aliases.Count == 0
                        ? command.Name
                        : command.Name + " (" + string.Join(", ", command.Aliases) + ")"));
        }
    }
}
