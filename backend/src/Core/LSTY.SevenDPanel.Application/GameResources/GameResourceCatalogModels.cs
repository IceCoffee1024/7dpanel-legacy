using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public enum GameResourceKind
    {
        Item,
        Block
    }

    public enum GameResourceVisibility
    {
        Public,
        Hidden
    }

    public enum GameResourceIconStatus
    {
        Available,
        Missing,
        Invalid
    }

    public enum GameResourceCatalogReadStatus
    {
        Building,
        Available,
        Unavailable
    }

    public enum GameResourceIconReadStatus
    {
        Available,
        Missing,
        Unavailable
    }

    public enum GameResourceAccess
    {
        Standard,
        Owner
    }

    public sealed class GameResourceCatalogEntry
    {
        public GameResourceCatalogEntry(
            string resourceId,
            int numericId,
            string internalName,
            string? localizedNameZhCn,
            string? localizedNameEn,
            GameResourceKind kind,
            GameResourceVisibility visibility,
            int? maxStack,
            bool? hasQuality,
            GameResourceIconStatus iconStatus,
            string? iconTintHex)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
                throw new ArgumentException("A resource identifier is required.", nameof(resourceId));
            if (numericId < 0) throw new ArgumentOutOfRangeException(nameof(numericId));
            if (string.IsNullOrWhiteSpace(internalName))
                throw new ArgumentException("An internal name is required.", nameof(internalName));
            if (!Enum.IsDefined(typeof(GameResourceKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (maxStack.HasValue && maxStack.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(maxStack));
            if (!Enum.IsDefined(typeof(GameResourceIconStatus), iconStatus))
                throw new ArgumentOutOfRangeException(nameof(iconStatus));
            if (iconTintHex != null && !IsUppercaseRgb(iconTintHex))
            {
                throw new ArgumentException(
                    "An icon tint must be six uppercase RGB hexadecimal characters.",
                    nameof(iconTintHex));
            }

            ResourceId = resourceId;
            NumericId = numericId;
            InternalName = internalName;
            LocalizedNameZhCn = NormalizeOptionalName(localizedNameZhCn);
            LocalizedNameEn = NormalizeOptionalName(localizedNameEn);
            Kind = kind;
            Visibility = visibility == GameResourceVisibility.Public
                ? GameResourceVisibility.Public
                : GameResourceVisibility.Hidden;
            MaxStack = maxStack;
            HasQuality = hasQuality;
            IconStatus = iconStatus;
            IconTintHex = iconTintHex;
        }

        public string ResourceId { get; }

        public int NumericId { get; }

        public string InternalName { get; }

        public string? LocalizedNameZhCn { get; }

        public string? LocalizedNameEn { get; }

        public GameResourceKind Kind { get; }

        public GameResourceVisibility Visibility { get; }

        public int? MaxStack { get; }

        public bool? HasQuality { get; }

        public GameResourceIconStatus IconStatus { get; }

        public string? IconTintHex { get; }

        private static string? NormalizeOptionalName(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        private static bool IsUppercaseRgb(string value)
        {
            if (value.Length != 6) return false;

            foreach (var character in value)
            {
                if ((character < '0' || character > '9') &&
                    (character < 'A' || character > 'F'))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class GameResourceCatalogSnapshot
    {
        public GameResourceCatalogSnapshot(
            string catalogVersion,
            string? gameVersion,
            DateTimeOffset observedAtUtc,
            IEnumerable<GameResourceCatalogEntry> resources,
            IEnumerable<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(catalogVersion))
                throw new ArgumentException("A catalog version is required.", nameof(catalogVersion));
            if (observedAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("The observation time must be UTC.", nameof(observedAtUtc));
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            if (warnings == null) throw new ArgumentNullException(nameof(warnings));

            var resourceCopy = resources.ToArray();
            if (resourceCopy.Any(resource => resource == null))
                throw new ArgumentException("Resources cannot contain null entries.", nameof(resources));
            var warningCopy = warnings.ToArray();
            if (warningCopy.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Warnings cannot contain blank entries.", nameof(warnings));

            CatalogVersion = catalogVersion;
            GameVersion = string.IsNullOrWhiteSpace(gameVersion) ? null : gameVersion;
            ObservedAtUtc = observedAtUtc;
            Resources = Array.AsReadOnly(resourceCopy);
            Warnings = Array.AsReadOnly(warningCopy);
        }

        public string CatalogVersion { get; }

        public string? GameVersion { get; }

        public DateTimeOffset ObservedAtUtc { get; }

        public IReadOnlyList<GameResourceCatalogEntry> Resources { get; }

        public IReadOnlyList<string> Warnings { get; }
    }

    public sealed class GameResourceCatalogReadResult
    {
        private GameResourceCatalogReadResult(
            GameResourceCatalogReadStatus status,
            GameResourceCatalogSnapshot? snapshot)
        {
            Status = status;
            Snapshot = snapshot;
        }

        public GameResourceCatalogReadStatus Status { get; }

        public GameResourceCatalogSnapshot? Snapshot { get; }

        public static GameResourceCatalogReadResult Building() =>
            new GameResourceCatalogReadResult(GameResourceCatalogReadStatus.Building, null);

        public static GameResourceCatalogReadResult Available(
            GameResourceCatalogSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return new GameResourceCatalogReadResult(
                GameResourceCatalogReadStatus.Available,
                snapshot);
        }

        public static GameResourceCatalogReadResult Unavailable() =>
            new GameResourceCatalogReadResult(GameResourceCatalogReadStatus.Unavailable, null);
    }

    public sealed class GameResourceIconReadResult
    {
        private readonly byte[]? content;

        private GameResourceIconReadResult(
            GameResourceIconReadStatus status,
            byte[]? content,
            string? contentType,
            string? etag)
        {
            Status = status;
            this.content = content == null ? null : (byte[])content.Clone();
            ContentType = contentType;
            ETag = etag;
        }

        public GameResourceIconReadStatus Status { get; }

        public byte[]? Content => content == null ? null : (byte[])content.Clone();

        public string? ContentType { get; }

        public string? ETag { get; }

        public static GameResourceIconReadResult Available(byte[] content, string etag)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (content.Length == 0)
                throw new ArgumentException("Icon content cannot be empty.", nameof(content));
            if (string.IsNullOrWhiteSpace(etag) ||
                etag.Length < 2 ||
                etag[0] != '"' ||
                etag[etag.Length - 1] != '"' ||
                etag.IndexOf('\r') >= 0 ||
                etag.IndexOf('\n') >= 0)
            {
                throw new ArgumentException("A quoted entity tag is required.", nameof(etag));
            }

            return new GameResourceIconReadResult(
                GameResourceIconReadStatus.Available,
                content,
                "image/png",
                etag);
        }

        public static GameResourceIconReadResult Missing() =>
            new GameResourceIconReadResult(GameResourceIconReadStatus.Missing, null, null, null);

        public static GameResourceIconReadResult Unavailable() =>
            new GameResourceIconReadResult(GameResourceIconReadStatus.Unavailable, null, null, null);
    }

    public sealed class GameResourceQuery
    {
        public GameResourceQuery(
            string? search,
            GameResourceKind? kind,
            bool includeHidden,
            string language,
            int page,
            int pageSize)
        {
            string? normalizedSearch = null;
            if (search != null)
            {
                normalizedSearch = search.Trim();
                if (normalizedSearch.Length == 0)
                    throw new ArgumentException("Search cannot be blank.", nameof(search));
                if (normalizedSearch.Length > 100)
                    throw new ArgumentOutOfRangeException(nameof(search));
            }
            if (kind.HasValue && !Enum.IsDefined(typeof(GameResourceKind), kind.Value))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!string.Equals(language, "zh-CN", StringComparison.Ordinal) &&
                !string.Equals(language, "en", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Only zh-CN and en game resource languages are supported.",
                    nameof(language));
            }
            if (page < 1 || page > 100_000)
                throw new ArgumentOutOfRangeException(nameof(page));
            if (pageSize < 1 || pageSize > 100)
                throw new ArgumentOutOfRangeException(nameof(pageSize));

            Search = normalizedSearch;
            Kind = kind;
            IncludeHidden = includeHidden;
            Language = language;
            Page = page;
            PageSize = pageSize;
        }

        public string? Search { get; }

        public GameResourceKind? Kind { get; }

        public bool IncludeHidden { get; }

        public string Language { get; }

        public int Page { get; }

        public int PageSize { get; }
    }

    public sealed class GameResourceQueryItem
    {
        internal GameResourceQueryItem(
            GameResourceCatalogEntry resource,
            string? localizedName)
        {
            ResourceId = resource.ResourceId;
            NumericId = resource.NumericId;
            InternalName = resource.InternalName;
            LocalizedName = localizedName;
            Kind = resource.Kind;
            Visibility = resource.Visibility;
            MaxStack = resource.MaxStack;
            HasQuality = resource.HasQuality;
            IconStatus = resource.IconStatus;
            IconTintHex = resource.IconTintHex;
        }

        public string ResourceId { get; }

        public int NumericId { get; }

        public string InternalName { get; }

        public string? LocalizedName { get; }

        public GameResourceKind Kind { get; }

        public GameResourceVisibility Visibility { get; }

        public int? MaxStack { get; }

        public bool? HasQuality { get; }

        public GameResourceIconStatus IconStatus { get; }

        public string? IconTintHex { get; }
    }

    public sealed class GameResourceQueryResult
    {
        internal GameResourceQueryResult(
            GameResourceCatalogReadStatus status,
            string? catalogVersion,
            string? gameVersion,
            DateTimeOffset? observedAtUtc,
            IEnumerable<GameResourceQueryItem> items,
            IEnumerable<string> warnings,
            int total,
            int page,
            int pageSize)
        {
            Status = status;
            CatalogVersion = catalogVersion;
            GameVersion = gameVersion;
            ObservedAtUtc = observedAtUtc;
            Items = Array.AsReadOnly(items.ToArray());
            Warnings = Array.AsReadOnly(warnings.ToArray());
            Total = total;
            Page = page;
            PageSize = pageSize;
        }

        public GameResourceCatalogReadStatus Status { get; }

        public string? CatalogVersion { get; }

        public string? GameVersion { get; }

        public DateTimeOffset? ObservedAtUtc { get; }

        public IReadOnlyList<GameResourceQueryItem> Items { get; }

        public IReadOnlyList<string> Warnings { get; }

        public int Total { get; }

        public int Page { get; }

        public int PageSize { get; }
    }
}
