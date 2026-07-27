using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application.Backups;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Backups
{
    internal delegate Task DispatchWorldSave(
        string operationName,
        Action action,
        TimeSpan startTimeout,
        CancellationToken cancellationToken);

    public sealed class SevenDaysWorldSaveGateway : IWorldSaveGateway
    {
        private static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(5);

        private readonly DispatchWorldSave dispatch;
        private readonly Action saveWorld;
        private readonly Action confirmCommit;

        public SevenDaysWorldSaveGateway()
            : this(
                DispatchOnGameThreadAsync,
                () => GameManager.Instance.SaveWorld(),
                () => SaveDataUtils.SaveDataManager.CommitSync())
        {
        }

        internal SevenDaysWorldSaveGateway(
            DispatchWorldSave dispatch,
            Action saveWorld,
            Action confirmCommit)
        {
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.saveWorld = saveWorld ?? throw new ArgumentNullException(nameof(saveWorld));
            this.confirmCommit = confirmCommit ?? throw new ArgumentNullException(nameof(confirmCommit));
        }

        public Task SaveCurrentWorldAsync(CancellationToken cancellationToken)
        {
            return dispatch(
                "7DPanel.Backups.SaveWorld",
                () =>
                {
                    saveWorld();
                    confirmCommit();
                },
                DefaultStartTimeout,
                cancellationToken);
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
    }
}
