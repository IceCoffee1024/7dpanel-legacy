using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IGamePermissionControl
    {
        Task<IReadOnlyList<GameAdminEntry>> GetAdminsAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<CommandPermissionEntry>> GetCommandsAsync(CancellationToken cancellationToken);
        Task<GamePermissionMutationResult> UpsertAdminAsync(GameAdminEntry entry, CancellationToken cancellationToken);
        Task<GamePermissionMutationResult> RemoveAdminAsync(string playerId, CancellationToken cancellationToken);
        Task<GamePermissionMutationResult> UpsertCommandAsync(string command, int level, CancellationToken cancellationToken);
        Task<GamePermissionMutationResult> RemoveCommandAsync(string command, CancellationToken cancellationToken);
    }
}
