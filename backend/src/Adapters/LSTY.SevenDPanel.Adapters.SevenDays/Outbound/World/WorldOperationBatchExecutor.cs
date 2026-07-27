using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World
{
    internal delegate Task<WorldOperationBatchStepResult> WorldOperationBatchDispatcher(
        string name,
        Func<WorldOperationBatchStepResult> action,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    internal enum WorldOperationBatchExecutionStatus
    {
        Completed,
        Rejected,
        Failed,
        Cancelled,
        ResultUnknown
    }

    internal sealed class WorldOperationBatchExecutionResult
    {
        private WorldOperationBatchExecutionResult(
            WorldOperationBatchExecutionStatus status,
            long completedBlocks,
            string? errorCode)
        {
            Status = status;
            CompletedBlocks = completedBlocks;
            ErrorCode = errorCode;
        }

        public WorldOperationBatchExecutionStatus Status { get; }
        public long CompletedBlocks { get; }
        public string? ErrorCode { get; }

        internal static WorldOperationBatchExecutionResult Create(
            WorldOperationBatchExecutionStatus status,
            long completedBlocks,
            string? errorCode = null) =>
            new WorldOperationBatchExecutionResult(status, completedBlocks, errorCode);
    }

    internal sealed class WorldOperationBatchContext
    {
        private WorldOperationBatchContext(string? rejectionCode, Func<long, bool>? processBlock)
        {
            RejectionCode = rejectionCode;
            ProcessBlock = processBlock;
        }

        public string? RejectionCode { get; }
        public Func<long, bool>? ProcessBlock { get; }

        internal static WorldOperationBatchContext Ready(Func<long, bool> processBlock) =>
            new WorldOperationBatchContext(
                null,
                processBlock ?? throw new ArgumentNullException(nameof(processBlock)));

        internal static WorldOperationBatchContext Rejected(string errorCode)
        {
            if (string.IsNullOrWhiteSpace(errorCode))
                throw new ArgumentException("An error code is required.", nameof(errorCode));
            return new WorldOperationBatchContext(errorCode, null);
        }
    }

    internal sealed class WorldOperationBatchStepResult
    {
        private WorldOperationBatchStepResult(
            WorldOperationBatchExecutionStatus status,
            int processedBlocks,
            string? errorCode)
        {
            Status = status;
            ProcessedBlocks = processedBlocks;
            ErrorCode = errorCode;
        }

        public WorldOperationBatchExecutionStatus Status { get; }
        public int ProcessedBlocks { get; }
        public string? ErrorCode { get; }

        internal static WorldOperationBatchStepResult Applied(int processedBlocks) =>
            new WorldOperationBatchStepResult(
                WorldOperationBatchExecutionStatus.Completed,
                processedBlocks,
                null);

        internal static WorldOperationBatchStepResult Rejected(string errorCode) =>
            new WorldOperationBatchStepResult(
                WorldOperationBatchExecutionStatus.Rejected,
                0,
                errorCode);

        internal static WorldOperationBatchStepResult Unknown(int processedBlocks) =>
            new WorldOperationBatchStepResult(
                WorldOperationBatchExecutionStatus.ResultUnknown,
                processedBlocks,
                null);
    }

    internal sealed class WorldOperationBatchLease : IDisposable
    {
        private Action? release;

        internal WorldOperationBatchLease(Action release) =>
            this.release = release ?? throw new ArgumentNullException(nameof(release));

        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }

    internal sealed class WorldOperationBatchExecutor
    {
        internal const int MaximumBlocksPerBatch = 256;
        internal const int MaximumOperationCapacity = 4;
        internal static readonly TimeSpan FrameBudget = TimeSpan.FromMilliseconds(4);
        internal static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly object admissionSync = new object();
        private readonly SemaphoreSlim executionGate = new SemaphoreSlim(1, 1);
        private readonly WorldOperationBatchDispatcher dispatcher;
        private readonly Func<long> getTimestamp;
        private readonly long timestampFrequency;
        private int admittedOperations;

        internal WorldOperationBatchExecutor(WorldOperationBatchDispatcher dispatcher)
            : this(dispatcher, Stopwatch.GetTimestamp, Stopwatch.Frequency)
        {
        }

        internal WorldOperationBatchExecutor(
            WorldOperationBatchDispatcher dispatcher,
            Func<long> getTimestamp,
            long timestampFrequency)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.getTimestamp = getTimestamp ?? throw new ArgumentNullException(nameof(getTimestamp));
            if (timestampFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
            this.timestampFrequency = timestampFrequency;
        }

        internal Task<WorldOperationBatchLease?> TryEnterAsync(CancellationToken cancellationToken)
        {
            lock (admissionSync)
            {
                if (admittedOperations >= MaximumOperationCapacity)
                    return Task.FromResult<WorldOperationBatchLease?>(null);
                admittedOperations++;
            }

            return WaitForTurnAsync(cancellationToken);
        }

        internal async Task<WorldOperationBatchExecutionResult> ExecuteAsync(
            long totalBlocks,
            Func<WorldOperationBatchContext> openBatch,
            Action<long, long>? reportProgress,
            CancellationToken cancellationToken)
        {
            if (totalBlocks <= 0) throw new ArgumentOutOfRangeException(nameof(totalBlocks));
            if (openBatch == null) throw new ArgumentNullException(nameof(openBatch));

            long completed = 0;
            reportProgress?.Invoke(0, totalBlocks);
            while (completed < totalBlocks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return WorldOperationBatchExecutionResult.Create(
                        WorldOperationBatchExecutionStatus.Cancelled,
                        completed);
                }

                WorldOperationBatchStepResult step;
                try
                {
                    step = await dispatcher(
                            "7DPanel.World.RegionOperation.Batch",
                            () => ExecuteBatch(completed, totalBlocks, openBatch),
                            DispatchTimeout,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return WorldOperationBatchExecutionResult.Create(
                        WorldOperationBatchExecutionStatus.Cancelled,
                        completed);
                }
                catch
                {
                    return WorldOperationBatchExecutionResult.Create(
                        WorldOperationBatchExecutionStatus.Failed,
                        completed);
                }

                if (step == null ||
                    step.ProcessedBlocks < 0 ||
                    step.ProcessedBlocks > MaximumBlocksPerBatch ||
                    completed + step.ProcessedBlocks > totalBlocks)
                {
                    return WorldOperationBatchExecutionResult.Create(
                        WorldOperationBatchExecutionStatus.Failed,
                        completed);
                }

                completed += step.ProcessedBlocks;
                if (step.ProcessedBlocks > 0) reportProgress?.Invoke(completed, totalBlocks);
                if (step.Status != WorldOperationBatchExecutionStatus.Completed)
                {
                    return WorldOperationBatchExecutionResult.Create(
                        step.Status,
                        completed,
                        step.ErrorCode);
                }
                if (step.ProcessedBlocks == 0)
                {
                    return WorldOperationBatchExecutionResult.Create(
                        WorldOperationBatchExecutionStatus.Failed,
                        completed);
                }
            }

            return WorldOperationBatchExecutionResult.Create(
                WorldOperationBatchExecutionStatus.Completed,
                completed);
        }

        private async Task<WorldOperationBatchLease?> WaitForTurnAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                await executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                return new WorldOperationBatchLease(Release);
            }
            catch
            {
                lock (admissionSync) admittedOperations--;
                throw;
            }
        }

        private void Release()
        {
            executionGate.Release();
            lock (admissionSync) admittedOperations--;
        }

        private WorldOperationBatchStepResult ExecuteBatch(
            long completed,
            long totalBlocks,
            Func<WorldOperationBatchContext> openBatch)
        {
            var context = openBatch();
            if (context == null) throw new InvalidOperationException();
            if (context.RejectionCode != null)
                return WorldOperationBatchStepResult.Rejected(context.RejectionCode);
            if (context.ProcessBlock == null) throw new InvalidOperationException();

            var started = getTimestamp();
            var processed = 0;
            while (processed < MaximumBlocksPerBatch && completed + processed < totalBlocks)
            {
                if (processed > 0 && Elapsed(started, getTimestamp()) >= FrameBudget)
                    break;

                bool succeeded;
                try
                {
                    succeeded = context.ProcessBlock(completed + processed);
                }
                catch
                {
                    return WorldOperationBatchStepResult.Unknown(processed);
                }
                if (!succeeded)
                    return WorldOperationBatchStepResult.Unknown(processed);
                processed++;
            }

            return WorldOperationBatchStepResult.Applied(processed);
        }

        private TimeSpan Elapsed(long started, long current) =>
            TimeSpan.FromSeconds((current - started) / (double)timestampFrequency);
    }
}
