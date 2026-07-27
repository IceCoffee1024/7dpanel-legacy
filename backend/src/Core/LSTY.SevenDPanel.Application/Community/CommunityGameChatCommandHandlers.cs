using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Application.Community
{
    public static class CommunityGameChatCommandHandlerSet
    {
        public static IReadOnlyList<IGameChatCommandHandler> Create(
            CommunityGameCommandRouter router)
        {
            if (router == null) throw new ArgumentNullException(nameof(router));
            return CommunityGameCommandDirectory.Definitions
                .Select(definition => (IGameChatCommandHandler)
                    new CommunityGameChatCommandHandler(definition, router))
                .ToArray();
        }
    }

    internal sealed class CommunityGameChatCommandHandler : IGameChatCommandHandler
    {
        private readonly CommunityGameCommandRouter router;

        public CommunityGameChatCommandHandler(
            CommunityGameCommandDefinition definition,
            CommunityGameCommandRouter router)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            this.router = router ?? throw new ArgumentNullException(nameof(router));
            Descriptor = new GameChatCommandDescriptor(
                definition.Name,
                definition.Aliases);
        }

        public GameChatCommandDescriptor Descriptor { get; }

        public GameChatCommandResult Handle(GameChatCommandContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var result = router.Route(
                Descriptor.Name,
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
