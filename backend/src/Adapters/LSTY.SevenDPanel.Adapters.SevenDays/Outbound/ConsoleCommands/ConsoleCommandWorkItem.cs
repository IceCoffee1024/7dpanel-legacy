using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.ConsoleCommands;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands
{
    internal sealed class ConsoleCommandWorkItem : IDisposable
    {
        private const int Pending = 0;
        private const int Running = 1;
        private const int Completed = 2;

        private readonly TaskCompletionSource<ConsoleCommandResult> completion =
            new TaskCompletionSource<ConsoleCommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken cancellationToken;
        private CancellationTokenRegistration cancellationRegistration;
        private int state = Pending;

        public ConsoleCommandWorkItem(
            ConsoleCommandRequest request,
            CancellationToken cancellationToken)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            this.cancellationToken = cancellationToken;
            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(CancelPending);
            }
        }

        public ConsoleCommandRequest Request { get; }
        public Task<ConsoleCommandResult> Task => completion.Task;

        public bool TryStart()
        {
            if (Interlocked.CompareExchange(ref state, Running, Pending) != Pending)
                return false;
            cancellationRegistration.Dispose();
            return true;
        }

        public void Complete(ConsoleCommandResult result)
        {
            Volatile.Write(ref state, Completed);
            completion.TrySetResult(result);
        }

        public void Fail(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            Volatile.Write(ref state, Completed);
            completion.TrySetException(exception);
        }

        public void RejectUnavailable()
        {
            if (Interlocked.CompareExchange(ref state, Completed, Pending) == Pending)
                completion.TrySetException(new ConsoleCommandUnavailableException());
        }

        public void Dispose() => cancellationRegistration.Dispose();

        private void CancelPending()
        {
            if (Interlocked.CompareExchange(ref state, Completed, Pending) == Pending)
                completion.TrySetCanceled(cancellationToken);
        }
    }
}