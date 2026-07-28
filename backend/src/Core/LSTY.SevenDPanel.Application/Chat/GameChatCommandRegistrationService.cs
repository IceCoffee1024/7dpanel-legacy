using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application.Chat
{
    public sealed class GameChatCommandRegistrationService
    {
        private readonly GameChatCommandCatalog catalog;
        private readonly Func<IEnumerable<IGameChatCommandHandler>> createHandlers;

        public GameChatCommandRegistrationService(
            GameChatCommandCatalog catalog,
            Func<IEnumerable<IGameChatCommandHandler>> createHandlers)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.createHandlers = createHandlers ?? throw new ArgumentNullException(nameof(createHandlers));
        }

        public IReadOnlyList<GameChatCommandDescriptor> Commands => catalog.Commands;

        public void Rebuild() => catalog.Replace(createHandlers());
    }
}
