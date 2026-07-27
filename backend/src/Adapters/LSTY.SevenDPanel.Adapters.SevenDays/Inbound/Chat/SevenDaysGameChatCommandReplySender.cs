using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Chat
{
    public sealed class SevenDaysGameChatCommandReplySender
    {
        public void Send(ClientInfo clientInfo, IEnumerable<string> messages)
        {
            if (clientInfo == null) throw new ArgumentNullException(nameof(clientInfo));
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            foreach (var message in messages)
            {
                if (string.IsNullOrWhiteSpace(message)) continue;
                var package = NetPackageManager.GetPackage<NetPackageChat>().Setup(
                    EChatType.Whisper, -1, message, null, EMessageSender.None,
                    GeneratedTextManager.BbCodeSupportMode.Supported);
                clientInfo.SendPackage(package);
            }
        }
    }
}
