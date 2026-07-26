using System;
using System.Linq;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class GameResourcePageHttpResponse
    {
        public GameResourcePageHttpResponse(GameResourceQueryResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.Status != GameResourceCatalogReadStatus.Available)
                throw new ArgumentException("An available game-resource result is required.", nameof(result));

            CatalogVersion = result.CatalogVersion ??
                throw new ArgumentException("The catalog version is required.", nameof(result));
            GameVersion = result.GameVersion;
            ObservedAtUtc = result.ObservedAtUtc ??
                throw new ArgumentException("The catalog observation time is required.", nameof(result));
            Total = result.Total;
            Page = result.Page;
            PageSize = result.PageSize;
            Warnings = result.Warnings.ToArray();
            Items = result.Items.Select(item => new GameResourceItemHttpResponse(item)).ToArray();
        }

        public string CatalogVersion { get; }

        public string? GameVersion { get; }

        public DateTimeOffset ObservedAtUtc { get; }

        public int Total { get; }

        public int Page { get; }

        public int PageSize { get; }

        public string[] Warnings { get; }

        public GameResourceItemHttpResponse[] Items { get; }
    }

    public sealed class GameResourceItemHttpResponse
    {
        public GameResourceItemHttpResponse(GameResourceQueryItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            ResourceId = item.ResourceId;
            NumericId = item.NumericId;
            InternalName = item.InternalName;
            LocalizedName = item.LocalizedName;
            Kind = KindName(item.Kind);
            Visibility = VisibilityName(item.Visibility);
            MaxStack = item.MaxStack;
            HasQuality = item.HasQuality;
            IconStatus = IconStatusName(item.IconStatus);
            IconTintHex = item.IconTintHex;
        }

        public string ResourceId { get; }

        public int NumericId { get; }

        public string InternalName { get; }

        public string? LocalizedName { get; }

        public string Kind { get; }

        public string Visibility { get; }

        public int? MaxStack { get; }

        public bool? HasQuality { get; }

        public string IconStatus { get; }

        public string? IconTintHex { get; }

        private static string KindName(GameResourceKind kind)
        {
            switch (kind)
            {
                case GameResourceKind.Item: return "item";
                case GameResourceKind.Block: return "block";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static string VisibilityName(GameResourceVisibility visibility)
        {
            switch (visibility)
            {
                case GameResourceVisibility.Public: return "public";
                case GameResourceVisibility.Hidden: return "hidden";
                default: throw new ArgumentOutOfRangeException(nameof(visibility));
            }
        }

        private static string IconStatusName(GameResourceIconStatus status)
        {
            switch (status)
            {
                case GameResourceIconStatus.Available: return "available";
                case GameResourceIconStatus.Missing: return "missing";
                case GameResourceIconStatus.Invalid: return "invalid";
                default: throw new ArgumentOutOfRangeException(nameof(status));
            }
        }
    }
}
