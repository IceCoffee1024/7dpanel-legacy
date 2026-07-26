using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources
{
    internal static class GameResourceCreativeMode
    {
        public const int All = 0;
        public const int Player = 1;
        public const int Console = 2;
        public const int Dev = 3;
        public const int Test = 4;
        public const int None = 5;

        public static bool IsKnown(int value) =>
            value >= All && value <= None;

        public static bool IsPublic(int value) =>
            value == All || value == Player;
    }

    internal sealed class GameResourceScalarDraft
    {
        public GameResourceScalarDraft(
            string? gameVersion,
            DateTimeOffset observedAtUtc,
            IEnumerable<GameResourceScalarEntry> resources,
            IEnumerable<GameResourceIconRootDescriptor> iconRoots,
            IEnumerable<string> warnings)
        {
            GameVersion = gameVersion;
            ObservedAtUtc = observedAtUtc;
            Resources = ReadOnly(resources, nameof(resources));
            IconRoots = ReadOnly(iconRoots, nameof(iconRoots));
            Warnings = ReadOnly(warnings, nameof(warnings));
        }

        public string? GameVersion { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public IReadOnlyList<GameResourceScalarEntry> Resources { get; }
        public IReadOnlyList<GameResourceIconRootDescriptor> IconRoots { get; }
        public IReadOnlyList<string> Warnings { get; }

        private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> source, string parameterName)
        {
            if (source == null) throw new ArgumentNullException(parameterName);
            return new ReadOnlyCollection<T>(source.ToArray());
        }
    }

    internal sealed class GameResourceScalarEntry
    {
        public GameResourceScalarEntry(
            int numericId,
            string internalName,
            bool isBlock,
            bool isPublic,
            int? maxStack,
            bool? hasQuality,
            string? iconName,
            string? iconTintHex,
            string? simplifiedChineseName,
            string? englishName)
        {
            NumericId = numericId;
            InternalName = internalName ?? throw new ArgumentNullException(nameof(internalName));
            IsBlock = isBlock;
            IsPublic = isPublic;
            MaxStack = maxStack;
            HasQuality = hasQuality;
            IconName = iconName;
            IconTintHex = iconTintHex;
            SimplifiedChineseName = simplifiedChineseName;
            EnglishName = englishName;
        }

        public int NumericId { get; }
        public string InternalName { get; }
        public bool IsBlock { get; }
        public bool IsPublic { get; }
        public int? MaxStack { get; }
        public bool? HasQuality { get; }
        public string? IconName { get; }
        public string? IconTintHex { get; }
        public string? SimplifiedChineseName { get; }
        public string? EnglishName { get; }
    }

    internal sealed class GameResourceIconRootDescriptor
    {
        public GameResourceIconRootDescriptor(int precedence, string sourceName, string rootPath)
        {
            if (precedence < 0) throw new ArgumentOutOfRangeException(nameof(precedence));
            if (string.IsNullOrWhiteSpace(sourceName))
                throw new ArgumentException("An icon root source name is required.", nameof(sourceName));
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("An icon root path is required.", nameof(rootPath));

            Precedence = precedence;
            SourceName = sourceName;
            RootPath = rootPath;
        }

        public int Precedence { get; }
        public string SourceName { get; }
        public string RootPath { get; }
    }

    internal sealed class GameResourceCapturedEntry
    {
        public GameResourceCapturedEntry(
            int numericId,
            string? internalName,
            bool isBlock,
            int creativeMode,
            int? maxStack,
            bool? hasQuality,
            string? iconName,
            GameResourceCapturedTint? iconTint,
            bool isFinalDefinition)
        {
            NumericId = numericId;
            InternalName = internalName;
            IsBlock = isBlock;
            CreativeMode = creativeMode;
            MaxStack = maxStack;
            HasQuality = hasQuality;
            IconName = iconName;
            IconTint = iconTint;
            IsFinalDefinition = isFinalDefinition;
        }

        public int NumericId { get; }
        public string? InternalName { get; }
        public bool IsBlock { get; }
        public int CreativeMode { get; }
        public int? MaxStack { get; }
        public bool? HasQuality { get; }
        public string? IconName { get; }
        public GameResourceCapturedTint? IconTint { get; }
        public bool IsFinalDefinition { get; }
    }

    internal sealed class GameResourceCapturedTint
    {
        public GameResourceCapturedTint(float red, float green, float blue, float alpha)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }

        public float Red { get; }
        public float Green { get; }
        public float Blue { get; }
        public float Alpha { get; }
    }

    internal sealed class GameResourceLocalizationCapture
    {
        public GameResourceLocalizationCapture(
            IEnumerable<string> languages,
            IReadOnlyDictionary<string, string[]> entries)
        {
            if (languages == null) throw new ArgumentNullException(nameof(languages));
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            Languages = new ReadOnlyCollection<string>(languages.ToArray());
            var copied = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var pair in entries)
                copied[pair.Key] = pair.Value?.ToArray() ?? Array.Empty<string>();
            Entries = new ReadOnlyDictionary<string, string[]>(copied);
        }

        public IReadOnlyList<string> Languages { get; }
        public IReadOnlyDictionary<string, string[]> Entries { get; }
    }

    internal sealed class GameResourceCatalogAmbiguousException : Exception
    {
        public GameResourceCatalogAmbiguousException()
            : base("The game resource catalog contains an ambiguous final definition.")
        {
        }
    }
}
