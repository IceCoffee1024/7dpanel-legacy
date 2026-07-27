using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.Announcements
{
    public interface IAnnouncementGateway
    {
        Task SendAsync(
            AnnouncementMessage message,
            CancellationToken cancellationToken);
    }
}
