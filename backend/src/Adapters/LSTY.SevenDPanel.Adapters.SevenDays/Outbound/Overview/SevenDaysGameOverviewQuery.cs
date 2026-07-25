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
                        sample.OnlinePlayerCount,
                        sample.MaximumPlayerCount,
                        sample.HistoricalPlayerCount,
                        sample.FramesPerSecond,
                        sample.GameTime);
                }
            }
            catch (TimeoutException)
            {
                snapshot = new GameOverviewSnapshot(
                    AvailabilityState.Stale, null, null, null, null, null, null, null,
                    null, null, null, null, null, null, null, null, null, null);
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

            return new SevenDaysGameOverviewSample(
                true,
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.GameName)),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld)),
                Read(() => (long?)global::UnityEngine.Time.timeSinceLevelLoad),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.GameVersion)),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.GameMode)),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.GameDifficulty)),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.Region)),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.Language)),
                Read(() => global::GamePrefs.GetString(global::EnumGamePrefs.ConnectToServerIP)),
                Read(() => (int?)global::GamePrefs.GetInt(global::EnumGamePrefs.ConnectToServerPort)),
                Read(() => (int?)world.Players?.Count),
                Read(() => (int?)global::GamePrefs.GetInt(global::EnumGamePrefs.ServerMaxPlayerCount)),
                Read(() => (int?)manager.persistentPlayerCount),
                Read(() => global::GameManager.frameTime > 0f
                    ? (double?)(1d / global::GameManager.frameTime)
                    : null),
                Read(() => global::GameUtils.WorldTimeToString(world.worldTime)));
        }

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
            int? onlinePlayerCount,
            int? maximumPlayerCount,
            int? historicalPlayerCount,
            double? framesPerSecond,
            string? gameTime)
        {
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
            OnlinePlayerCount = onlinePlayerCount;
            MaximumPlayerCount = maximumPlayerCount;
            HistoricalPlayerCount = historicalPlayerCount;
            FramesPerSecond = framesPerSecond;
            GameTime = gameTime;
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
        public int? OnlinePlayerCount { get; }
        public int? MaximumPlayerCount { get; }
        public int? HistoricalPlayerCount { get; }
        public double? FramesPerSecond { get; }
        public string? GameTime { get; }

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
                null,
                null,
                null,
                null);
    }
}
