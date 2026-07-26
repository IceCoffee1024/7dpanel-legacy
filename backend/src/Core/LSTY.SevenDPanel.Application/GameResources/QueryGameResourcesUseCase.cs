using System;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GameResourceHiddenForbiddenException : Exception
    {
        public GameResourceHiddenForbiddenException()
            : base("Including hidden game resources requires owner access.")
        {
        }
    }

    public sealed class QueryGameResourcesUseCase
    {
        private readonly IGameResourceCatalog catalog;

        public QueryGameResourcesUseCase(IGameResourceCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public GameResourceQueryResult Execute(
            GameResourceQuery query,
            GameResourceAccess access)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (!Enum.IsDefined(typeof(GameResourceAccess), access))
                throw new ArgumentOutOfRangeException(nameof(access));
            if (query.IncludeHidden && access != GameResourceAccess.Owner)
                throw new GameResourceHiddenForbiddenException();

            var read = catalog.Read();
            if (read == null)
                throw new InvalidOperationException("The game resource catalog returned no read result.");
            if (read.Status != GameResourceCatalogReadStatus.Available)
            {
                return new GameResourceQueryResult(
                    read.Status,
                    null,
                    null,
                    null,
                    Enumerable.Empty<GameResourceQueryItem>(),
                    Enumerable.Empty<string>(),
                    0,
                    query.Page,
                    query.PageSize);
            }

            var snapshot = read.Snapshot!;
            var filtered = snapshot.Resources.Where(resource =>
                query.IncludeHidden ||
                resource.Visibility == GameResourceVisibility.Public);

            if (query.Kind.HasValue)
                filtered = filtered.Where(resource => resource.Kind == query.Kind.Value);
            if (query.Search != null)
            {
                filtered = filtered.Where(resource =>
                    Contains(resource.InternalName, query.Search) ||
                    Contains(LocalizedName(resource, query.Language), query.Search));
            }

            var ordered = filtered
                .OrderBy(
                    resource => LocalizedName(resource, query.Language) ?? resource.InternalName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(resource => resource.InternalName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(resource => resource.NumericId)
                .ToArray();
            var offset = (query.Page - 1) * query.PageSize;
            var items = ordered
                .Skip(offset)
                .Take(query.PageSize)
                .Select(resource => new GameResourceQueryItem(
                    resource,
                    LocalizedName(resource, query.Language)))
                .ToArray();

            return new GameResourceQueryResult(
                GameResourceCatalogReadStatus.Available,
                snapshot.CatalogVersion,
                snapshot.GameVersion,
                snapshot.ObservedAtUtc,
                items,
                snapshot.Warnings,
                ordered.Length,
                query.Page,
                query.PageSize);
        }

        private static string? LocalizedName(
            GameResourceCatalogEntry resource,
            string language) =>
            string.Equals(language, "zh-CN", StringComparison.Ordinal)
                ? resource.LocalizedNameZhCn
                : resource.LocalizedNameEn;

        private static bool Contains(string? value, string search) =>
            value != null && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
