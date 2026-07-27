using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.Announcements
{
    public sealed class AnnouncementService
    {
        private readonly IAnnouncementGateway gateway;

        public AnnouncementService(IAnnouncementGateway gateway)
        {
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        public Task SendAsync(string messageText, CancellationToken cancellationToken)
        {
            if (messageText == null || messageText.Length < 1 || messageText.Length > 500)
                throw new AnnouncementValidationException();

            return gateway.SendAsync(
                new AnnouncementMessage(messageText),
                cancellationToken);
        }
    }
}
