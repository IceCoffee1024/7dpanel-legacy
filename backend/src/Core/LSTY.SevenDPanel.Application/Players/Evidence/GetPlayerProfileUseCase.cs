using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public enum PlayerEvidenceAccess
    {
        Standard,
        Owner
    }

    public sealed class GetPlayerProfileUseCase
    {
        private readonly IPlayerHistoryStore historyStore;
        private readonly IPlayerEvidenceStore evidenceStore;
        private readonly TimeZoneInfo timeZone;

        public GetPlayerProfileUseCase(
            IPlayerHistoryStore historyStore,
            IPlayerEvidenceStore evidenceStore,
            TimeZoneInfo timeZone)
        {
            this.historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
            this.evidenceStore = evidenceStore ?? throw new ArgumentNullException(nameof(evidenceStore));
            this.timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        }

        public PlayerProfile Execute(
            PlayerEvidenceRangeQuery query,
            PlayerEvidenceAccess access)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            PlayerEvidenceUseCaseSupport.RequireAccess(access);
            if (access != PlayerEvidenceAccess.Owner)
                return Forbidden(query.CrossplatformId);

            var summary = ReadSummary(query.CrossplatformId);
            var sessions = ReadSessions(query);
            var activity = ReadActivity(query);
            var inventory = ReadInventory(query);
            var skills = ReadSkills(query);
            var daily = BuildDaily(sessions, activity, inventory);

            return new PlayerProfile(
                query.CrossplatformId,
                summary,
                sessions.Section,
                activity.Section,
                inventory.Section,
                skills,
                daily);
        }

        private PlayerProfileSection<HistoricalPlayerSummary> ReadSummary(string crossplatformId)
        {
            try
            {
                var details = historyStore.GetPlayer(crossplatformId);
                if (details != null && !string.Equals(
                        details.Player.CrossplatformId,
                        crossplatformId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The history source returned another player identity.");
                }

                return new PlayerProfileSection<HistoricalPlayerSummary>(
                    details?.Player.HasGaps == true
                        ? PlayerProfileSectionState.Partial
                        : PlayerProfileSectionState.Available,
                    details?.Player.LastObservedAtUtc,
                    details?.Player,
                    Array.Empty<PlayerEvidenceGap>());
            }
            catch (Exception)
            {
                return PlayerEvidenceUseCaseSupport.Unavailable<HistoricalPlayerSummary>();
            }
        }

        private SessionRead ReadSessions(PlayerEvidenceRangeQuery query)
        {
            try
            {
                var values = (evidenceStore.GetSessions(query) ??
                              throw new InvalidOperationException("The session source returned no result."))
                    .Where(session => string.Equals(
                        session.CrossplatformId,
                        query.CrossplatformId,
                        StringComparison.Ordinal))
                    .OrderByDescending(session => session.StartedAtUtc)
                    .ThenByDescending(session => session.SessionId)
                    .ToArray();
                var partial = values.Any(session =>
                    session.EndedAtUtc == null ||
                    session.Completeness != PlayerProfileSectionState.Available);
                var observedAtUtc = values
                    .Select(session => session.EndedAtUtc ?? session.StartedAtUtc)
                    .Cast<DateTimeOffset?>()
                    .DefaultIfEmpty(null)
                    .Max();
                return new SessionRead(
                    true,
                    values,
                    new PlayerProfileSection<IReadOnlyList<PlayerSession>>(
                        partial ? PlayerProfileSectionState.Partial : PlayerProfileSectionState.Available,
                        observedAtUtc,
                        values,
                        Array.Empty<PlayerEvidenceGap>()));
            }
            catch (Exception)
            {
                return new SessionRead(
                    false,
                    Array.Empty<PlayerSession>(),
                    PlayerEvidenceUseCaseSupport.Unavailable<IReadOnlyList<PlayerSession>>());
            }
        }

        private ActivityRead ReadActivity(PlayerEvidenceRangeQuery query)
        {
            try
            {
                var values = (evidenceStore.GetActivity(query) ??
                              throw new InvalidOperationException("The activity source returned no result."))
                    .Where(activity => string.Equals(
                        activity.CrossplatformId,
                        query.CrossplatformId,
                        StringComparison.Ordinal))
                    .OrderByDescending(activity => activity.ObservedAtUtc)
                    .ThenByDescending(activity => activity.ActivityId)
                    .ToArray();
                var state = values.Any(activity =>
                    activity.Completeness != PlayerProfileSectionState.Available)
                    ? PlayerProfileSectionState.Partial
                    : PlayerProfileSectionState.Available;
                var observedAtUtc = values
                    .Select(activity => (DateTimeOffset?)activity.ObservedAtUtc)
                    .DefaultIfEmpty(null)
                    .Max();
                return new ActivityRead(
                    true,
                    values,
                    new PlayerProfileSection<IReadOnlyList<PlayerActivityEvent>>(
                        state,
                        observedAtUtc,
                        values,
                        Array.Empty<PlayerEvidenceGap>()));
            }
            catch (Exception)
            {
                return new ActivityRead(
                    false,
                    Array.Empty<PlayerActivityEvent>(),
                    PlayerEvidenceUseCaseSupport.Unavailable<IReadOnlyList<PlayerActivityEvent>>());
            }
        }

        private InventoryRead ReadInventory(PlayerEvidenceRangeQuery query)
        {
            try
            {
                var loaded = LoadInventorySnapshots(query);
                var gaps = (evidenceStore.GetInventoryGaps(query) ??
                            throw new InvalidOperationException("The inventory gap source returned no result."))
                    .Where(gap => string.Equals(
                        gap.CrossplatformId,
                        query.CrossplatformId,
                        StringComparison.Ordinal))
                    .OrderBy(gap => gap.StartedAtUtc)
                    .ThenBy(gap => gap.GapId)
                    .ToArray();
                var latest = loaded.AllSnapshots.FirstOrDefault();
                var state = gaps.Length > 0 ||
                            loaded.Truncated ||
                            latest?.CatalogResolution == CatalogResolutionState.Unavailable
                    ? PlayerProfileSectionState.Partial
                    : PlayerProfileSectionState.Available;
                var section = new PlayerProfileSection<PlayerInventorySnapshot>(
                    state,
                    latest?.ObservedAtUtc,
                    latest,
                    gaps);
                return new InventoryRead(
                    true,
                    !loaded.Truncated,
                    loaded.RangeSnapshots,
                    gaps,
                    section);
            }
            catch (Exception)
            {
                return new InventoryRead(
                    false,
                    false,
                    Array.Empty<PlayerInventorySnapshot>(),
                    Array.Empty<PlayerEvidenceGap>(),
                    PlayerEvidenceUseCaseSupport.Unavailable<PlayerInventorySnapshot>());
            }
        }

        private PlayerProfileSection<PlayerSkillSnapshot> ReadSkills(PlayerEvidenceRangeQuery query)
        {
            try
            {
                var page = evidenceStore.GetSkillSnapshots(
                               new PlayerSkillSnapshotsQuery(query.CrossplatformId, 1, null)) ??
                           throw new InvalidOperationException("The skill source returned no page.");
                var gaps = (evidenceStore.GetSkillGaps(query) ??
                            throw new InvalidOperationException("The skill gap source returned no result."))
                    .Where(gap => string.Equals(
                        gap.CrossplatformId,
                        query.CrossplatformId,
                        StringComparison.Ordinal))
                    .OrderBy(gap => gap.StartedAtUtc)
                    .ThenBy(gap => gap.GapId)
                    .ToArray();
                var latest = page.Snapshots
                    .Where(snapshot => string.Equals(
                        snapshot.CrossplatformId,
                        query.CrossplatformId,
                        StringComparison.Ordinal))
                    .OrderByDescending(snapshot => snapshot.ObservedAtUtc)
                    .ThenByDescending(snapshot => snapshot.SnapshotId)
                    .FirstOrDefault();
                var state = gaps.Length > 0 ||
                            (latest != null && GetPlayerSkillsUseCase.IsPartial(latest))
                    ? PlayerProfileSectionState.Partial
                    : PlayerProfileSectionState.Available;
                return new PlayerProfileSection<PlayerSkillSnapshot>(
                    state,
                    latest?.ObservedAtUtc,
                    latest,
                    gaps);
            }
            catch (Exception)
            {
                return PlayerEvidenceUseCaseSupport.Unavailable<PlayerSkillSnapshot>();
            }
        }

        private InventorySnapshotLoad LoadInventorySnapshots(PlayerEvidenceRangeQuery range)
        {
            var all = new List<PlayerInventorySnapshot>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            PlayerEvidenceCursor? cursor = null;
            var remaining = range.MaximumResults;
            var truncated = false;

            while (remaining > 0)
            {
                var pageSize = Math.Min(PlayerInventorySnapshotsQuery.MaximumPageSize, remaining);
                var page = evidenceStore.GetInventorySnapshots(
                               new PlayerInventorySnapshotsQuery(range.CrossplatformId, pageSize, cursor)) ??
                           throw new InvalidOperationException("The inventory source returned no page.");
                var pageSnapshots = page.Snapshots
                    .Where(snapshot =>
                        string.Equals(
                            snapshot.CrossplatformId,
                            range.CrossplatformId,
                            StringComparison.Ordinal) &&
                        IsAfterCursor(snapshot, cursor))
                    .OrderByDescending(snapshot => snapshot.ObservedAtUtc)
                    .ThenByDescending(snapshot => snapshot.SnapshotId)
                    .ToArray();
                foreach (var snapshot in pageSnapshots)
                {
                    var key = snapshot.ObservedAtUtc.UtcTicks + ":" + snapshot.SnapshotId;
                    if (keys.Add(key)) all.Add(snapshot);
                }
                remaining -= pageSnapshots.Length;

                if (page.NextCursor == null || pageSnapshots.Length == 0)
                    break;
                if (pageSnapshots[pageSnapshots.Length - 1].ObservedAtUtc < range.FromUtc)
                    break;
                if (remaining == 0)
                {
                    truncated = true;
                    break;
                }
                if (cursor != null && page.NextCursor.CompareTo(cursor) == 0)
                {
                    truncated = true;
                    break;
                }
                cursor = page.NextCursor;
            }

            var ordered = all
                .OrderByDescending(snapshot => snapshot.ObservedAtUtc)
                .ThenByDescending(snapshot => snapshot.SnapshotId)
                .ToArray();
            return new InventorySnapshotLoad(
                ordered,
                ordered.Where(snapshot =>
                    snapshot.ObservedAtUtc >= range.FromUtc &&
                    snapshot.ObservedAtUtc <= range.ToUtc).ToArray(),
                truncated);
        }

        private PlayerProfileSection<IReadOnlyList<PlayerDailyActivitySummary>> BuildDaily(
            SessionRead sessions,
            ActivityRead activity,
            InventoryRead inventory)
        {
            var dates = new SortedSet<DateTime>();
            foreach (var session in sessions.Values)
                dates.Add(LocalDate(session.StartedAtUtc));
            foreach (var item in activity.Values)
                dates.Add(LocalDate(item.ObservedAtUtc));
            foreach (var snapshot in inventory.RangeSnapshots)
                dates.Add(LocalDate(snapshot.ObservedAtUtc));
            foreach (var gap in inventory.Gaps)
            {
                var date = LocalDate(gap.StartedAtUtc);
                var end = LocalDate(gap.EndedAtUtc);
                while (date <= end)
                {
                    dates.Add(date);
                    date = date.AddDays(1);
                }
            }

            var summaries = new List<PlayerDailyActivitySummary>();
            var partial = !sessions.Available ||
                          !activity.Available ||
                          !inventory.Available ||
                          !inventory.RangeComplete;

            foreach (var date in dates)
            {
                var sessionsOnDate = sessions.Values.Count(session => LocalDate(session.StartedAtUtc) == date);
                var activityOnDate = activity.Values.Where(item => LocalDate(item.ObservedAtUtc) == date).ToArray();
                var inventoryGap = inventory.Gaps.Any(gap =>
                    LocalDate(gap.StartedAtUtc) <= date &&
                    LocalDate(gap.EndedAtUtc) >= date);
                var inventoryCount = inventory.Available && inventory.RangeComplete && !inventoryGap
                    ? (int?)inventory.RangeSnapshots.Count(snapshot => LocalDate(snapshot.ObservedAtUtc) == date)
                    : null;
                var chatCount = KnownCount(activity.Available, activityOnDate, "ChatMessage");
                var deathCount = KnownCount(activity.Available, activityOnDate, "PlayerDied");
                var killCount = KnownCount(activity.Available, activityOnDate, "PlayerKilledEntity");
                var summary = new PlayerDailyActivitySummary(
                    date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    sessions.Available ? (int?)sessionsOnDate : null,
                    activity.Available ? (int?)activityOnDate.Count(item => IsKind(item, "PlayerJoined")) : null,
                    chatCount,
                    deathCount,
                    killCount,
                    inventoryCount);
                summaries.Add(summary);
                partial |= inventoryGap ||
                           summary.SessionCount == null ||
                           summary.LoginCount == null ||
                           summary.ChatMessageCount == null ||
                           summary.DeathCount == null ||
                           summary.KillCount == null ||
                           summary.InventoryObservationCount == null;
            }

            partial |= sessions.Values.Any(session =>
                session.EndedAtUtc == null ||
                session.Completeness != PlayerProfileSectionState.Available);
            partial |= activity.Values.Any(item =>
                item.Completeness != PlayerProfileSectionState.Available);

            var observedTimes = sessions.Values
                .Select(session => session.EndedAtUtc ?? session.StartedAtUtc)
                .Concat(activity.Values.Select(item => item.ObservedAtUtc))
                .Concat(inventory.RangeSnapshots.Select(snapshot => snapshot.ObservedAtUtc))
                .Concat(inventory.Gaps.Select(gap => gap.EndedAtUtc))
                .ToArray();
            var observedAtUtc = observedTimes.Length == 0
                ? (DateTimeOffset?)null
                : observedTimes.Max();
            return new PlayerProfileSection<IReadOnlyList<PlayerDailyActivitySummary>>(
                partial ? PlayerProfileSectionState.Partial : PlayerProfileSectionState.Available,
                observedAtUtc,
                summaries,
                inventory.Gaps);
        }

        private DateTime LocalDate(DateTimeOffset observedAtUtc) =>
            TimeZoneInfo.ConvertTime(observedAtUtc, timeZone).Date;

        private static bool IsKind(PlayerActivityEvent item, string kind) =>
            string.Equals(item.Kind, kind, StringComparison.Ordinal);

        private static int? KnownCount(
            bool sourceAvailable,
            IReadOnlyList<PlayerActivityEvent> activity,
            string kind)
        {
            if (!sourceAvailable) return null;
            var count = activity.Count(item => IsKind(item, kind));
            return count == 0 ? (int?)null : count;
        }

        private static bool IsAfterCursor(
            PlayerInventorySnapshot snapshot,
            PlayerEvidenceCursor? cursor) =>
            cursor == null ||
            snapshot.ObservedAtUtc < cursor.ObservedAtUtc ||
            (snapshot.ObservedAtUtc == cursor.ObservedAtUtc && snapshot.SnapshotId < cursor.Id);

        private static PlayerProfile Forbidden(string crossplatformId) =>
            new PlayerProfile(
                crossplatformId,
                PlayerEvidenceUseCaseSupport.Forbidden<HistoricalPlayerSummary>(),
                PlayerEvidenceUseCaseSupport.Forbidden<IReadOnlyList<PlayerSession>>(),
                PlayerEvidenceUseCaseSupport.Forbidden<IReadOnlyList<PlayerActivityEvent>>(),
                PlayerEvidenceUseCaseSupport.Forbidden<PlayerInventorySnapshot>(),
                PlayerEvidenceUseCaseSupport.Forbidden<PlayerSkillSnapshot>(),
                PlayerEvidenceUseCaseSupport.Forbidden<IReadOnlyList<PlayerDailyActivitySummary>>());

        private sealed class SessionRead
        {
            public SessionRead(
                bool available,
                IReadOnlyList<PlayerSession> values,
                PlayerProfileSection<IReadOnlyList<PlayerSession>> section)
            {
                Available = available;
                Values = values;
                Section = section;
            }

            public bool Available { get; }
            public IReadOnlyList<PlayerSession> Values { get; }
            public PlayerProfileSection<IReadOnlyList<PlayerSession>> Section { get; }
        }

        private sealed class ActivityRead
        {
            public ActivityRead(
                bool available,
                IReadOnlyList<PlayerActivityEvent> values,
                PlayerProfileSection<IReadOnlyList<PlayerActivityEvent>> section)
            {
                Available = available;
                Values = values;
                Section = section;
            }

            public bool Available { get; }
            public IReadOnlyList<PlayerActivityEvent> Values { get; }
            public PlayerProfileSection<IReadOnlyList<PlayerActivityEvent>> Section { get; }
        }

        private sealed class InventoryRead
        {
            public InventoryRead(
                bool available,
                bool rangeComplete,
                IReadOnlyList<PlayerInventorySnapshot> rangeSnapshots,
                IReadOnlyList<PlayerEvidenceGap> gaps,
                PlayerProfileSection<PlayerInventorySnapshot> section)
            {
                Available = available;
                RangeComplete = rangeComplete;
                RangeSnapshots = rangeSnapshots;
                Gaps = gaps;
                Section = section;
            }

            public bool Available { get; }
            public bool RangeComplete { get; }
            public IReadOnlyList<PlayerInventorySnapshot> RangeSnapshots { get; }
            public IReadOnlyList<PlayerEvidenceGap> Gaps { get; }
            public PlayerProfileSection<PlayerInventorySnapshot> Section { get; }
        }

        private sealed class InventorySnapshotLoad
        {
            public InventorySnapshotLoad(
                IReadOnlyList<PlayerInventorySnapshot> allSnapshots,
                IReadOnlyList<PlayerInventorySnapshot> rangeSnapshots,
                bool truncated)
            {
                AllSnapshots = allSnapshots;
                RangeSnapshots = rangeSnapshots;
                Truncated = truncated;
            }

            public IReadOnlyList<PlayerInventorySnapshot> AllSnapshots { get; }
            public IReadOnlyList<PlayerInventorySnapshot> RangeSnapshots { get; }
            public bool Truncated { get; }
        }
    }

    internal static class PlayerEvidenceUseCaseSupport
    {
        public static void RequireAccess(PlayerEvidenceAccess access)
        {
            if (!Enum.IsDefined(typeof(PlayerEvidenceAccess), access))
                throw new ArgumentOutOfRangeException(nameof(access));
        }

        public static PlayerProfileSection<T> Forbidden<T>() =>
            new PlayerProfileSection<T>(
                PlayerProfileSectionState.Forbidden,
                null,
                default,
                Array.Empty<PlayerEvidenceGap>());

        public static PlayerProfileSection<T> Unavailable<T>() =>
            new PlayerProfileSection<T>(
                PlayerProfileSectionState.Unavailable,
                null,
                default,
                Array.Empty<PlayerEvidenceGap>());
    }
}
