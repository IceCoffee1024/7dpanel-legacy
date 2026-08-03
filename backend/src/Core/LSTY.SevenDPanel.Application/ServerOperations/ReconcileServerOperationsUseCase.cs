using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class ReconcileServerOperationsUseCase
    {
        private readonly IServerOperationStore store;
        private readonly Func<DateTimeOffset> utcClock;

        public ReconcileServerOperationsUseCase(IServerOperationStore store)
            : this(store, () => DateTimeOffset.UtcNow) { }

        internal ReconcileServerOperationsUseCase(IServerOperationStore store, Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public void ReconcileAfterGameReady(string currentProcessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(currentProcessInstanceId))
                throw new ArgumentException("A process instance identifier is required.", nameof(currentProcessInstanceId));
            var now = utcClock();
            foreach (var operation in store.ListRunning())
            {
                if (now > operation.CompletionDeadlineUtc)
                {
                    store.TryTransition(operation.OperationId, ServerOperationLifecycleStatus.Running,
                        ServerOperationLifecycleStatus.ResultUnknown, now, "completion_timeout");
                }
                else if (!string.Equals(operation.OriginProcessInstanceId, currentProcessInstanceId, StringComparison.Ordinal))
                {
                    store.TryTransition(operation.OperationId, ServerOperationLifecycleStatus.Running,
                        ServerOperationLifecycleStatus.Succeeded, now, null);
                }
            }
        }
    }
}
