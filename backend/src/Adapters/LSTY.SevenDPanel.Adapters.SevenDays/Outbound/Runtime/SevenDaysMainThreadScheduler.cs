using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime
{
    public sealed class SevenDaysMainThreadScheduler
    {
        private const string PumpOperationName = "7DPanel.MainThreadPump";

        private readonly object sync = new object();
        private readonly Queue<IRequest> requests = new Queue<IRequest>();
        private readonly IMainThreadDispatcher dispatcher;
        private readonly IMainThreadDeadlineScheduler deadlineScheduler;
        private readonly int capacity;
        private SchedulerState state;
        private int occupiedSlots;
        private bool pumpPosted;
        private IRequest? runningRequest;

        public SevenDaysMainThreadScheduler(
            IMainThreadDispatcher dispatcher,
            IMainThreadDeadlineScheduler deadlineScheduler,
            int capacity)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.deadlineScheduler = deadlineScheduler ?? throw new ArgumentNullException(nameof(deadlineScheduler));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
        }

        public void Start()
        {
            lock (sync)
            {
                if (state == SchedulerState.Created) state = SchedulerState.Ready;
            }
        }

        public Task<MainThreadReply<T>> RequestAsync<T>(
            string operationName,
            Func<T> operation,
            TimeSpan timeout)
        {
            return RequestAsync(operationName, operation, timeout, CancellationToken.None);
        }

        public Task<MainThreadReply<T>> RequestAsync<T>(
            string operationName,
            Func<T> operation,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(operationName)) throw new ArgumentException("An operation name is required.", nameof(operationName));
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

            var request = new Request<T>(operation);
            request.Arm(deadlineScheduler, timeout, cancellationToken);

            MainThreadUnavailableReason rejectionReason = MainThreadUnavailableReason.None;
            var postPump = false;
            lock (sync)
            {
                if (!request.IsPending)
                {
                    return request.ReplyTask;
                }

                if (state != SchedulerState.Ready)
                {
                    rejectionReason = state == SchedulerState.Created
                        ? MainThreadUnavailableReason.NotReady
                        : MainThreadUnavailableReason.Stopping;
                }
                else if (occupiedSlots >= capacity)
                {
                    rejectionReason = MainThreadUnavailableReason.CapacityExceeded;
                }
                else
                {
                    requests.Enqueue(request);
                    occupiedSlots++;
                    if (!pumpPosted)
                    {
                        pumpPosted = true;
                        postPump = true;
                    }
                }
            }

            if (rejectionReason != MainThreadUnavailableReason.None)
            {
                request.CompleteUnavailable(rejectionReason);
            }
            else if (postPump)
            {
                PostPump();
            }

            return request.ReplyTask;
        }

        public void Stop()
        {
            List<IRequest>? pending = null;
            IRequest? running = null;
            lock (sync)
            {
                if (state == SchedulerState.Stopping || state == SchedulerState.Stopped) return;

                state = SchedulerState.Stopping;
                if (requests.Count > 0)
                {
                    pending = new List<IRequest>(requests.Count);
                    while (requests.Count > 0)
                    {
                        pending.Add(requests.Dequeue());
                        occupiedSlots--;
                    }
                }

                running = runningRequest;
                if (occupiedSlots == 0) state = SchedulerState.Stopped;
            }

            if (pending != null)
            {
                foreach (var request in pending)
                {
                    request.CompleteUnavailable(MainThreadUnavailableReason.Stopping);
                }
            }

            running?.CompleteUnknown();
        }

        private void Pump()
        {
            IRequest? request = null;
            lock (sync)
            {
                if (state != SchedulerState.Ready)
                {
                    pumpPosted = false;
                    return;
                }

                while (requests.Count > 0)
                {
                    var candidate = requests.Dequeue();
                    if (candidate.TryStart())
                    {
                        request = candidate;
                        runningRequest = candidate;
                        break;
                    }

                    occupiedSlots--;
                }

                if (request == null) pumpPosted = false;
            }

            if (request == null) return;

            request.Execute();

            var postNextPump = false;
            lock (sync)
            {
                if (ReferenceEquals(runningRequest, request)) runningRequest = null;
                occupiedSlots--;

                if (state == SchedulerState.Ready && requests.Count > 0)
                {
                    postNextPump = true;
                }
                else
                {
                    pumpPosted = false;
                    if (state == SchedulerState.Stopping && occupiedSlots == 0)
                    {
                        state = SchedulerState.Stopped;
                    }
                }
            }

            if (postNextPump) PostPump();
        }

        private void PostPump()
        {
            try
            {
                dispatcher.Post(PumpOperationName, Pump);
            }
            catch
            {
                Stop();
            }
        }

        private enum SchedulerState
        {
            Created,
            Ready,
            Stopping,
            Stopped
        }

        private interface IRequest
        {
            bool TryStart();
            void Execute();
            void CompleteUnavailable(MainThreadUnavailableReason reason);
            void CompleteUnknown();
        }

        private sealed class Request<T> : IRequest
        {
            private const int Pending = 0;
            private const int Running = 1;
            private const int Completed = 2;

            private readonly Func<T> operation;
            private readonly TaskCompletionSource<MainThreadReply<T>> completion =
                new TaskCompletionSource<MainThreadReply<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int executionState;
            private IDisposable? cancellationRegistration;
            private IDisposable? deadlineRegistration;

            public Request(Func<T> operation)
            {
                this.operation = operation;
            }

            public bool IsPending => Volatile.Read(ref executionState) == Pending;
            public Task<MainThreadReply<T>> ReplyTask => completion.Task;

            public void Arm(
                IMainThreadDeadlineScheduler deadlineScheduler,
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                try
                {
                    deadlineRegistration = deadlineScheduler.Schedule(timeout, CompleteTimedOut);
                    if (!IsPending)
                    {
                        DisposeRegistrations();
                        return;
                    }

                    if (cancellationToken.CanBeCanceled)
                    {
                        cancellationRegistration = cancellationToken.Register(CompleteCanceled);
                        if (!IsPending) DisposeRegistrations();
                    }
                }
                catch
                {
                    DisposeRegistrations();
                    throw;
                }
            }

            public bool TryStart()
            {
                return Interlocked.CompareExchange(ref executionState, Running, Pending) == Pending;
            }

            public void Execute()
            {
                MainThreadReply<T> reply;
                try
                {
                    reply = MainThreadReply<T>.Succeeded(operation());
                }
                catch (Exception exception)
                {
                    reply = MainThreadReply<T>.Failed(exception);
                }

                Volatile.Write(ref executionState, Completed);
                CompleteReply(reply);
            }

            public void CompleteUnavailable(MainThreadUnavailableReason reason)
            {
                if (Interlocked.CompareExchange(ref executionState, Completed, Pending) == Pending)
                {
                    CompleteReply(MainThreadReply<T>.Unavailable(reason));
                }
            }

            public void CompleteUnknown()
            {
                if (Volatile.Read(ref executionState) == Running)
                {
                    CompleteReply(MainThreadReply<T>.Unknown());
                }
            }

            private void CompleteCanceled()
            {
                CompleteSignal(MainThreadReply<T>.Canceled());
            }

            private void CompleteTimedOut()
            {
                CompleteSignal(MainThreadReply<T>.TimedOut());
            }

            private void CompleteSignal(MainThreadReply<T> pendingReply)
            {
                while (true)
                {
                    var currentState = Volatile.Read(ref executionState);
                    if (currentState == Pending)
                    {
                        if (Interlocked.CompareExchange(ref executionState, Completed, Pending) != Pending) continue;
                        CompleteReply(pendingReply);
                        return;
                    }

                    if (currentState == Running)
                    {
                        CompleteReply(MainThreadReply<T>.Unknown());
                    }
                    return;
                }
            }

            private void CompleteReply(MainThreadReply<T> reply)
            {
                if (completion.TrySetResult(reply)) DisposeRegistrations();
            }

            private void DisposeRegistrations()
            {
                Interlocked.Exchange(ref cancellationRegistration, null)?.Dispose();
                Interlocked.Exchange(ref deadlineRegistration, null)?.Dispose();
            }
        }
    }
}
