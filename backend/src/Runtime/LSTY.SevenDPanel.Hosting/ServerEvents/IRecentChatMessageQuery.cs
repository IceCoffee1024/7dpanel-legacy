using System.Collections.Generic;

namespace LSTY.SevenDPanel.Hosting.ServerEvents
{
    public interface IRecentChatMessageQuery
    {
        IReadOnlyList<ChatMessageEventData> ReadRecentChatMessages(int limit);
    }
}
