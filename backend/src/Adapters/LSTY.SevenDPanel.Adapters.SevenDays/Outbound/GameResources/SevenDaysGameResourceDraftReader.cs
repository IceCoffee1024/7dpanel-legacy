using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources
{
    internal sealed class SevenDaysGameResourceDraftReader
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly Func<
            string,
            Func<GameResourceScalarDraft>,
            TimeSpan,
            CancellationToken,
            Task<GameResourceScalarDraft>> dispatch;
        private readonly Func<GameResourceScalarDraft> capture;

        public SevenDaysGameResourceDraftReader()
            : this(
                (operationName, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(
                        operationName,
                        action,
                        timeout,
                        cancellationToken),
                CaptureScalarDraft)
        {
        }

        internal SevenDaysGameResourceDraftReader(
            Func<
                string,
                Func<GameResourceScalarDraft>,
                TimeSpan,
                CancellationToken,
                Task<GameResourceScalarDraft>> dispatch,
            Func<GameResourceScalarDraft> capture)
        {
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        }

        public Task<GameResourceScalarDraft> ReadAsync(CancellationToken cancellationToken) =>
            dispatch(
                "7dpanel.game-resources.capture",
                capture,
                DispatchTimeout,
                cancellationToken);

        internal static GameResourceScalarDraft Normalize(
            string? gameVersion,
            DateTimeOffset observedAtUtc,
            IEnumerable<GameResourceCapturedEntry?> capturedResources,
            GameResourceLocalizationCapture? localization,
            IEnumerable<GameResourceIconRootDescriptor> iconRoots,
            IEnumerable<string>? captureWarnings = null)
        {
            if (capturedResources == null) throw new ArgumentNullException(nameof(capturedResources));
            if (iconRoots == null) throw new ArgumentNullException(nameof(iconRoots));

            var warnings = captureWarnings?.ToList() ?? new List<string>();
            var candidates = new List<IndexedCapturedEntry>();
            var index = 0;
            foreach (var captured in capturedResources)
            {
                if (captured == null)
                {
                    index++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(captured.InternalName))
                {
                    warnings.Add("resource-name-invalid");
                    index++;
                    continue;
                }

                candidates.Add(new IndexedCapturedEntry(index++, captured));
            }

            var winners = new List<IndexedCapturedEntry>();
            foreach (var group in candidates.GroupBy(
                         candidate => candidate.Entry.InternalName!,
                         StringComparer.Ordinal))
            {
                if (group.Count() == 1)
                {
                    winners.Add(group.Single());
                    continue;
                }

                var proven = group.Where(candidate => candidate.Entry.IsFinalDefinition).ToArray();
                if (proven.Length != 1)
                    throw new GameResourceCatalogAmbiguousException();

                winners.Add(proven[0]);
                warnings.Add("resource-duplicate-resolved");
            }

            var englishColumn = FindLanguageColumn(localization, "english");
            var simplifiedChineseColumn = FindLanguageColumn(localization, "schinese");
            var resources = winners
                .OrderBy(winner => winner.Index)
                .Select(winner => NormalizeEntry(
                    winner.Entry,
                    localization,
                    englishColumn,
                    simplifiedChineseColumn,
                    warnings))
                .ToArray();

            return new GameResourceScalarDraft(
                NullIfWhiteSpace(gameVersion),
                observedAtUtc,
                resources,
                iconRoots,
                warnings);
        }

        private static GameResourceScalarEntry NormalizeEntry(
            GameResourceCapturedEntry captured,
            GameResourceLocalizationCapture? localization,
            int englishColumn,
            int simplifiedChineseColumn,
            ICollection<string> warnings)
        {
            var creativeModeKnown = GameResourceCreativeMode.IsKnown(captured.CreativeMode);
            if (!creativeModeKnown)
                warnings.Add("resource-creative-mode-unknown");

            int? maxStack = captured.MaxStack;
            if (!maxStack.HasValue)
            {
                warnings.Add("resource-max-stack-unavailable");
            }
            else if (maxStack.Value < 1)
            {
                maxStack = null;
                warnings.Add("resource-max-stack-invalid");
            }

            if (!captured.HasQuality.HasValue)
                warnings.Add("resource-quality-unavailable");

            var tint = NormalizeTint(captured.IconTint, warnings);
            return new GameResourceScalarEntry(
                captured.NumericId,
                captured.InternalName!,
                captured.IsBlock,
                creativeModeKnown && GameResourceCreativeMode.IsPublic(captured.CreativeMode),
                maxStack,
                captured.HasQuality,
                NullIfWhiteSpace(captured.IconName),
                tint,
                ReadLocalization(
                    localization,
                    captured.InternalName!,
                    simplifiedChineseColumn),
                ReadLocalization(localization, captured.InternalName!, englishColumn));
        }

        private static int FindLanguageColumn(
            GameResourceLocalizationCapture? localization,
            string language)
        {
            if (localization == null) return -1;
            for (var index = 0; index < localization.Languages.Count; index++)
            {
                if (string.Equals(
                        localization.Languages[index],
                        language,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string? ReadLocalization(
            GameResourceLocalizationCapture? localization,
            string key,
            int column)
        {
            if (localization == null || column < 0 ||
                !localization.Entries.TryGetValue(key, out var values) ||
                column >= values.Length)
            {
                return null;
            }

            return NullIfWhiteSpace(values[column]);
        }

        private static string? NormalizeTint(
            GameResourceCapturedTint? tint,
            ICollection<string> warnings)
        {
            if (tint == null) return null;
            if (!IsColorComponent(tint.Red) ||
                !IsColorComponent(tint.Green) ||
                !IsColorComponent(tint.Blue) ||
                !IsColorComponent(tint.Alpha))
            {
                warnings.Add("resource-icon-tint-invalid");
                return null;
            }

            if (tint.Red == 1f && tint.Green == 1f && tint.Blue == 1f)
                return null;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:X2}{1:X2}{2:X2}",
                ToByte(tint.Red),
                ToByte(tint.Green),
                ToByte(tint.Blue));
        }

        private static bool IsColorComponent(float component) =>
            !float.IsNaN(component) &&
            !float.IsInfinity(component) &&
            component >= 0f &&
            component <= 1f;

        private static int ToByte(float component) =>
            (int)Math.Round(component * 255d, MidpointRounding.AwayFromZero);

        private static string? NullIfWhiteSpace(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        private static GameResourceScalarDraft CaptureScalarDraft()
        {
            var warnings = new List<string>();
            var captured = new List<GameResourceCapturedEntry?>();
            var itemClasses = global::ItemClass.list;
            if (itemClasses == null)
                throw new InvalidOperationException("The game item catalog is not available.");

            foreach (var itemClass in itemClasses)
            {
                if (itemClass == null)
                {
                    captured.Add(null);
                    continue;
                }

                var internalName = itemClass.GetItemName();
                int? maxStack = null;
                bool? hasQuality = null;
                string? iconName = null;
                GameResourceCapturedTint? tint = null;
                try { maxStack = itemClass.Stacknumber?.Value; }
                catch { warnings.Add("resource-max-stack-unavailable"); }
                try { hasQuality = itemClass.HasQuality; }
                catch { warnings.Add("resource-quality-unavailable"); }
                try { iconName = itemClass.GetIconName(); }
                catch { warnings.Add("resource-icon-name-unavailable"); }
                try
                {
                    var color = itemClass.GetIconTint(null);
                    tint = new GameResourceCapturedTint(color.r, color.g, color.b, color.a);
                }
                catch
                {
                    warnings.Add("resource-icon-tint-unavailable");
                }

                captured.Add(new GameResourceCapturedEntry(
                    itemClass.Id,
                    internalName,
                    itemClass.IsBlock(),
                    (int)itemClass.CreativeMode,
                    maxStack,
                    hasQuality,
                    iconName,
                    tint,
                    !string.IsNullOrWhiteSpace(internalName) &&
                    ReferenceEquals(
                        global::ItemClass.GetItemClass(internalName, false),
                        itemClass)));
            }

            var roots = CaptureIconRoots(warnings);
            var localization = CaptureLocalization(captured, warnings);
            string? gameVersion = null;
            try
            {
                gameVersion = global::GamePrefs.GetString(global::EnumGamePrefs.GameVersion);
            }
            catch
            {
                warnings.Add("game-version-unavailable");
            }

            return Normalize(
                gameVersion,
                DateTimeOffset.UtcNow,
                captured,
                localization,
                roots,
                warnings);
        }

        private static IReadOnlyList<GameResourceIconRootDescriptor> CaptureIconRoots(
            ICollection<string> warnings)
        {
            var roots = new List<GameResourceIconRootDescriptor>();
            roots.Add(new GameResourceIconRootDescriptor(
                0,
                "base",
                global::GameIO.GetGameDir("Data/ItemIcons")));

            var precedence = 1;
            foreach (var mod in global::ModManager.GetLoadedMods())
            {
                if (mod == null || string.IsNullOrWhiteSpace(mod.Path))
                {
                    warnings.Add("mod-icon-root-unavailable");
                    continue;
                }

                roots.Add(new GameResourceIconRootDescriptor(
                    precedence++,
                    string.IsNullOrWhiteSpace(mod.Name) ? "mod" : mod.Name,
                    global::System.IO.Path.Combine(mod.Path, "ItemIcons")));
            }

            return roots;
        }

        private static GameResourceLocalizationCapture? CaptureLocalization(
            IEnumerable<GameResourceCapturedEntry?> resources,
            ICollection<string> warnings)
        {
            try
            {
                var dictionary = global::Localization.Dictionary;
                if (dictionary == null ||
                    !dictionary.TryGetValue(global::Localization.HeaderKey, out var languages) ||
                    languages == null)
                {
                    warnings.Add("localization-metadata-unavailable");
                    return null;
                }

                var entries = new Dictionary<string, string[]>(StringComparer.Ordinal);
                foreach (var resource in resources)
                {
                    var key = resource?.InternalName;
                    if (string.IsNullOrWhiteSpace(key) || entries.ContainsKey(key!))
                        continue;
                    if (dictionary.TryGetValue(key!, out var values) && values != null)
                        entries[key!] = values.ToArray();
                }

                return new GameResourceLocalizationCapture(languages.ToArray(), entries);
            }
            catch
            {
                warnings.Add("localization-metadata-unavailable");
                return null;
            }
        }

        private sealed class IndexedCapturedEntry
        {
            public IndexedCapturedEntry(int index, GameResourceCapturedEntry entry)
            {
                Index = index;
                Entry = entry;
            }

            public int Index { get; }
            public GameResourceCapturedEntry Entry { get; }
        }
    }
}
