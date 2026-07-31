using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class SevenDaysPlayerEvidenceSnapshotReader
    {
        private const int SupportedProgressionVersion = 3;
        private const int MaximumProgressionValues = 10_000;
        private readonly PanelPlayerEvidenceOptions options;
        private readonly IGameResourceCatalog catalog;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly Func<string> worldId;
        private readonly Func<string> gameVersion;

        public SevenDaysPlayerEvidenceSnapshotReader(
            PanelPlayerEvidenceOptions options,
            IGameResourceCatalog catalog)
            : this(
                options,
                catalog,
                () => DateTimeOffset.UtcNow,
                () => GamePrefs.GetString(EnumGamePrefs.GameWorld),
                () => Constants.cVersionInformation.ToString())
        {
        }

        internal SevenDaysPlayerEvidenceSnapshotReader(
            PanelPlayerEvidenceOptions options,
            IGameResourceCatalog catalog,
            Func<DateTimeOffset> utcClock,
            Func<string> worldId,
            Func<string> gameVersion)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            this.worldId = worldId ?? throw new ArgumentNullException(nameof(worldId));
            this.gameVersion = gameVersion ?? throw new ArgumentNullException(nameof(gameVersion));
        }

        internal PlayerEvidenceDraft? Read(
            global::ClientInfo? client,
            global::PlayerDataFile? playerData)
        {
            if (!ThreadManager.IsMainThread())
                throw new InvalidOperationException("Player evidence must be read on the game thread.");
            var combinedId = Normalize(client?.CrossplatformId?.CombinedString);
            if (combinedId == null || client == null || playerData == null)
                return null;
            if (client.entityId < 0 || client.entityId != playerData.id)
                return null;

            var observedAtUtc = utcClock();
            PlayerEvidenceDraft.RequireUtc(observedAtUtc, nameof(observedAtUtc));
            var inventory = new List<InventoryItemScalar>();
            CopyStacks("bag", playerData.bag?.GetSlots(), inventory);
            CopyStacks("toolbelt", playerData.inventory, inventory);
            CopyEquipment(playerData.equipment?.GetItems(), inventory);
            var progression = ReadProgression(
                playerData.progressionData,
                ApprovedProgressionDefinitions());
            var catalogRead = ReadCatalog();
            PlayerPosition? position = null;
            if (playerData.ecd != null)
            {
                position = new PlayerPosition(
                    playerData.ecd.pos.x,
                    playerData.ecd.pos.y,
                    playerData.ecd.pos.z);
            }

            return CreateDraft(
                combinedId,
                options.ServerId,
                worldId(),
                observedAtUtc,
                gameVersion(),
                catalogRead,
                inventory,
                position,
                progression.Level,
                progression.SkillPoints,
                progression.Values);
        }

        internal static PlayerEvidenceDraft CreateDraft(
            string crossplatformId,
            string serverId,
            string worldId,
            DateTimeOffset observedAtUtc,
            string gameVersion,
            GameResourceCatalogReadResult catalog,
            IEnumerable<InventoryItemScalar> items,
            PlayerPosition? position,
            int? level,
            int? skillPoints,
            IEnumerable<PlayerSkillValue> progressionValues)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var itemCopy = items?.ToArray() ?? throw new ArgumentNullException(nameof(items));
            var valueCopy = progressionValues?.ToArray() ??
                throw new ArgumentNullException(nameof(progressionValues));
            var resolved = catalog.Status == GameResourceCatalogReadStatus.Available &&
                           catalog.Snapshot != null;
            var inventory = new PlayerEvidenceInventoryDraft(
                gameVersion,
                resolved ? catalog.Snapshot!.CatalogVersion : null,
                resolved ? CatalogResolutionState.Resolved : CatalogResolutionState.Unavailable,
                Fingerprint(itemCopy),
                false,
                itemCopy);
            var skills = new PlayerEvidenceSkillDraft(
                gameVersion,
                level,
                skillPoints,
                valueCopy);
            return new PlayerEvidenceDraft(
                crossplatformId,
                serverId,
                worldId,
                observedAtUtc,
                null,
                null,
                inventory,
                skills,
                position);
        }

        internal static PlayerEvidenceProgressionDraft ReadProgression(
            Stream? progressionData,
            IEnumerable<PlayerEvidenceProgressionDefinition> approvedDefinitions)
        {
            var definitions = approvedDefinitions?.ToArray() ??
                throw new ArgumentNullException(nameof(approvedDefinitions));
            if (progressionData == null || !progressionData.CanRead || !progressionData.CanSeek)
                return UnavailableProgression(definitions, SkillValueState.NotLoaded);

            long originalPosition;
            try { originalPosition = progressionData.Position; }
            catch { return UnavailableProgression(definitions, SkillValueState.NotLoaded); }

            try
            {
                using (var reader = new BinaryReader(progressionData, Encoding.UTF8, true))
                {
                    if (reader.ReadInt32() != SupportedProgressionVersion)
                        return UnavailableProgression(
                            definitions,
                            SkillValueState.UnsupportedByVersion);

                    var level = (int)reader.ReadUInt16();
                    reader.ReadInt32();
                    var skillPoints = (int)reader.ReadUInt16();
                    var valueCount = reader.ReadInt32();
                    if (valueCount < 0 || valueCount > MaximumProgressionValues)
                        return UnavailableProgression(definitions, SkillValueState.NotLoaded);

                    var approved = definitions.ToDictionary(
                        definition => definition.SkillKey,
                        StringComparer.Ordinal);
                    var known = new Dictionary<string, (int Value, int Cost)>(StringComparer.Ordinal);
                    for (var index = 0; index < valueCount; index++)
                    {
                        reader.ReadByte();
                        var key = reader.ReadString();
                        var value = (int)reader.ReadByte();
                        var cost = reader.ReadInt32();
                        if (approved.ContainsKey(key)) known[key] = (value, cost);
                    }
                    reader.ReadInt32();

                    return new PlayerEvidenceProgressionDraft(
                        level,
                        skillPoints,
                        definitions.Select(definition =>
                        {
                            if (!known.TryGetValue(definition.SkillKey, out var scalar))
                            {
                                return new PlayerSkillValue(
                                    definition.SkillKey,
                                    SkillValueState.Unknown,
                                    null,
                                    definition.Minimum,
                                    definition.Maximum,
                                    null,
                                    definition.ParentKey);
                            }
                            return new PlayerSkillValue(
                                definition.SkillKey,
                                SkillValueState.Known,
                                scalar.Value,
                                definition.Minimum,
                                definition.Maximum,
                                scalar.Cost,
                                definition.ParentKey);
                        }));
                }
            }
            catch
            {
                return UnavailableProgression(definitions, SkillValueState.NotLoaded);
            }
            finally
            {
                try { progressionData.Position = originalPosition; } catch { }
            }
        }

        private GameResourceCatalogReadResult ReadCatalog()
        {
            try { return catalog.Read(); }
            catch { return GameResourceCatalogReadResult.Unavailable(); }
        }

        private static IEnumerable<PlayerEvidenceProgressionDefinition> ApprovedProgressionDefinitions()
        {
            var classes = Progression.ProgressionClasses;
            if (classes == null) return Array.Empty<PlayerEvidenceProgressionDefinition>();
            return classes.Values
                .Where(value => value != null && value.Enabled && !value.Hidden)
                .OrderBy(value => value.Name, StringComparer.Ordinal)
                .Select(value => new PlayerEvidenceProgressionDefinition(
                    value.Name,
                    value.MinLevel,
                    value.MaxLevel,
                    value.ParentName))
                .ToArray();
        }

        private static void CopyStacks(
            string container,
            global::ItemStack[]? stacks,
            ICollection<InventoryItemScalar> target)
        {
            if (stacks == null) return;
            for (var slot = 0; slot < stacks.Length; slot++)
            {
                var stack = stacks[slot];
                if (stack == null || stack.IsEmpty()) continue;
                var item = CopyItem(container, slot, stack.itemValue, stack.count);
                if (item != null) target.Add(item);
            }
        }

        private static void CopyEquipment(
            global::ItemValue[]? values,
            ICollection<InventoryItemScalar> target)
        {
            if (values == null) return;
            for (var slot = 0; slot < values.Length; slot++)
            {
                var value = values[slot];
                if (value == null || value.IsEmpty()) continue;
                var item = CopyItem("equipment", slot, value, 1);
                if (item != null) target.Add(item);
            }
        }

        private static InventoryItemScalar? CopyItem(
            string container,
            int slot,
            global::ItemValue? itemValue,
            int count)
        {
            if (itemValue == null || itemValue.IsEmpty() || count <= 0) return null;
            var itemClass = itemValue.ItemClass;
            var internalName = Normalize(itemClass?.GetItemName());
            if (internalName == null) return null;
            decimal? useAmount = null;
            if (!float.IsNaN(itemValue.UseTimes) &&
                !float.IsInfinity(itemValue.UseTimes) &&
                itemValue.UseTimes >= 0)
            {
                useAmount = (decimal)itemValue.UseTimes;
            }
            var mods = CopyModInternalNames(
                ReadModInternalNames(itemValue.Modifications),
                ReadModInternalNames(itemValue.CosmeticMods));
            return new InventoryItemScalar(
                container,
                slot,
                internalName,
                count,
                itemValue.Quality == 0 ? null : (int?)itemValue.Quality,
                useAmount,
                mods);
        }

        internal static IReadOnlyList<string> CopyModInternalNames(
            IEnumerable<string?>? modifications,
            IEnumerable<string?>? cosmeticMods) =>
            (modifications ?? Array.Empty<string?>())
                .Concat(cosmeticMods ?? Array.Empty<string?>())
                .Select(Normalize)
                .Where(value => value != null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        private static IEnumerable<string?> ReadModInternalNames(
            IEnumerable<global::ItemValue>? values) =>
            (values ?? Array.Empty<global::ItemValue>())
                .Where(value => value != null && !value.IsEmpty() &&
                                value.ItemClass is global::ItemClassModifier)
                .Select(value => value.ItemClass?.GetItemName());

        private static PlayerEvidenceProgressionDraft UnavailableProgression(
            IEnumerable<PlayerEvidenceProgressionDefinition> definitions,
            SkillValueState state)
        {
            return new PlayerEvidenceProgressionDraft(
                null,
                null,
                definitions.Select(definition => new PlayerSkillValue(
                    definition.SkillKey,
                    state,
                    null,
                    definition.Minimum,
                    definition.Maximum,
                    null,
                    definition.ParentKey)));
        }

        private static string Fingerprint(IEnumerable<InventoryItemScalar> items)
        {
            var canonical = new StringBuilder();
            foreach (var item in items.OrderBy(value => value.Container, StringComparer.Ordinal)
                         .ThenBy(value => value.Slot))
            {
                canonical.Append(item.Container).Append('\u001f')
                    .Append(item.Slot.ToString(CultureInfo.InvariantCulture)).Append('\u001f')
                    .Append(item.InternalName).Append('\u001f')
                    .Append(item.Count.ToString(CultureInfo.InvariantCulture)).Append('\u001f')
                    .Append(item.Quality?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('\u001f')
                    .Append(item.UseAmount?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('\u001f')
                    .Append(string.Join("\u001e", item.ModInternalNames)).Append('\n');
            }
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(
                        Encoding.UTF8.GetBytes(canonical.ToString())))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }
}
