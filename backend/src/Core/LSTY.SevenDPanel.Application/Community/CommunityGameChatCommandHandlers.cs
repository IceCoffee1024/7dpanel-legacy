using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Application.Community
{
    public static class CommunityGameChatCommandHandlerSet
    {
        public static IReadOnlyList<IGameChatCommandHandler> Create(
            CommunityGameCommandRouter router,
            HomeTeleportExperience? homeExperience = null)
        {
            if (router == null) throw new ArgumentNullException(nameof(router));
            return CommunityGameCommandDirectory.Definitions
                .Select(definition => (IGameChatCommandHandler)
                    new CommunityGameChatCommandHandler(
                        definition,
                        CommandName(definition, homeExperience),
                        router))
                .ToArray();
        }

        public static IReadOnlyList<IGameChatCommandHandler> Create(
            CommunityGameCommandRouter router,
            CommunityGameCommandConfiguration configuration)
        {
            if (router == null) throw new ArgumentNullException(nameof(router));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            return CommunityGameCommandDirectory.Definitions
                .Select(definition =>
                {
                    var setting = configuration.Get(definition.Id);
                    return (IGameChatCommandHandler)new CommunityGameChatCommandHandler(
                        definition,
                        setting.Name,
                        setting.Aliases,
                        router);
                })
                .ToArray();
        }

        private static string CommandName(
            CommunityGameCommandDefinition definition,
            HomeTeleportExperience? experience) => definition.Id switch
            {
                CommunityGameCommandId.Homes when experience != null => experience.ListCommandName,
                CommunityGameCommandId.SetHome when experience != null => experience.SetCommandName,
                CommunityGameCommandId.DeleteHome when experience != null => experience.DeleteCommandName,
                CommunityGameCommandId.Home when experience != null => experience.TeleportCommandName,
                _ => definition.Name
            };
    }

    internal sealed class CommunityGameChatCommandHandler : IGameChatCommandHandler
    {
        private readonly CommunityGameCommandRouter router;
        private readonly CommunityGameCommandDefinition definition;

        public CommunityGameChatCommandHandler(
            CommunityGameCommandDefinition definition,
            string commandName,
            CommunityGameCommandRouter router)
            : this(
                definition,
                commandName,
                definition == null ? Array.Empty<string>() : definition.Aliases,
                router)
        {
        }

        public CommunityGameChatCommandHandler(
            CommunityGameCommandDefinition definition,
            string commandName,
            IReadOnlyList<string> aliases,
            CommunityGameCommandRouter router)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            this.definition = definition;
            this.router = router ?? throw new ArgumentNullException(nameof(router));
            Descriptor = new GameChatCommandDescriptor(
                definition.Id.ToString(),
                commandName,
                aliases ?? throw new ArgumentNullException(nameof(aliases)),
                true);
        }

        public GameChatCommandDescriptor Descriptor { get; }

        public GameChatCommandResult Handle(GameChatCommandContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var result = router.Route(
                definition,
                new CommunityGameCommandContext(
                    context.CrossplatformId,
                    context.DisplayName,
                    context.Arguments));
            var privateMessages = new[] { result.Code }
                .Concat(result.Messages)
                .ToArray();

            // The shared chat result contract delivers non-empty messages verbatim.
            // Prefixing the stable Community code keeps the existing private reply path
            // without introducing a second chat event subscription.
            return GameChatCommandResult.HelpSucceeded(privateMessages);
        }
    }
}
