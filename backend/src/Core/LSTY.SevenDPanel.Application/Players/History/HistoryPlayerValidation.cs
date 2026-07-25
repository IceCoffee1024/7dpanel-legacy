using System;

namespace LSTY.SevenDPanel.Application
{
    internal static class HistoryPlayerValidation
    {
        public const int MaxCrossplatformIdLength = 256;

        public static string RequireCrossplatformId(string? crossplatformId, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(crossplatformId))
                throw new ArgumentException("A cross-platform identity is required.", parameterName);
            var value = crossplatformId!;
            if (value.Length > MaxCrossplatformIdLength)
                throw new ArgumentOutOfRangeException(parameterName);

            return value;
        }

        public static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(parameterName, "The time must be UTC.");

            return value;
        }
    }
}
