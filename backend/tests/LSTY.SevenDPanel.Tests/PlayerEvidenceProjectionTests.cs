using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerEvidenceProjectionTests
    {
        [Fact]
        public void Join_save_and_disconnect_create_only_combined_identity_evidence()
        {
            Action<PlayerEvidenceIdentitySource?>? joined = null;
            Action<PlayerEvidenceDraft?>? saved = null;
            Action<PlayerEvidenceIdentitySource?>? disconnected = null;
            var recorded = new List<PlayerEvidenceDraft>();
            var now = Utc(1);
            using var projection = new SevenDaysPlayerEvidenceProjection(
                handler => Subscribe(value => joined = value, handler),
                handler => Subscribe(value => saved = value, handler),
                handler => Subscribe(value => disconnected = value, handler),
                draft => { recorded.Add(draft); return true; },
                PanelPlayerEvidenceOptions.Default,
                () => "world-a",
                () => now);

            projection.Start();
            joined!(new PlayerEvidenceIdentitySource(null));
            saved!(null);
            disconnected!(new PlayerEvidenceIdentitySource(" "));
            Assert.Empty(recorded);

            joined(new PlayerEvidenceIdentitySource("EOS_player"));
            saved(SnapshotDraft(now.AddMinutes(1)));
            now = now.AddMinutes(2);
            disconnected(new PlayerEvidenceIdentitySource("EOS_player"));

            Assert.Equal(3, recorded.Count);
            Assert.Equal("Joined", recorded[0].Activity!.Kind);
            Assert.NotNull(recorded[0].Session);
            Assert.Null(recorded[0].Session!.EndedAtUtc);
            Assert.Equal("Saved", recorded[1].Activity!.Kind);
            Assert.NotNull(recorded[1].Inventory);
            Assert.NotNull(recorded[1].Skills);
            Assert.Equal("Disconnected", recorded[2].Activity!.Kind);
            Assert.Equal(now, recorded[2].Session!.EndedAtUtc);
            Assert.Equal(recorded[0].Session!.SessionId, recorded[2].Session!.SessionId);
        }

        [Fact]
        public void Save_without_join_opens_a_partial_session_and_keeps_it_open()
        {
            Action<PlayerEvidenceDraft?>? saved = null;
            var recorded = new List<PlayerEvidenceDraft>();
            var observation = SnapshotDraft(Utc(2));
            using var projection = new SevenDaysPlayerEvidenceProjection(
                handler => Subscription.Empty,
                handler => Subscribe(value => saved = value, handler),
                handler => Subscription.Empty,
                draft => { recorded.Add(draft); return true; },
                PanelPlayerEvidenceOptions.Default,
                () => "world-a",
                () => observation.ObservedAtUtc);

            projection.Start();
            saved!(observation);

            var draft = Assert.Single(recorded);
            Assert.NotNull(draft.Session);
            Assert.Null(draft.Session!.EndedAtUtc);
            Assert.Equal(PlayerProfileSectionState.Partial, draft.Session.Completeness);
            Assert.Equal(PlayerProfileSectionState.Partial, draft.Activity!.Completeness);
        }

        [Fact]
        public void Scalar_reader_copies_all_containers_and_retains_names_when_catalog_is_unavailable()
        {
            var items = new List<InventoryItemScalar>
            {
                Item("bag", 0, "resourceFood", 2, 3, 1.25m, "modFood"),
                Item("toolbelt", 1, "meleeTool", 1, 5, 2.5m, "modGrip"),
                Item("equipment", 2, "armorHead", 1, 6, 0m, "modArmor")
            };
            var values = new List<PlayerSkillValue>
            {
                new PlayerSkillValue("perkAllowed", SkillValueState.Known, 2, 0, 5, 3, "attributeFortitude")
            };

            var draft = SevenDaysPlayerEvidenceSnapshotReader.CreateDraft(
                "EOS_player",
                "local",
                "world-a",
                Utc(3),
                "V 3.0.1 (b4)",
                GameResourceCatalogReadResult.Unavailable(),
                items,
                new PlayerPosition(10, 20, 30),
                7,
                4,
                values);

            items.Clear();
            values.Clear();

            Assert.Equal(CatalogResolutionState.Unavailable, draft.Inventory!.CatalogResolution);
            Assert.Null(draft.Inventory.CatalogVersion);
            Assert.Equal(new[] { "bag", "toolbelt", "equipment" },
                draft.Inventory.Items.Select(item => item.Container));
            Assert.Equal(new[] { "resourceFood", "meleeTool", "armorHead" },
                draft.Inventory.Items.Select(item => item.InternalName));
            Assert.Equal(new int?[] { 3, 5, 6 }, draft.Inventory.Items.Select(item => item.Quality));
            Assert.Equal(new decimal?[] { 1.25m, 2.5m, 0m }, draft.Inventory.Items.Select(item => item.UseAmount));
            Assert.Equal(new[] { "modFood", "modGrip", "modArmor" },
                draft.Inventory.Items.Select(item => Assert.Single(item.ModInternalNames)));
            Assert.Equal(7, draft.Skills!.Level);
            Assert.Equal(4, draft.Skills.SkillPoints);
            Assert.Equal("perkAllowed", Assert.Single(draft.Skills.Values).SkillKey);
            Assert.Equal((10f, 20f, 30f), (draft.Position!.Value.X, draft.Position.Value.Y, draft.Position.Value.Z));
        }

        [Fact]
        public void Progression_reader_keeps_only_approved_keys_and_distinguishes_unavailable_states()
        {
            var definitions = new[]
            {
                new PlayerEvidenceProgressionDefinition("perkAllowed", 0, 5, "attributeFortitude")
            };
            using var supported = ProgressionStream(
                3,
                8,
                6,
                ("perkAllowed", 2, 3),
                ("perkNotApproved", 5, 0));

            var known = SevenDaysPlayerEvidenceSnapshotReader.ReadProgression(supported, definitions);
            var unsupported = SevenDaysPlayerEvidenceSnapshotReader.ReadProgression(
                ProgressionStream(99, 8, 6), definitions);
            var notLoaded = SevenDaysPlayerEvidenceSnapshotReader.ReadProgression(null, definitions);

            Assert.Equal(8, known.Level);
            Assert.Equal(6, known.SkillPoints);
            var value = Assert.Single(known.Values);
            Assert.Equal("perkAllowed", value.SkillKey);
            Assert.Equal(SkillValueState.Known, value.State);
            Assert.Equal(2, value.Value);
            Assert.Equal(3, value.NextLevelCost);
            Assert.Equal(SkillValueState.UnsupportedByVersion, Assert.Single(unsupported.Values).State);
            Assert.Null(unsupported.Level);
            Assert.Null(unsupported.SkillPoints);
            Assert.Equal(SkillValueState.NotLoaded, Assert.Single(notLoaded.Values).State);
            Assert.Null(notLoaded.Level);
            Assert.Null(notLoaded.SkillPoints);
        }

        [Fact]
        public void Draft_public_state_contains_no_game_runtime_objects()
        {
            var gameAssemblyNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "Assembly-CSharp",
                "UnityEngine.CoreModule"
            };
            var draftTypes = new[]
            {
                typeof(PlayerEvidenceDraft),
                typeof(PlayerEvidenceSessionDraft),
                typeof(PlayerEvidenceActivityDraft),
                typeof(PlayerEvidenceInventoryDraft),
                typeof(PlayerEvidenceSkillDraft),
                typeof(PlayerEvidenceProgressionDraft)
            };
            var exposedTypes = draftTypes
                .SelectMany(type => type.GetProperties()
                    .Select(property => property.PropertyType)
                    .Concat(type.GetFields(
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic)
                        .Select(field => field.FieldType)))
                .SelectMany(ContainedTypes);

            Assert.DoesNotContain(exposedTypes, type =>
                gameAssemblyNames.Contains(type.Assembly.GetName().Name ?? string.Empty));
        }

        private static PlayerEvidenceDraft SnapshotDraft(DateTimeOffset observedAtUtc) =>
            SevenDaysPlayerEvidenceSnapshotReader.CreateDraft(
                "EOS_player",
                "local",
                "world-a",
                observedAtUtc,
                "V 3.0.1 (b4)",
                GameResourceCatalogReadResult.Unavailable(),
                new[] { Item("bag", 0, "resourceFood", 1, null, null) },
                new PlayerPosition(1, 2, 3),
                1,
                0,
                Array.Empty<PlayerSkillValue>());

        private static InventoryItemScalar Item(
            string container,
            int slot,
            string internalName,
            int count,
            int? quality,
            decimal? useAmount,
            params string[] mods) =>
            new InventoryItemScalar(container, slot, internalName, count, quality, useAmount, mods);

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 27, 10, minute, 0, TimeSpan.Zero);

        private static IEnumerable<Type> ContainedTypes(Type type)
        {
            yield return type;
            foreach (var argument in type.GetGenericArguments())
                foreach (var contained in ContainedTypes(argument))
                    yield return contained;
            if (type.HasElementType && type.GetElementType() is Type elementType)
                foreach (var contained in ContainedTypes(elementType))
                    yield return contained;
        }

        private static MemoryStream ProgressionStream(
            int version,
            ushort level,
            ushort skillPoints,
            params (string Key, byte Value, int Cost)[] values)
        {
            var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(version);
                writer.Write(level);
                writer.Write(100);
                writer.Write(skillPoints);
                writer.Write(values.Length);
                foreach (var value in values)
                {
                    writer.Write((byte)1);
                    writer.Write(value.Key);
                    writer.Write(value.Value);
                    writer.Write(value.Cost);
                }
                writer.Write(0);
            }
            stream.Position = 0;
            return stream;
        }

        private static IDisposable Subscribe<T>(Action<Action<T>?> assign, Action<T> handler)
        {
            assign(handler);
            return new Subscription(() => assign(null));
        }

        private sealed class Subscription : IDisposable
        {
            private Action? dispose;
            public Subscription(Action dispose) => this.dispose = dispose;
            public static IDisposable Empty { get; } = new Subscription(() => { });
            public void Dispose() => System.Threading.Interlocked.Exchange(ref dispose, null)?.Invoke();
        }
    }
}
