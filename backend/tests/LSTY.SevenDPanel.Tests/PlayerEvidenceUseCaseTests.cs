using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Application")]
    public sealed class PlayerEvidenceUseCaseTests
    {
        [Fact]
        public void Non_owner_receives_forbidden_sections_without_reading_sources()
        {
            var history = new TestHistoryStore { Player = Details() };
            var evidence = PopulatedEvidenceStore();
            var operations = new TestOperationQuery();
            var range = Range(Utc(0), Utc(59));

            var profile = new GetPlayerProfileUseCase(history, evidence, TimeZoneInfo.Utc)
                .Execute(range, PlayerEvidenceAccess.Standard);
            var inventories = new GetInventorySnapshotsUseCase(evidence)
                .Execute(new PlayerInventorySnapshotsQuery(PlayerId, 10, null), PlayerEvidenceAccess.Standard);
            var skills = new GetPlayerSkillsUseCase(evidence)
                .Execute(new PlayerSkillSnapshotsQuery(PlayerId, 10, null), PlayerEvidenceAccess.Standard);
            var diffs = new GetInventoryDiffsUseCase(
                    evidence,
                    operations,
                    new PlayerInventoryDiffService())
                .Execute(
                    new PlayerInventoryDiffsQuery(PlayerId, 10, null),
                    PlayerEvidenceAccess.Standard,
                    Array.Empty<string>());

            Assert.Equal(PlayerProfileSectionState.Forbidden, profile.Summary.State);
            Assert.Equal(PlayerProfileSectionState.Forbidden, profile.Sessions.State);
            Assert.Equal(PlayerProfileSectionState.Forbidden, profile.Activity.State);
            Assert.Equal(PlayerProfileSectionState.Forbidden, profile.Inventory.State);
            Assert.Equal(PlayerProfileSectionState.Forbidden, profile.Skills.State);
            Assert.Equal(PlayerProfileSectionState.Forbidden, profile.DailyActivity.State);
            Assert.Equal(PlayerProfileSectionState.Forbidden, inventories.State);
            Assert.Equal(PlayerProfileSectionState.Forbidden, skills.State);
            Assert.Equal(PlayerProfileSectionState.Forbidden, diffs.State);
            Assert.Equal(0, history.GetPlayerCalls);
            Assert.Equal(0, evidence.ReadCalls);
            Assert.Equal(0, operations.GetCalls);
        }

        [Fact]
        public void Profile_aggregates_only_the_stable_identity_and_preserves_each_source_observation()
        {
            var history = new TestHistoryStore { Player = Details() };
            var evidence = new TestEvidenceStore();
            evidence.Sessions.Add(Session(1, Utc(5), null, PlayerProfileSectionState.Partial));
            evidence.Activity.Add(Activity(1, "PlayerJoined", Utc(10)));
            evidence.InventorySnapshots.Add(Inventory(
                11,
                Utc(20),
                CatalogResolutionState.Unavailable,
                Item("resourceWood", 2)));
            evidence.InventoryGaps.Add(Gap(1, Utc(18), Utc(19)));
            evidence.SkillSnapshots.Add(Skills(
                21,
                Utc(30),
                new PlayerSkillValue(
                    "perkMiner69r",
                    SkillValueState.NotLoaded,
                    null,
                    null,
                    null,
                    null,
                    null)));
            evidence.SkillGaps.Add(Gap(2, Utc(25), Utc(26)));

            var profile = new GetPlayerProfileUseCase(history, evidence, TimeZoneInfo.Utc)
                .Execute(Range(Utc(0), Utc(59)), PlayerEvidenceAccess.Owner);

            Assert.Equal(PlayerId, profile.CrossplatformId);
            Assert.Equal("Alice", profile.Summary.Value?.LatestName);
            Assert.Equal(Utc(40), profile.Summary.ObservedAtUtc);
            Assert.Equal(PlayerProfileSectionState.Partial, profile.Sessions.State);
            Assert.Null(Assert.Single(profile.Sessions.Value!).EndedAtUtc);
            Assert.Equal(1f, Assert.Single(profile.Sessions.Value!).LastPosition?.X);
            Assert.Equal(Utc(5), profile.Sessions.ObservedAtUtc);
            Assert.Equal(PlayerProfileSectionState.Available, profile.Activity.State);
            Assert.Equal(Utc(10), profile.Activity.ObservedAtUtc);
            Assert.Equal(PlayerProfileSectionState.Partial, profile.Inventory.State);
            Assert.Equal(Utc(20), profile.Inventory.ObservedAtUtc);
            Assert.Equal(CatalogResolutionState.Unavailable, profile.Inventory.Value?.CatalogResolution);
            Assert.Single(profile.Inventory.GapMetadata);
            Assert.Equal(PlayerProfileSectionState.Partial, profile.Skills.State);
            Assert.Equal(Utc(30), profile.Skills.ObservedAtUtc);
            Assert.Equal(SkillValueState.NotLoaded, Assert.Single(profile.Skills.Value!.Values).State);
            Assert.Single(profile.Skills.GapMetadata);

            var daily = Assert.Single(profile.DailyActivity.Value!);
            Assert.Equal("2026-07-26", daily.LocalDate);
            Assert.Equal(1, daily.SessionCount);
            Assert.Equal(1, daily.LoginCount);
            Assert.Null(daily.ChatMessageCount);
            Assert.Null(daily.DeathCount);
            Assert.Null(daily.KillCount);
            Assert.Null(daily.InventoryObservationCount);
            Assert.Equal(PlayerProfileSectionState.Partial, profile.DailyActivity.State);
        }

        [Theory]
        [InlineData("summary")]
        [InlineData("sessions")]
        [InlineData("activity")]
        [InlineData("inventory")]
        [InlineData("skills")]
        public void A_source_failure_only_makes_its_own_profile_section_unavailable(string source)
        {
            var history = new TestHistoryStore { Player = Details(), ThrowOnGet = source == "summary" };
            var evidence = PopulatedEvidenceStore();
            evidence.ThrowSessions = source == "sessions";
            evidence.ThrowActivity = source == "activity";
            evidence.ThrowInventory = source == "inventory";
            evidence.ThrowSkills = source == "skills";

            var profile = new GetPlayerProfileUseCase(history, evidence, TimeZoneInfo.Utc)
                .Execute(Range(Utc(0), Utc(59)), PlayerEvidenceAccess.Owner);

            Assert.Equal(
                source == "summary" ? PlayerProfileSectionState.Unavailable : PlayerProfileSectionState.Available,
                profile.Summary.State);
            Assert.Equal(
                source == "sessions" ? PlayerProfileSectionState.Unavailable : PlayerProfileSectionState.Available,
                profile.Sessions.State);
            Assert.Equal(
                source == "activity" ? PlayerProfileSectionState.Unavailable : PlayerProfileSectionState.Available,
                profile.Activity.State);
            Assert.Equal(
                source == "inventory" ? PlayerProfileSectionState.Unavailable : PlayerProfileSectionState.Available,
                profile.Inventory.State);
            Assert.Equal(
                source == "skills" ? PlayerProfileSectionState.Unavailable : PlayerProfileSectionState.Available,
                profile.Skills.State);
            Assert.NotEqual(PlayerProfileSectionState.Unavailable, profile.DailyActivity.State);
        }

        [Theory]
        [InlineData(2026, 3, 8, 9, 30)]
        [InlineData(2026, 11, 1, 8, 30)]
        public void Daily_summary_uses_the_explicit_time_zone_across_DST_without_loss_or_duplication(
            int year,
            int month,
            int day,
            int hour,
            int minute)
        {
            var first = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
            var second = first.AddHours(1);
            var evidence = new TestEvidenceStore();
            evidence.Sessions.Add(Session(1, first, first.AddMinutes(10), PlayerProfileSectionState.Available));
            evidence.Sessions.Add(Session(2, second, second.AddMinutes(10), PlayerProfileSectionState.Available));
            evidence.Activity.Add(Activity(1, "PlayerJoined", first));
            evidence.Activity.Add(Activity(2, "PlayerJoined", second));

            var profile = new GetPlayerProfileUseCase(
                    new TestHistoryStore { Player = Details() },
                    evidence,
                    PacificTimeZone())
                .Execute(Range(first.AddMinutes(-1), second.AddMinutes(11)), PlayerEvidenceAccess.Owner);

            var daily = Assert.Single(profile.DailyActivity.Value!);
            Assert.Equal(2, daily.SessionCount);
            Assert.Equal(2, daily.LoginCount);
        }

        [Fact]
        public void Gap_only_day_keeps_unknown_inventory_count_nullable_instead_of_zero()
        {
            var evidence = new TestEvidenceStore();
            evidence.InventoryGaps.Add(Gap(1, Utc(15), Utc(20)));

            var profile = new GetPlayerProfileUseCase(
                    new TestHistoryStore { Player = Details() },
                    evidence,
                    TimeZoneInfo.Utc)
                .Execute(Range(Utc(0), Utc(59)), PlayerEvidenceAccess.Owner);

            var daily = Assert.Single(profile.DailyActivity.Value!);
            Assert.Null(daily.InventoryObservationCount);
            Assert.Equal(PlayerProfileSectionState.Partial, profile.DailyActivity.State);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(PlayerInventorySnapshotsQuery.MaximumPageSize + 1)]
        public void Evidence_queries_reject_page_sizes_outside_the_contract(int pageSize)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PlayerInventorySnapshotsQuery(PlayerId, pageSize, null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PlayerInventoryDiffsQuery(PlayerId, pageSize, null));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PlayerSkillSnapshotsQuery(PlayerId, pageSize, null));
        }

        [Fact]
        public void Evidence_cursor_rejects_non_UTC_time_and_non_positive_id()
        {
            Assert.Throws<ArgumentException>(() =>
                new PlayerEvidenceCursor(
                    new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.FromHours(8)),
                    1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerEvidenceCursor(Utc(1), 0));
        }

        [Fact]
        public void Inventory_snapshot_keyset_breaks_same_time_ties_by_id_without_overlap()
        {
            var evidence = new TestEvidenceStore();
            evidence.InventorySnapshots.AddRange(new[]
            {
                Inventory(1, Utc(10)),
                Inventory(3, Utc(20)),
                Inventory(2, Utc(20)),
                Inventory(4, Utc(30))
            });
            var useCase = new GetInventorySnapshotsUseCase(evidence);

            var first = useCase.Execute(
                new PlayerInventorySnapshotsQuery(PlayerId, 2, null),
                PlayerEvidenceAccess.Owner);
            var second = useCase.Execute(
                new PlayerInventorySnapshotsQuery(PlayerId, 2, first.Value!.NextCursor),
                PlayerEvidenceAccess.Owner);

            Assert.Equal(new long[] { 4, 3 }, first.Value.Snapshots.Select(snapshot => snapshot.SnapshotId));
            Assert.Equal(new long[] { 2, 1 }, second.Value!.Snapshots.Select(snapshot => snapshot.SnapshotId));
            Assert.Empty(first.Value.Snapshots.Select(snapshot => snapshot.SnapshotId)
                .Intersect(second.Value.Snapshots.Select(snapshot => snapshot.SnapshotId)));
            Assert.Null(second.Value.NextCursor);
        }

        [Fact]
        public void Skill_snapshot_keyset_breaks_same_time_ties_by_id_without_overlap()
        {
            var evidence = new TestEvidenceStore();
            evidence.SkillSnapshots.AddRange(new[]
            {
                Skills(1, Utc(10)),
                Skills(3, Utc(20)),
                Skills(2, Utc(20)),
                Skills(4, Utc(30))
            });
            var useCase = new GetPlayerSkillsUseCase(evidence);

            var first = useCase.Execute(
                new PlayerSkillSnapshotsQuery(PlayerId, 2, null),
                PlayerEvidenceAccess.Owner);
            var second = useCase.Execute(
                new PlayerSkillSnapshotsQuery(PlayerId, 2, first.Value!.NextCursor),
                PlayerEvidenceAccess.Owner);

            Assert.Equal(new long[] { 4, 3 }, first.Value.Snapshots.Select(snapshot => snapshot.SnapshotId));
            Assert.Equal(new long[] { 2, 1 }, second.Value!.Snapshots.Select(snapshot => snapshot.SnapshotId));
            Assert.Empty(first.Value.Snapshots.Select(snapshot => snapshot.SnapshotId)
                .Intersect(second.Value.Snapshots.Select(snapshot => snapshot.SnapshotId)));
        }

        [Fact]
        public void Inventory_diff_keyset_has_no_tie_duplicates_and_uses_current_snapshot_as_the_key()
        {
            var evidence = new TestEvidenceStore();
            evidence.InventorySnapshots.AddRange(new[]
            {
                Inventory(1, Utc(10)),
                Inventory(3, Utc(20), CatalogResolutionState.Resolved, Item("resourceWood", 3)),
                Inventory(2, Utc(20), CatalogResolutionState.Resolved, Item("resourceWood", 2)),
                Inventory(4, Utc(30), CatalogResolutionState.Resolved, Item("resourceWood", 4))
            });
            var useCase = new GetInventoryDiffsUseCase(
                evidence,
                new TestOperationQuery(),
                new PlayerInventoryDiffService());

            var first = useCase.Execute(
                new PlayerInventoryDiffsQuery(PlayerId, 2, null),
                PlayerEvidenceAccess.Owner,
                Array.Empty<string>());
            var second = useCase.Execute(
                new PlayerInventoryDiffsQuery(PlayerId, 2, first.Value!.NextCursor),
                PlayerEvidenceAccess.Owner,
                Array.Empty<string>());

            Assert.Equal(new long[] { 4, 3 }, first.Value.Diffs.Select(diff => diff.CurrentSnapshotId));
            Assert.Equal(new long[] { 2, 1 }, second.Value!.Diffs.Select(diff => diff.CurrentSnapshotId));
            Assert.Empty(first.Value.Diffs.Select(diff => diff.CurrentSnapshotId)
                .Intersect(second.Value.Diffs.Select(diff => diff.CurrentSnapshotId)));
            Assert.Null(second.Value.NextCursor);
        }

        [Fact]
        public void Diff_confirms_only_successful_operations_with_exact_snapshot_links()
        {
            var evidence = new TestEvidenceStore();
            evidence.InventorySnapshots.Add(Inventory(1, Utc(10)));
            evidence.InventorySnapshots.Add(Inventory(
                2,
                Utc(20),
                CatalogResolutionState.Resolved,
                Item("resourceWood", 1)));
            var operations = new TestOperationQuery();
            operations.Items["exact"] = Operation("exact", PlayerActionStatus.Succeeded, 1, 2);
            operations.Items["failed"] = Operation("failed", PlayerActionStatus.Failed, 1, 2);
            operations.Items["wrong-before"] = Operation("wrong-before", PlayerActionStatus.Succeeded, 9, 2);
            operations.Items["wrong-after"] = Operation("wrong-after", PlayerActionStatus.Succeeded, 1, 9);
            var useCase = new GetInventoryDiffsUseCase(
                evidence,
                operations,
                new PlayerInventoryDiffService());

            var result = useCase.Execute(
                new PlayerInventoryDiffsQuery(PlayerId, 10, null),
                PlayerEvidenceAccess.Owner,
                operations.Items.Keys);

            var diff = result.Value!.Diffs.Single(candidate => candidate.CurrentSnapshotId == 2);
            var change = Assert.Single(diff.Changes);
            Assert.Equal(EvidenceLevel.Confirmed, change.EvidenceLevel);
            Assert.Equal("exact", Assert.Single(change.SourceOperationIds));
        }

        [Fact]
        public void Unavailable_operation_source_never_promotes_an_observed_diff_to_confirmed()
        {
            var evidence = new TestEvidenceStore();
            evidence.InventorySnapshots.Add(Inventory(1, Utc(10)));
            evidence.InventorySnapshots.Add(Inventory(
                2,
                Utc(20),
                CatalogResolutionState.Resolved,
                Item("resourceWood", 1)));
            var operations = new TestOperationQuery { ThrowOnGet = true };
            var useCase = new GetInventoryDiffsUseCase(
                evidence,
                operations,
                new PlayerInventoryDiffService());

            var result = useCase.Execute(
                new PlayerInventoryDiffsQuery(PlayerId, 10, null),
                PlayerEvidenceAccess.Owner,
                new[] { "unavailable" });

            Assert.Equal(PlayerProfileSectionState.Partial, result.State);
            var diff = result.Value!.Diffs.Single(candidate => candidate.CurrentSnapshotId == 2);
            var change = Assert.Single(diff.Changes);
            Assert.Equal(EvidenceLevel.ObservedChange, change.EvidenceLevel);
            Assert.Empty(change.SourceOperationIds);
        }

        private static TestEvidenceStore PopulatedEvidenceStore()
        {
            var evidence = new TestEvidenceStore();
            evidence.Sessions.Add(Session(1, Utc(5), Utc(6), PlayerProfileSectionState.Available));
            evidence.Activity.Add(Activity(1, "PlayerJoined", Utc(5)));
            evidence.InventorySnapshots.Add(Inventory(1, Utc(10)));
            evidence.SkillSnapshots.Add(Skills(1, Utc(15)));
            return evidence;
        }

        private static HistoricalPlayerDetails Details() =>
            new HistoricalPlayerDetails(
                new HistoricalPlayerSummary(
                    PlayerId,
                    "Alice",
                    Utc(1),
                    Utc(40),
                    2,
                    2,
                    0,
                    false),
                new PlayerHistoryGapSummary(0, 0));

        private static PlayerEvidenceRangeQuery Range(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
            new PlayerEvidenceRangeQuery(PlayerId, fromUtc, toUtc, 5000);

        private static PlayerSession Session(
            long id,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc,
            PlayerProfileSectionState completeness) =>
            new PlayerSession(
                id,
                PlayerId,
                "local",
                "world-1",
                startedAtUtc,
                endedAtUtc,
                endedAtUtc.HasValue ? "Left" : null,
                new PlayerPosition(1, 2, 3),
                completeness);

        private static PlayerActivityEvent Activity(long id, string kind, DateTimeOffset observedAtUtc) =>
            new PlayerActivityEvent(
                id,
                PlayerId,
                "local",
                "world-1",
                kind,
                observedAtUtc,
                null,
                PlayerProfileSectionState.Available);

        private static PlayerInventorySnapshot Inventory(
            long id,
            DateTimeOffset observedAtUtc,
            CatalogResolutionState catalogResolution = CatalogResolutionState.Resolved,
            params InventoryItemScalar[] items) =>
            new PlayerInventorySnapshot(
                id,
                PlayerId,
                "local",
                "world-1",
                observedAtUtc,
                "v3.0.1-b4",
                catalogResolution == CatalogResolutionState.Resolved ? "catalog-1" : null,
                catalogResolution,
                "inventory-" + id,
                false,
                items);

        private static InventoryItemScalar Item(string internalName, int count) =>
            new InventoryItemScalar("Bag", 0, internalName, count, null, null, Array.Empty<string>());

        private static PlayerSkillSnapshot Skills(
            long id,
            DateTimeOffset observedAtUtc,
            params PlayerSkillValue[] values) =>
            new PlayerSkillSnapshot(
                id,
                PlayerId,
                "local",
                "world-1",
                observedAtUtc,
                "v3.0.1-b4",
                5,
                1,
                values);

        private static PlayerEvidenceGap Gap(long id, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc) =>
            new PlayerEvidenceGap(id, PlayerId, startedAtUtc, endedAtUtc, "QueueFull", 1);

        private static PlayerActionOperation Operation(
            string operationId,
            PlayerActionStatus status,
            long? beforeSnapshotId,
            long? afterSnapshotId) =>
            new PlayerActionOperation(
                operationId,
                PlayerActionOperationTypes.GrantItem,
                "owner",
                new PlayerTargetStamp(PlayerId, 17, Utc(1), "world-1"),
                status,
                Utc(1),
                Utc(1),
                Utc(2),
                status == PlayerActionStatus.Succeeded ? null : "failed",
                beforeSnapshotId,
                afterSnapshotId,
                null,
                null,
                "correlation-1");

        private static TimeZoneInfo PacificTimeZone()
        {
            var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0),
                3,
                2,
                DayOfWeek.Sunday);
            var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0),
                11,
                1,
                DayOfWeek.Sunday);
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2020, 1, 1),
                new DateTime(2030, 12, 31),
                TimeSpan.FromHours(1),
                start,
                end);
            return TimeZoneInfo.CreateCustomTimeZone(
                "PlayerEvidenceTestPacific",
                TimeSpan.FromHours(-8),
                "Player Evidence Test Pacific",
                "Player Evidence Test Standard",
                "Player Evidence Test Daylight",
                new[] { rule });
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 1, 0, 0, TimeSpan.Zero).AddMinutes(minute);

        private const string PlayerId = "EOS_1";

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class TestHistoryStore : IPlayerHistoryStore
        {
            public HistoricalPlayerDetails? Player { get; set; }
            public bool ThrowOnGet { get; set; }
            public int GetPlayerCalls { get; private set; }

            public HistoricalPlayerDetails? GetPlayer(string crossplatformId)
            {
                GetPlayerCalls++;
                if (ThrowOnGet) throw new InvalidOperationException("history unavailable");
                return string.Equals(crossplatformId, PlayerId, StringComparison.Ordinal) ? Player : null;
            }

            public void Append(PlayerSnapshot snapshot) => throw new NotSupportedException();
            public void AppendGap(PlayerHistoryGap gap) => throw new NotSupportedException();
            public HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query) => throw new NotSupportedException();
            public PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query) => throw new NotSupportedException();
            public PlayerTrackHistory? GetPlayerTrack(GetPlayerTrackQuery query) => throw new NotSupportedException();
            public IReadOnlyList<HistoricalPlayerLastRetainedLocation> GetHistoricalPlayerLastRetainedLocations(
                HistoricalPlayerLastLocationsStoreQuery query) => throw new NotSupportedException();
            public int Compact(DateTimeOffset utcNow, int maximumDeletes) => throw new NotSupportedException();
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class TestEvidenceStore : IPlayerEvidenceStore
        {
            public List<PlayerSession> Sessions { get; } = new List<PlayerSession>();
            public List<PlayerActivityEvent> Activity { get; } = new List<PlayerActivityEvent>();
            public List<PlayerInventorySnapshot> InventorySnapshots { get; } = new List<PlayerInventorySnapshot>();
            public List<PlayerSkillSnapshot> SkillSnapshots { get; } = new List<PlayerSkillSnapshot>();
            public List<PlayerEvidenceGap> InventoryGaps { get; } = new List<PlayerEvidenceGap>();
            public List<PlayerEvidenceGap> SkillGaps { get; } = new List<PlayerEvidenceGap>();
            public bool ThrowSessions { get; set; }
            public bool ThrowActivity { get; set; }
            public bool ThrowInventory { get; set; }
            public bool ThrowSkills { get; set; }
            public int ReadCalls { get; private set; }

            public IReadOnlyList<PlayerSession> GetSessions(PlayerEvidenceRangeQuery query)
            {
                ReadCalls++;
                if (ThrowSessions) throw new InvalidOperationException("sessions unavailable");
                return Sessions.Where(session =>
                    string.Equals(session.CrossplatformId, query.CrossplatformId, StringComparison.Ordinal) &&
                    session.StartedAtUtc <= query.ToUtc &&
                    (session.EndedAtUtc ?? query.ToUtc) >= query.FromUtc)
                    .Take(query.MaximumResults)
                    .ToArray();
            }

            public IReadOnlyList<PlayerActivityEvent> GetActivity(PlayerEvidenceRangeQuery query)
            {
                ReadCalls++;
                if (ThrowActivity) throw new InvalidOperationException("activity unavailable");
                return Activity.Where(activity =>
                    string.Equals(activity.CrossplatformId, query.CrossplatformId, StringComparison.Ordinal) &&
                    activity.ObservedAtUtc >= query.FromUtc &&
                    activity.ObservedAtUtc <= query.ToUtc)
                    .Take(query.MaximumResults)
                    .ToArray();
            }

            public PlayerInventorySnapshotsPage GetInventorySnapshots(PlayerInventorySnapshotsQuery query)
            {
                ReadCalls++;
                if (ThrowInventory) throw new InvalidOperationException("inventory unavailable");
                var page = Page(
                    InventorySnapshots.Where(snapshot =>
                        string.Equals(snapshot.CrossplatformId, query.CrossplatformId, StringComparison.Ordinal)),
                    query.PageSize,
                    query.Cursor,
                    snapshot => snapshot.ObservedAtUtc,
                    snapshot => snapshot.SnapshotId);
                return new PlayerInventorySnapshotsPage(
                    page.Items,
                    page.NextCursor,
                    InventoryGaps.Where(gap =>
                        string.Equals(gap.CrossplatformId, query.CrossplatformId, StringComparison.Ordinal)));
            }

            public PlayerSkillSnapshotsPage GetSkillSnapshots(PlayerSkillSnapshotsQuery query)
            {
                ReadCalls++;
                if (ThrowSkills) throw new InvalidOperationException("skills unavailable");
                var page = Page(
                    SkillSnapshots.Where(snapshot =>
                        string.Equals(snapshot.CrossplatformId, query.CrossplatformId, StringComparison.Ordinal)),
                    query.PageSize,
                    query.Cursor,
                    snapshot => snapshot.ObservedAtUtc,
                    snapshot => snapshot.SnapshotId);
                return new PlayerSkillSnapshotsPage(
                    page.Items,
                    page.NextCursor,
                    SkillGaps.Where(gap =>
                        string.Equals(gap.CrossplatformId, query.CrossplatformId, StringComparison.Ordinal)));
            }

            public IReadOnlyList<PlayerEvidenceGap> GetInventoryGaps(PlayerEvidenceRangeQuery query)
            {
                ReadCalls++;
                if (ThrowInventory) throw new InvalidOperationException("inventory gaps unavailable");
                return Gaps(InventoryGaps, query);
            }

            public IReadOnlyList<PlayerEvidenceGap> GetSkillGaps(PlayerEvidenceRangeQuery query)
            {
                ReadCalls++;
                if (ThrowSkills) throw new InvalidOperationException("skill gaps unavailable");
                return Gaps(SkillGaps, query);
            }

            private static IReadOnlyList<PlayerEvidenceGap> Gaps(
                IEnumerable<PlayerEvidenceGap> gaps,
                PlayerEvidenceRangeQuery query) =>
                gaps.Where(gap =>
                    string.Equals(gap.CrossplatformId, query.CrossplatformId, StringComparison.Ordinal) &&
                    gap.StartedAtUtc <= query.ToUtc &&
                    gap.EndedAtUtc >= query.FromUtc)
                    .ToArray();

            private static PageResult<T> Page<T>(
                IEnumerable<T> source,
                int pageSize,
                PlayerEvidenceCursor? cursor,
                Func<T, DateTimeOffset> observedAtUtc,
                Func<T, long> id)
            {
                var filtered = source.Where(item =>
                    cursor == null ||
                    observedAtUtc(item) < cursor.ObservedAtUtc ||
                    (observedAtUtc(item) == cursor.ObservedAtUtc && id(item) < cursor.Id));
                var candidates = filtered
                    .OrderByDescending(observedAtUtc)
                    .ThenByDescending(id)
                    .Take(pageSize + 1)
                    .ToArray();
                var items = candidates.Take(pageSize).ToArray();
                var nextCursor = candidates.Length > pageSize && items.Length > 0
                    ? new PlayerEvidenceCursor(observedAtUtc(items[items.Length - 1]), id(items[items.Length - 1]))
                    : null;
                return new PageResult<T>(items, nextCursor);
            }

            public void AppendSession(PlayerSession session) => throw new NotSupportedException();
            public void AppendActivity(PlayerActivityEvent activity) => throw new NotSupportedException();
            public void AppendInventorySnapshot(PlayerInventorySnapshot snapshot) => throw new NotSupportedException();
            public void AppendSkillSnapshot(PlayerSkillSnapshot snapshot) => throw new NotSupportedException();
            public void AppendInventoryGap(PlayerEvidenceGap gap) => throw new NotSupportedException();
            public void AppendSkillGap(PlayerEvidenceGap gap) => throw new NotSupportedException();
            public void Compact(PlayerEvidenceCompactionRequest request) => throw new NotSupportedException();
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class PageResult<T>
        {
            public PageResult(IReadOnlyList<T> items, PlayerEvidenceCursor? nextCursor)
            {
                Items = items;
                NextCursor = nextCursor;
            }

            public IReadOnlyList<T> Items { get; }
            public PlayerEvidenceCursor? NextCursor { get; }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class TestOperationQuery : IPlayerActionOperationQuery
        {
            public Dictionary<string, PlayerActionOperation> Items { get; } =
                new Dictionary<string, PlayerActionOperation>(StringComparer.Ordinal);
            public bool ThrowOnGet { get; set; }
            public int GetCalls { get; private set; }

            public PlayerActionOperation? Get(string operationId)
            {
                GetCalls++;
                if (ThrowOnGet) throw new InvalidOperationException("operations unavailable");
                return Items.TryGetValue(operationId, out var operation) ? operation : null;
            }
        }
    }
}
