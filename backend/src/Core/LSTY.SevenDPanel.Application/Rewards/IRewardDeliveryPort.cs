using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.Rewards
{
    public interface IRewardDeliveryPort
    {
        Task<RewardDeliveryResult> DeliverAsync(
            RewardDeliveryCommand command,
            CancellationToken cancellationToken);
    }
}
