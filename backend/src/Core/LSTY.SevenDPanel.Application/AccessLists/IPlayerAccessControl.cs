using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IPlayerAccessControl
    {
        Task<IReadOnlyList<BanEntry>> GetBansAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<WhitelistEntry>> GetWhitelistAsync(CancellationToken cancellationToken);
        Task<AccessListMutationResult> UpsertBanAsync(BanRequest request, CancellationToken cancellationToken);
        Task<AccessListMutationResult> RemoveBanAsync(string playerId, CancellationToken cancellationToken);
        Task<AccessListMutationResult> UpsertWhitelistAsync(WhitelistRequest request, CancellationToken cancellationToken);
        Task<AccessListMutationResult> RemoveWhitelistAsync(string playerId, CancellationToken cancellationToken);
    }
}
