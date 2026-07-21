using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime
{
    internal static class GameThreadDispatcher
    {
        public static Task<T> Enqueue<T>(
            string operationName,
            Func<T> action,
            TimeSpan startTimeout,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(operationName))
                throw new ArgumentException("An operation name is required.", nameof(operationName));
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (startTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(startTimeout));
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<T>(cancellationToken);

            if (ThreadManager.IsMainThread())
            {
                try
                {
                    return Task.FromResult(action());
                }
                catch (Exception exception)
                {
                    return Task.FromException<T>(exception);
                }
            }

            var request = new GameThreadDispatchRequest<T>(action);
            try
            {
                ThreadManager.AddSingleTaskMainThread(operationName, request.Execute);
            }
            catch (Exception exception)
            {
                request.FailToQueue(exception);
            }

            return request.WaitAsync(startTimeout, cancellationToken);
        }
    }

    internal sealed class GameThreadDispatchRequest<T>
    {
        private const int Pending = 0;
        private const int Running = 1;
        private const int Completed = 2;

        private readonly Func<T> action;
        private readonly TaskCompletionSource<T> completion =
            new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int state = Pending;

        public GameThreadDispatchRequest(Func<T> action)
        {
            this.action = action ?? throw new ArgumentNullException(nameof(action));
        }

        public async Task<T> WaitAsync(
            TimeSpan startTimeout,
            CancellationToken cancellationToken)
        {
            if (startTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(startTimeout));

            using var timeoutCancellation = new CancellationTokenSource();
            var timeoutTask = Task.Delay(startTimeout, timeoutCancellation.Token);
            var cancellationSignal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellationRegistration = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(() => cancellationSignal.TrySetResult(true))
                : default;

            var completed = cancellationToken.CanBeCanceled
                ? await Task.WhenAny(
                        completion.Task,
                        cancellationSignal.Task,
                        timeoutTask)
                    .ConfigureAwait(false)
                : await Task.WhenAny(completion.Task, timeoutTask).ConfigureAwait(false);

            if (completed == cancellationSignal.Task)
            {
                TryCancel();
                timeoutCancellation.Cancel();
            }
            else if (completed == timeoutTask)
            {
                TryTimeout();
            }
            else
            {
                timeoutCancellation.Cancel();
            }

            return await completion.Task.ConfigureAwait(false);
        }

        public void Execute()
        {
            if (Interlocked.CompareExchange(ref state, Running, Pending) != Pending)
                return;

            try
            {
                var result = action();
                Volatile.Write(ref state, Completed);
                completion.TrySetResult(result);
            }
            catch (Exception exception)
            {
                Volatile.Write(ref state, Completed);
                completion.TrySetException(exception);
            }
        }

        public void FailToQueue(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (Interlocked.CompareExchange(ref state, Completed, Pending) == Pending)
                completion.TrySetException(exception);
        }

        internal bool TryCancel()
        {
            if (Interlocked.CompareExchange(ref state, Completed, Pending) == Pending)
            {
                completion.TrySetCanceled();
                return true;
            }

            return false;
        }

        internal bool TryTimeout()
        {
            if (Interlocked.CompareExchange(ref state, Completed, Pending) == Pending)
            {
                completion.TrySetException(new TimeoutException(
                    "Timed out waiting for the game thread to start the queued operation."));
                return true;
            }

            return false;
        }
    }
}
