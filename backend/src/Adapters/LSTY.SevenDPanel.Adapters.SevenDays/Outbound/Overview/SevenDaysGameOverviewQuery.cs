using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Overview
{
    public sealed class SevenDaysGameOverviewQuery : IGameOverviewQuery
    {
        private static readonly TimeSpan DefaultCacheLifetime = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly object sync = new object();
        private readonly Func<string, Func<SevenDaysGameOverviewSample>, TimeSpan, Task<SevenDaysGameOverviewSample>> dispatch;
        private readonly Func<SevenDaysGameOverviewSample> capture;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan cacheLifetime;
        private GameOverviewSnapshot? cached;
        private Task<GameOverviewSnapshot>? inFlight;

        public SevenDaysGameOverviewQuery()
            : this(
                (operationName, action, timeout) => GameThreadDispatcher.Enqueue(
                    operationName,
                    action,
                    timeout,
                    CancellationToken.None),
                CaptureOnGameThread,
                () => DateTimeOffset.UtcNow,
                DefaultCacheLifetime)
        {
        }

        internal SevenDaysGameOverviewQuery(
            Func<string, Func<SevenDaysGameOverviewSample>, TimeSpan, Task<SevenDaysGameOverviewSample>> dispatch,
            Func<SevenDaysGameOverviewSample> capture,
            Func<DateTimeOffset> utcNow,
            TimeSpan cacheLifetime)
        {
            if (cacheLifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(cacheLifetime));

            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.cacheLifetime = cacheLifetime;
        }

        public Task<GameOverviewSnapshot> GetGameOverviewAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<GameOverviewSnapshot> shared;
            lock (sync)
            {
                if (cached != null && IsFresh(cached, utcNow()))
                    shared = Task.FromResult(cached);
                else if (inFlight != null)
                    shared = inFlight;
                else
                {
                    var completion = new TaskCompletionSource<GameOverviewSnapshot>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    shared = completion.Task;
                    inFlight = shared;
                    _ = PopulateAsync(completion);
                }
            }

            return AwaitForCallerAsync(shared, cancellationToken);
        }

        private async Task PopulateAsync(TaskCompletionSource<GameOverviewSnapshot> completion)
        {
            GameOverviewSnapshot snapshot;
            try
            {
                var sample = await dispatch(
                        "7DPanel.Overview.Game",
                        capture,
                        DispatchTimeout)
                    .ConfigureAwait(false);
                if (!sample.IsGameReady)
                {
                    snapshot = GameOverviewSnapshot.Unavailable();
                }
                else
                {
                    var sampledAtUtc = utcNow();
                    snapshot = new GameOverviewSnapshot(
                        AvailabilityState.Available,
                        sampledAtUtc,
                        "7 Days to Die",
                        sample.SaveGameName,
                        sample.WorldName,
                        sample.WorldSessionUptimeSeconds,
                        sample.Version,
                        sample.GameMode,
                        sample.Difficulty,
                        sample.Region,
                        sample.Language,
                        sample.ConnectionAddress,
                        sample.ConnectionPort,
                        sample.MaximumPlayerCount,
                        CreateRuntimeMetrics(sample.RuntimeMetrics!, sampledAtUtc));
                }
            }
            catch (TimeoutException)
            {
                snapshot = GetTimeoutSnapshot();
            }
            catch
            {
                snapshot = GameOverviewSnapshot.Unavailable();
            }

            lock (sync)
            {
                if (snapshot.Availability == AvailabilityState.Available)
                    cached = snapshot;
                completion.TrySetResult(snapshot);
                if (ReferenceEquals(inFlight, completion.Task))
                    inFlight = null;
            }
        }

        private bool IsFresh(GameOverviewSnapshot snapshot, DateTimeOffset now)
        {
            if (!snapshot.SampledAtUtc.HasValue || snapshot.SampledAtUtc.Value > now)
                return false;

            return now - snapshot.SampledAtUtc.Value < cacheLifetime;
        }

        private GameOverviewSnapshot GetTimeoutSnapshot()
        {
            lock (sync)
            {
                if (cached == null)
                    return GameOverviewSnapshot.Unavailable();

                return new GameOverviewSnapshot(
                    AvailabilityState.Stale,
                    cached.SampledAtUtc,
                    cached.GameTitle,
                    cached.SaveGameName,
                    cached.WorldName,
                    cached.WorldSessionUptimeSeconds,
                    cached.Version,
                    cached.GameMode,
                    cached.Difficulty,
                    cached.Region,
                    cached.Language,
                    cached.ConnectionAddress,
                    cached.ConnectionPort,
                    cached.MaximumPlayerCount,
                    cached.RuntimeMetrics);
            }
        }

        private static async Task<GameOverviewSnapshot> AwaitForCallerAsync(
            Task<GameOverviewSnapshot> shared,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
                return await shared.ConfigureAwait(false);

            var cancellationSignal = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                () => cancellationSignal.TrySetResult(true));
            if (await Task.WhenAny(shared, cancellationSignal.Task).ConfigureAwait(false) == cancellationSignal.Task)
                cancellationToken.ThrowIfCancellationRequested();

            return await shared.ConfigureAwait(false);
        }

        private static SevenDaysGameOverviewSample CaptureOnGameThread()
        {
            var preferences = global::GamePrefs.Instance;
            var manager = global::GameManager.Instance;
            var world = manager?.World;
            if (preferences == null || manager == null || world == null)
                return SevenDaysGameOverviewSample.NotReady();

            SevenDaysMetricSample<int?> animalCount;
            SevenDaysMetricSample<int?> hostileEntityCount;
            SevenDaysMetricSample<int?> activeEntityCount;
            SevenDaysMetricSample<int?> droppedItemCount;
            try
            {
                var entities = world.Entities?.list;
                if (entities == null)
                    throw new InvalidOperationException();

                var animals = 0;
                var hostileEntities = 0;
                var droppedItems = 0;
                for (var index = 0; index < entities.Count; index++)
                {
                    var entity = entities[index];
                    if (entity is global::EntityAnimal)
                        animals++;
                    if (entity is global::EntityZombie)
                        hostileEntities++;
                    if (entity is global::EntityItem)
                        droppedItems++;
                }

                animalCount = SevenDaysMetricSample<int?>.Available(animals);
                hostileEntityCount = SevenDaysMetricSample<int?>.Available(hostileEntities);
                activeEntityCount = SevenDaysMetricSample<int?>.Available(entities.Count);
                droppedItemCount = SevenDaysMetricSample<int?>.Available(droppedItems);
            }
            catch
            {
                animalCount = SevenDaysMetricSample<int?>.ReadFailed();
                hostileEntityCount = SevenDaysMetricSample<int?>.ReadFailed();
                activeEntityCount = SevenDaysMetricSample<int?>.ReadFailed();
                droppedItemCount = SevenDaysMetricSample<int?>.ReadFailed();
            }

            return new SevenDaysGameOverviewSample(
                true,
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.GameName)),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld)),
                Read(() => (long?)global::UnityEngine.Time.timeSinceLevelLoad),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.GameVersion)),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.GameMode)),
                Read(() => global::GamePrefs.GetInt(global::EnumGamePrefs.GameDifficulty).ToString()),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.Region)),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.Language)),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.ConnectToServerIP)),
                Read(() => (int?)global::GamePrefs.GetInt(global::EnumGamePrefs.ConnectToServerPort)),
                Read(() => (int?)global::GamePrefs.GetInt(global::EnumGamePrefs.ServerMaxPlayerCount)),
                new SevenDaysGameRuntimeMetricsSample(
                    CaptureMetric(() => global::GameUtils.WorldTimeToString(world.worldTime)),
                    CaptureMetric<bool?>(() => world.aiDirector.BloodMoonComponent.BloodMoonActive),
                    CaptureMetric<double?>(() => global::GameManager.frameTime > 0f
                        ? 1d / global::GameManager.frameTime
                        : (double?)null),
                    CaptureMetric<int?>(() => world.Players?.Count),
                    CaptureMetric<int?>(() => manager.persistentPlayerCount),
                    animalCount,
                    hostileEntityCount,
                    activeEntityCount,
                    CaptureMetric<int?>(() => global::Chunk.InstanceCount),
                    droppedItemCount,
                    CaptureMetric<long?>(() => GC.GetTotalMemory(false))));
        }

        private static SevenDaysMetricSample<T> CaptureMetric<T>(Func<T> read)
        {
            try
            {
                var value = read();
                return value is null
                    ? SevenDaysMetricSample<T>.ReadFailed()
                    : SevenDaysMetricSample<T>.Available(value);
            }
            catch
            {
                return SevenDaysMetricSample<T>.ReadFailed();
            }
        }

        private static GameRuntimeMetrics CreateRuntimeMetrics(
            SevenDaysGameRuntimeMetricsSample sample,
            DateTimeOffset observedAtUtc) =>
            new GameRuntimeMetrics(
                Observe(sample.GameDayTime, "World.worldTime", "game-clock", observedAtUtc),
                Observe(sample.IsBloodMoon, "World.aiDirector.BloodMoonComponent.BloodMoonActive", "boolean", observedAtUtc),
                Observe(sample.FramesPerSecond, "GameManager.frameTime", "frames/second", observedAtUtc),
                Observe(sample.OnlinePlayerCount, "World.Players.Count", "count", observedAtUtc),
                Observe(sample.HistoricalPlayerCount, "GameManager.persistentPlayerCount", "count", observedAtUtc),
                Observe(sample.AnimalCount, "World.Entities", "count", observedAtUtc),
                Observe(sample.HostileEntityCount, "World.Entities", "count", observedAtUtc),
                Observe(sample.ActiveEntityCount, "World.Entities", "count", observedAtUtc),
                Observe(sample.ChunkCount, "Chunk.InstanceCount", "count", observedAtUtc),
                Observe(sample.DroppedItemCount, "World.Entities", "count", observedAtUtc),
                Observe(sample.GameMemoryBytes, "GC.GetTotalMemory(false)", "bytes", observedAtUtc));

        private static ObservedMetric<T> Observe<T>(
            SevenDaysMetricSample<T> sample,
            string source,
            string unit,
            DateTimeOffset observedAtUtc) =>
            new ObservedMetric<T>(sample.Value, source, unit, observedAtUtc, sample.Warning);

        private static T? Read<T>(Func<T> read) where T : class
        {
            try { return read(); }
            catch { return null; }
        }

        private static T? Read<T>(Func<T?> read) where T : struct
        {
            try { return read(); }
            catch { return null; }
        }
    }

    public sealed class SevenDaysGameOverviewSample
    {
        public SevenDaysGameOverviewSample(
            bool isGameReady,
            string? saveGameName,
            string? worldName,
            long? worldSessionUptimeSeconds,
            string? version,
            string? gameMode,
            string? difficulty,
            string? region,
            string? language,
            string? connectionAddress,
            int? connectionPort,
            int? maximumPlayerCount,
            SevenDaysGameRuntimeMetricsSample? runtimeMetrics)
        {
            if (isGameReady && runtimeMetrics == null)
                throw new ArgumentNullException(nameof(runtimeMetrics));

            IsGameReady = isGameReady;
            SaveGameName = saveGameName;
            WorldName = worldName;
            WorldSessionUptimeSeconds = worldSessionUptimeSeconds;
            Version = version;
            GameMode = gameMode;
            Difficulty = difficulty;
            Region = region;
            Language = language;
            ConnectionAddress = connectionAddress;
            ConnectionPort = connectionPort;
            MaximumPlayerCount = maximumPlayerCount;
            RuntimeMetrics = runtimeMetrics;
        }

        public bool IsGameReady { get; }
        public string? SaveGameName { get; }
        public string? WorldName { get; }
        public long? WorldSessionUptimeSeconds { get; }
        public string? Version { get; }
        public string? GameMode { get; }
        public string? Difficulty { get; }
        public string? Region { get; }
        public string? Language { get; }
        public string? ConnectionAddress { get; }
        public int? ConnectionPort { get; }
        public int? MaximumPlayerCount { get; }
        public SevenDaysGameRuntimeMetricsSample? RuntimeMetrics { get; }

        internal static SevenDaysGameOverviewSample NotReady() =>
            new SevenDaysGameOverviewSample(
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
    }

    public readonly struct SevenDaysMetricSample<T>
    {
        public SevenDaysMetricSample(T value, RuntimeMetricWarningCode? warning)
        {
            if (warning.HasValue && !Enum.IsDefined(typeof(RuntimeMetricWarningCode), warning.Value))
                throw new ArgumentOutOfRangeException(nameof(warning));
            if (value is null && !warning.HasValue)
                throw new ArgumentException("A missing metric value requires a warning.", nameof(warning));
            if (value is not null && warning.HasValue)
                throw new ArgumentException("An available metric value cannot carry a warning.", nameof(warning));

            Value = value;
            Warning = warning;
        }

        public T Value { get; }
        public RuntimeMetricWarningCode? Warning { get; }

        public static SevenDaysMetricSample<T> Available(T value) =>
            new SevenDaysMetricSample<T>(value, null);

        public static SevenDaysMetricSample<T> ReadFailed() =>
            new SevenDaysMetricSample<T>(default!, RuntimeMetricWarningCode.ReadFailed);

    }

    public sealed class SevenDaysGameRuntimeMetricsSample
    {
        public SevenDaysGameRuntimeMetricsSample(
            SevenDaysMetricSample<string> gameDayTime,
            SevenDaysMetricSample<bool?> isBloodMoon,
            SevenDaysMetricSample<double?> framesPerSecond,
            SevenDaysMetricSample<int?> onlinePlayerCount,
            SevenDaysMetricSample<int?> historicalPlayerCount,
            SevenDaysMetricSample<int?> animalCount,
            SevenDaysMetricSample<int?> hostileEntityCount,
            SevenDaysMetricSample<int?> activeEntityCount,
            SevenDaysMetricSample<int?> chunkCount,
            SevenDaysMetricSample<int?> droppedItemCount,
            SevenDaysMetricSample<long?> gameMemoryBytes)
        {
            GameDayTime = gameDayTime;
            IsBloodMoon = isBloodMoon;
            FramesPerSecond = framesPerSecond;
            OnlinePlayerCount = onlinePlayerCount;
            HistoricalPlayerCount = historicalPlayerCount;
            AnimalCount = animalCount;
            HostileEntityCount = hostileEntityCount;
            ActiveEntityCount = activeEntityCount;
            ChunkCount = chunkCount;
            DroppedItemCount = droppedItemCount;
            GameMemoryBytes = gameMemoryBytes;
        }

        public SevenDaysMetricSample<string> GameDayTime { get; }
        public SevenDaysMetricSample<bool?> IsBloodMoon { get; }
        public SevenDaysMetricSample<double?> FramesPerSecond { get; }
        public SevenDaysMetricSample<int?> OnlinePlayerCount { get; }
        public SevenDaysMetricSample<int?> HistoricalPlayerCount { get; }
        public SevenDaysMetricSample<int?> AnimalCount { get; }
        public SevenDaysMetricSample<int?> HostileEntityCount { get; }
        public SevenDaysMetricSample<int?> ActiveEntityCount { get; }
        public SevenDaysMetricSample<int?> ChunkCount { get; }
        public SevenDaysMetricSample<int?> DroppedItemCount { get; }
        public SevenDaysMetricSample<long?> GameMemoryBytes { get; }
    }
}
