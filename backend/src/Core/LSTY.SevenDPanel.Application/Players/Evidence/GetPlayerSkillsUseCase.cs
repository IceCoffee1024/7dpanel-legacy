using System;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetPlayerSkillsUseCase
    {
        private readonly IPlayerEvidenceStore store;

        public GetPlayerSkillsUseCase(IPlayerEvidenceStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public PlayerProfileSection<PlayerSkillSnapshotsPage> Execute(
            PlayerSkillSnapshotsQuery query,
            PlayerEvidenceAccess access)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            PlayerEvidenceUseCaseSupport.RequireAccess(access);
            if (access != PlayerEvidenceAccess.Owner)
                return PlayerEvidenceUseCaseSupport.Forbidden<PlayerSkillSnapshotsPage>();

            try
            {
                var page = store.GetSkillSnapshots(query) ??
                    throw new InvalidOperationException("The skill source returned no page.");
                var observedAtUtc = page.Snapshots.Count == 0
                    ? (DateTimeOffset?)null
                    : page.Snapshots.Max(snapshot => snapshot.ObservedAtUtc);
                var state = page.Gaps.Count > 0 || page.Snapshots.Any(IsPartial)
                    ? PlayerProfileSectionState.Partial
                    : PlayerProfileSectionState.Available;
                return new PlayerProfileSection<PlayerSkillSnapshotsPage>(
                    state,
                    observedAtUtc,
                    page,
                    page.Gaps);
            }
            catch (Exception)
            {
                return PlayerEvidenceUseCaseSupport.Unavailable<PlayerSkillSnapshotsPage>();
            }
        }

        internal static bool IsPartial(PlayerSkillSnapshot snapshot) =>
            snapshot.Level == null ||
            snapshot.SkillPoints == null ||
            snapshot.Values.Any(value => value.State != SkillValueState.Known);
    }
}
