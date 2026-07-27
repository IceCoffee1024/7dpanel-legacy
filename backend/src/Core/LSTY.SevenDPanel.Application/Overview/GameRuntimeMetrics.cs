using System;

namespace LSTY.SevenDPanel.Application
{
    public enum RuntimeMetricWarningCode
    {
        ReadFailed,
        Unsupported
    }

    public sealed record ObservedMetric<T>
    {
        public ObservedMetric(
            T? value,
            string source,
            string unit,
            DateTimeOffset observedAtUtc,
            RuntimeMetricWarningCode? warning)
        {
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("A metric source is required.", nameof(source));
            if (string.IsNullOrWhiteSpace(unit))
                throw new ArgumentException("A metric unit is required.", nameof(unit));
            RequireUtc(observedAtUtc, nameof(observedAtUtc));
            if (warning.HasValue && !Enum.IsDefined(typeof(RuntimeMetricWarningCode), warning.Value))
                throw new ArgumentOutOfRangeException(nameof(warning));
            if (value is null && !warning.HasValue)
                throw new ArgumentException("A missing metric value requires a warning.", nameof(warning));
            if (value is not null && warning.HasValue)
                throw new ArgumentException("An available metric value cannot carry a warning.", nameof(warning));

            Value = value;
            Source = source.Trim();
            Unit = unit.Trim();
            ObservedAtUtc = observedAtUtc;
            Warning = warning;
        }

        public T? Value { get; }
        public string Source { get; }
        public string Unit { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public RuntimeMetricWarningCode? Warning { get; }

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }
    }

    public sealed class GameRuntimeMetrics
    {
        public GameRuntimeMetrics(
            ObservedMetric<string> gameDayTime,
            ObservedMetric<bool?> isBloodMoon,
            ObservedMetric<double?> framesPerSecond,
            ObservedMetric<int?> onlinePlayerCount,
            ObservedMetric<int?> historicalPlayerCount,
            ObservedMetric<int?> animalCount,
            ObservedMetric<int?> hostileEntityCount,
            ObservedMetric<int?> activeEntityCount,
            ObservedMetric<int?> chunkCount,
            ObservedMetric<int?> droppedItemCount,
            ObservedMetric<long?> gameMemoryBytes)
        {
            GameDayTime = gameDayTime ?? throw new ArgumentNullException(nameof(gameDayTime));
            IsBloodMoon = isBloodMoon ?? throw new ArgumentNullException(nameof(isBloodMoon));
            FramesPerSecond = framesPerSecond ?? throw new ArgumentNullException(nameof(framesPerSecond));
            OnlinePlayerCount = onlinePlayerCount ?? throw new ArgumentNullException(nameof(onlinePlayerCount));
            HistoricalPlayerCount = historicalPlayerCount ?? throw new ArgumentNullException(nameof(historicalPlayerCount));
            AnimalCount = animalCount ?? throw new ArgumentNullException(nameof(animalCount));
            HostileEntityCount = hostileEntityCount ?? throw new ArgumentNullException(nameof(hostileEntityCount));
            ActiveEntityCount = activeEntityCount ?? throw new ArgumentNullException(nameof(activeEntityCount));
            ChunkCount = chunkCount ?? throw new ArgumentNullException(nameof(chunkCount));
            DroppedItemCount = droppedItemCount ?? throw new ArgumentNullException(nameof(droppedItemCount));
            GameMemoryBytes = gameMemoryBytes ?? throw new ArgumentNullException(nameof(gameMemoryBytes));
        }

        public ObservedMetric<string> GameDayTime { get; }
        public ObservedMetric<bool?> IsBloodMoon { get; }
        public ObservedMetric<double?> FramesPerSecond { get; }
        public ObservedMetric<int?> OnlinePlayerCount { get; }
        public ObservedMetric<int?> HistoricalPlayerCount { get; }
        public ObservedMetric<int?> AnimalCount { get; }
        public ObservedMetric<int?> HostileEntityCount { get; }
        public ObservedMetric<int?> ActiveEntityCount { get; }
        public ObservedMetric<int?> ChunkCount { get; }
        public ObservedMetric<int?> DroppedItemCount { get; }
        public ObservedMetric<long?> GameMemoryBytes { get; }
    }
}
