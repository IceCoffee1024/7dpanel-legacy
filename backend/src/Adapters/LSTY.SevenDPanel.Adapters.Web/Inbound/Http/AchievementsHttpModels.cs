using System;
using LSTY.SevenDPanel.Application.Commerce;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class AchievementDefinitionUpsertHttpRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public AchievementStatistic Statistic { get; set; }
        public long ThresholdValue { get; set; }
        public string? RewardPackageId { get; set; }
        public bool Enabled { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class AchievementDefinitionHttpResponse
    {
        public AchievementDefinitionHttpResponse(AchievementDefinitionSnapshot definition)
        {
            AchievementId = definition.AchievementId;
            Name = definition.Name;
            Description = definition.Description;
            Statistic = definition.Statistic;
            ThresholdValue = definition.ThresholdValue;
            RewardPackageId = definition.RewardPackageId;
            Enabled = definition.Enabled;
            SortOrder = definition.SortOrder;
            CreatedAtUtc = definition.CreatedAtUtc;
            UpdatedAtUtc = definition.UpdatedAtUtc;
            RowVersion = definition.RowVersion;
        }

        public string AchievementId { get; }
        public string Name { get; }
        public string Description { get; }
        public AchievementStatistic Statistic { get; }
        public long ThresholdValue { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public int SortOrder { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class AchievementRecordHttpResponse
    {
        public AchievementRecordHttpResponse(AchievementProgressSnapshot record)
        {
            AchievementId = record.AchievementId;
            CrossplatformId = record.CrossplatformId;
            CurrentValue = record.CurrentValue;
            EligibilityKey = record.EligibilityKey;
            GrantOperationId = record.GrantOperationId;
            CompletedAtUtc = record.CompletedAtUtc;
            UpdatedAtUtc = record.UpdatedAtUtc;
            RowVersion = record.RowVersion;
        }

        public string AchievementId { get; }
        public string CrossplatformId { get; }
        public long CurrentValue { get; }
        public string? EligibilityKey { get; }
        public string? GrantOperationId { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }
}
