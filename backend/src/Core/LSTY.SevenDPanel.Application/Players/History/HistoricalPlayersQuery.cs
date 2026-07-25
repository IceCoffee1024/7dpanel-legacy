using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class HistoricalPlayersCursor
    {
        public HistoricalPlayersCursor(DateTimeOffset firstObservedAtUtc, string crossplatformId)
        {
            FirstObservedAtUtc = HistoryPlayerValidation.RequireUtc(
                firstObservedAtUtc,
                nameof(firstObservedAtUtc));
            CrossplatformId = HistoryPlayerValidation.RequireCrossplatformId(
                crossplatformId,
                nameof(crossplatformId));
        }

        public DateTimeOffset FirstObservedAtUtc { get; }

        public string CrossplatformId { get; }
    }

    public sealed class HistoricalPlayersQuery
    {
        public const int DefaultPageSize = 50;
        public const int MaximumPageSize = 100;

        public HistoricalPlayersQuery(string? query, int pageSize, HistoricalPlayersCursor? cursor)
        {
            if (pageSize < 1 || pageSize > MaximumPageSize)
                throw new ArgumentOutOfRangeException(nameof(pageSize));

            Query = string.IsNullOrWhiteSpace(query) ? null : query!.Trim();
            PageSize = pageSize;
            Cursor = cursor;
        }

        public string? Query { get; }

        public int PageSize { get; }

        public HistoricalPlayersCursor? Cursor { get; }
    }
}
