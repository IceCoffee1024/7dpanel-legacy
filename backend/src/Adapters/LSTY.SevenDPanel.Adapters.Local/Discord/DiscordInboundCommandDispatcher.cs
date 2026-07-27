using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Discord;

namespace LSTY.SevenDPanel.Adapters.Local.Discord
{
    public sealed class DiscordInboundCommandDispatcher : IDiscordInboundCommandDispatcher
    {
        private readonly GetOverviewUseCase overview;
        private readonly GetOnlinePlayersUseCase onlinePlayers;

        public DiscordInboundCommandDispatcher(
            GetOverviewUseCase overview,
            GetOnlinePlayersUseCase onlinePlayers)
        {
            this.overview = overview ?? throw new ArgumentNullException(nameof(overview));
            this.onlinePlayers = onlinePlayers ??
                throw new ArgumentNullException(nameof(onlinePlayers));
        }

        public async Task<DiscordCommandDispatchResult> DispatchAsync(
            DiscordServerStatusCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var snapshot = await overview.ExecuteAsync(OverviewAudience.NonOwner, cancellationToken)
                .ConfigureAwait(false);
            return DiscordCommandDispatchResult.Succeeded(
                "服务器状态：" + snapshot.Availability);
        }

        public async Task<DiscordCommandDispatchResult> DispatchAsync(
            DiscordOnlinePlayersCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var snapshot = await onlinePlayers.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var names = snapshot.Players.Select(player => player.Name).ToArray();
            var content = names.Length == 0
                ? "当前没有在线玩家。"
                : "在线玩家（" + names.Length + "）：" + string.Join("、", names);
            return DiscordCommandDispatchResult.Succeeded(
                content.Length <= 2000 ? content : content.Substring(0, 1997) + "...");
        }
    }
}
