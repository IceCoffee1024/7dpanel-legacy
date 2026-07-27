using System;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Application.Modules
{
    public sealed class FeatureModuleWorldOperationJobBridge : IWorldOperationJobBridge
    {
        private readonly IWorldOperationJobBridge inner;
        private readonly FeatureModuleGate gate;

        public FeatureModuleWorldOperationJobBridge(
            IWorldOperationJobBridge inner,
            FeatureModuleGate gate)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public WorldOperationReceipt Enqueue(WorldOperationIntent intent)
        {
            gate.RequireEnabled(FeatureModuleId.WorldTools);
            return inner.Enqueue(intent);
        }

        public WorldOperationRecord Get(string operationId) => inner.Get(operationId);

        public WorldOperationPage Query(WorldOperationQuery query) => inner.Query(query);

        public bool RequestCancellation(string operationId, string actorSubject) =>
            inner.RequestCancellation(operationId, actorSubject);
    }
}
