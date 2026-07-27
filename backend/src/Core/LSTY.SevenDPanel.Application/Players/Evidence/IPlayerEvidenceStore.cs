using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application
{
    public interface IPlayerEvidenceStore
    {
        void AppendSession(PlayerSession session);

        void AppendActivity(PlayerActivityEvent activity);

        void AppendInventorySnapshot(PlayerInventorySnapshot snapshot);

        void AppendSkillSnapshot(PlayerSkillSnapshot snapshot);

        void AppendInventoryGap(PlayerEvidenceGap gap);

        void AppendSkillGap(PlayerEvidenceGap gap);

        IReadOnlyList<PlayerSession> GetSessions(PlayerEvidenceRangeQuery query);

        IReadOnlyList<PlayerActivityEvent> GetActivity(PlayerEvidenceRangeQuery query);

        PlayerInventorySnapshotsPage GetInventorySnapshots(PlayerInventorySnapshotsQuery query);

        PlayerSkillSnapshotsPage GetSkillSnapshots(PlayerSkillSnapshotsQuery query);

        IReadOnlyList<PlayerEvidenceGap> GetInventoryGaps(PlayerEvidenceRangeQuery query);

        IReadOnlyList<PlayerEvidenceGap> GetSkillGaps(PlayerEvidenceRangeQuery query);

        void Compact(PlayerEvidenceCompactionRequest request);
    }
}
