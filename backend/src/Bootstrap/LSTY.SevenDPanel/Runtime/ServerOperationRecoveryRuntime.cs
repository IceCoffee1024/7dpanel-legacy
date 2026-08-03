using System;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class ServerOperationRecoveryRuntime : IModRuntime
    {
        private readonly ReconcileServerOperationsUseCase reconcile;
        private readonly ServerOperationProcessInstance processInstance;
        private readonly IModRuntime inner;

        public ServerOperationRecoveryRuntime(
            ReconcileServerOperationsUseCase reconcile,
            ServerOperationProcessInstance processInstance,
            IModRuntime inner)
        {
            this.reconcile = reconcile ?? throw new ArgumentNullException(nameof(reconcile));
            this.processInstance = processInstance ?? throw new ArgumentNullException(nameof(processInstance));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start() => inner.Start();

        public void MarkGameReady()
        {
            inner.MarkGameReady();
            reconcile.ReconcileAfterGameReady(processInstance.Value);
        }

        public void Stop() => inner.Stop();
    }
}
