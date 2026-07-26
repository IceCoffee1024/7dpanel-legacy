using System;

namespace LSTY.SevenDPanel.Application.Chat
{
    public sealed class GetChatHistoryUseCase
    {
        private readonly IChatHistoryStore store;
        public GetChatHistoryUseCase(IChatHistoryStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        public ChatHistoryPage Execute(ChatHistoryQuery query) =>
            store.GetHistory(query ?? throw new ArgumentNullException(nameof(query)));
    }

    public sealed class GetChatSettingsUseCase
    {
        private readonly IChatSettingsStore store;
        public GetChatSettingsUseCase(IChatSettingsStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        public ChatSettings Execute() => store.Get();
    }

    public sealed class GetColoredChatSettingsUseCase
    {
        private readonly IColoredChatStore store;
        public GetColoredChatSettingsUseCase(IColoredChatStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        public ColoredChatSettings Execute() => store.GetSettings();
    }

    public sealed class GetColoredChatProfilesUseCase
    {
        private readonly IColoredChatStore store;
        public GetColoredChatProfilesUseCase(IColoredChatStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        public ColoredChatProfilePage Execute(ColoredChatProfileQuery query) =>
            store.GetProfiles(query ?? throw new ArgumentNullException(nameof(query)));
    }
}
