using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Application.Announcements;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Announcements
{
    internal delegate Task DispatchAnnouncement(
        string operationName,
        Action action,
        TimeSpan startTimeout,
        CancellationToken cancellationToken);

    public sealed class SevenDaysAnnouncementGateway : IAnnouncementGateway
    {
        private static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(5);

        private readonly DispatchAnnouncement dispatch;
        private readonly Action<string> executeConsoleCommand;

        public SevenDaysAnnouncementGateway()
            : this(DispatchOnGameThreadAsync, ExecuteConsoleCommand)
        {
        }

        internal SevenDaysAnnouncementGateway(
            DispatchAnnouncement dispatch,
            Action<string> executeConsoleCommand)
        {
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.executeConsoleCommand = executeConsoleCommand ??
                throw new ArgumentNullException(nameof(executeConsoleCommand));
        }

        public Task SendAsync(
            AnnouncementMessage message,
            CancellationToken cancellationToken)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var command = BuildSayCommand(message.MessageText);
            return dispatch(
                "7DPanel.Announcements.Send",
                () => executeConsoleCommand(command),
                DefaultStartTimeout,
                cancellationToken);
        }

        private static string BuildSayCommand(string messageText)
        {
            if (messageText == null) throw new ArgumentNullException(nameof(messageText));
            var escaped = new StringBuilder(messageText.Length);
            foreach (var character in messageText)
            {
                switch (character)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '"':
                        escaped.Append("\\\"");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    default:
                        escaped.Append(character);
                        break;
                }
            }
            return "say \"" + escaped + "\"";
        }

        private static async Task DispatchOnGameThreadAsync(
            string operationName,
            Action action,
            TimeSpan startTimeout,
            CancellationToken cancellationToken)
        {
            await GameThreadDispatcher.Enqueue(
                    operationName,
                    () =>
                    {
                        action();
                        return true;
                    },
                    startTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static void ExecuteConsoleCommand(string command)
        {
            using (ConsoleCommandSourceContext.Push("7dpanel-announcement", null))
            {
                SdtdConsole.Instance.ExecuteSync(command, null);
            }
        }
    }
}
