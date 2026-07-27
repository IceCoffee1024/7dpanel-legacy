using System;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetInventorySnapshotsUseCase
    {
        private readonly IPlayerEvidenceStore store;

        public GetInventorySnapshotsUseCase(IPlayerEvidenceStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public PlayerProfileSection<PlayerInventorySnapshotsPage> Execute(
            PlayerInventorySnapshotsQuery query,
            PlayerEvidenceAccess access)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            PlayerEvidenceUseCaseSupport.RequireAccess(access);
            if (access != PlayerEvidenceAccess.Owner)
                return PlayerEvidenceUseCaseSupport.Forbidden<PlayerInventorySnapshotsPage>();

            try
            {
                var page = store.GetInventorySnapshots(query) ??
                    throw new InvalidOperationException("The inventory source returned no page.");
                var observedAtUtc = page.Snapshots.Count == 0
                    ? (DateTimeOffset?)null
                    : page.Snapshots.Max(snapshot => snapshot.ObservedAtUtc);
                var state = page.Gaps.Count > 0 ||
                            page.Snapshots.Any(snapshot =>
                                snapshot.CatalogResolution != CatalogResolutionState.Resolved)
                    ? PlayerProfileSectionState.Partial
                    : PlayerProfileSectionState.Available;
                return new PlayerProfileSection<PlayerInventorySnapshotsPage>(
                    state,
                    observedAtUtc,
                    page,
                    page.Gaps);
            }
            catch (Exception)
            {
                return PlayerEvidenceUseCaseSupport.Unavailable<PlayerInventorySnapshotsPage>();
            }
        }
    }
}
