using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.Chat
{
    public sealed class GameChatCommandDescriptor
    {
        public GameChatCommandDescriptor(string name, IReadOnlyList<string> aliases)
        {
            Name = RequireName(name, nameof(name));
            if (aliases == null) throw new ArgumentNullException(nameof(aliases));
            Aliases = aliases.Select(alias => RequireName(alias, nameof(aliases))).ToArray();
            if (Aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Aliases.Count)
                throw new ArgumentException("Command aliases must be unique.", nameof(aliases));
            if (Aliases.Contains(Name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("A command alias cannot repeat its name.", nameof(aliases));
        }

        public string Name { get; }
        public IReadOnlyList<string> Aliases { get; }

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
        private readonly IReadOnlyDictionary<string, IGameChatCommandHandler> handlers;

        public GameChatCommandCatalog(IEnumerable<IGameChatCommandHandler> handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));
            var byName = new Dictionary<string, IGameChatCommandHandler>(StringComparer.OrdinalIgnoreCase);
            var descriptors = new List<GameChatCommandDescriptor>();
            foreach (var handler in handlers)
            {
                if (handler == null) throw new ArgumentException("Command handlers cannot be null.", nameof(handlers));
                descriptors.Add(handler.Descriptor);
                Add(byName, handler.Descriptor.Name, handler);
                foreach (var alias in handler.Descriptor.Aliases) Add(byName, alias, handler);
            }

            this.handlers = byName;
            Commands = descriptors.AsReadOnly();
        }

        public IReadOnlyList<GameChatCommandDescriptor> Commands { get; }

        public GameChatCommandResult Handle(string commandName, GameChatCommandContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(commandName) || !handlers.TryGetValue(commandName.Trim(), out var handler))
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

        private static void Add(
            IDictionary<string, IGameChatCommandHandler> handlers,
            string name,
            IGameChatCommandHandler handler)
        {
            if (handlers.ContainsKey(name))
                throw new ArgumentException("Command names and aliases must be unique.", nameof(handler));
            handlers.Add(name, handler);
        }
    }

    public sealed class HelpGameChatCommandHandler : IGameChatCommandHandler
    {
        private readonly Func<bool> isAvailable;

        public HelpGameChatCommandHandler(Func<bool> isAvailable)
        {
            this.isAvailable = isAvailable ?? throw new ArgumentNullException(nameof(isAvailable));
            Descriptor = new GameChatCommandDescriptor("help", Array.Empty<string>());
        }

        public GameChatCommandDescriptor Descriptor { get; }

        public GameChatCommandResult Handle(GameChatCommandContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Arguments.Count != 0) return GameChatCommandResult.InvalidArguments();
            if (!isAvailable()) return GameChatCommandResult.Unavailable();
            return GameChatCommandResult.HelpSucceeded(new[] { Descriptor.Name });
        }
    }
}
