using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Commerce;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class OnlineRewardRuleUpsertHttpRequest
    {
        public string? Name { get; set; }
        public long RequiredOnlineSeconds { get; set; }
        public long? RepeatIntervalSeconds { get; set; }
        public EvidenceGapPolicy GapPolicy { get; set; }
        public string? RewardPackageId { get; set; }
        public bool Enabled { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class OnlineRewardRuleHttpResponse
    {
        public OnlineRewardRuleHttpResponse(OnlineRewardRuleSnapshot rule)
        {
            RuleId = rule.RuleId;
            Name = rule.Name;
            RequiredOnlineSeconds = checked((long)rule.RequiredOnline.TotalSeconds);
            RepeatIntervalSeconds = rule.RepeatInterval.HasValue
                ? checked((long)rule.RepeatInterval.Value.TotalSeconds)
                : null;
            GapPolicy = rule.GapPolicy;
            RewardPackageId = rule.RewardPackageId;
            Enabled = rule.Enabled;
            SortOrder = rule.SortOrder;
            CreatedAtUtc = rule.CreatedAtUtc;
            UpdatedAtUtc = rule.UpdatedAtUtc;
            RowVersion = rule.RowVersion;
        }

        public string RuleId { get; }
        public string Name { get; }
        public long RequiredOnlineSeconds { get; }
        public long? RepeatIntervalSeconds { get; }
        public EvidenceGapPolicy GapPolicy { get; }
        public string RewardPackageId { get; }
        public bool Enabled { get; }
        public int SortOrder { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class ManualOnlineRewardGrantHttpRequest
    {
        public string? RuleId { get; set; }
        public string? CrossplatformId { get; set; }
        public int ExpectedEntityId { get; set; }
        public string? ExpectedWorldId { get; set; }
        public string? ClientRequestKey { get; set; }
    }

    public sealed class OnlineRewardRecordHttpResponse
    {
        public OnlineRewardRecordHttpResponse(RewardEligibilitySnapshot record)
        {
            EligibilityId = record.EligibilityId;
            RuleKind = record.RuleKind;
            RuleId = record.RuleId;
            RewardPackageId = record.RewardPackageId;
            CrossplatformId = record.CrossplatformId;
            EligibilityKey = record.EligibilityKey;
            State = record.State;
            GrantOperationId = record.GrantOperationId;
            CorrelationId = record.CorrelationId;
            EvidenceFromUtc = record.EvidenceFromUtc;
            EvidenceToUtc = record.EvidenceToUtc;
            CreatedAtUtc = record.CreatedAtUtc;
            UpdatedAtUtc = record.UpdatedAtUtc;
            RowVersion = record.RowVersion;
        }

        public string EligibilityId { get; }
        public string RuleKind { get; }
        public string RuleId { get; }
        public string RewardPackageId { get; }
        public string CrossplatformId { get; }
        public string EligibilityKey { get; }
        public RewardEligibilityState State { get; }
        public string? GrantOperationId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset? EvidenceFromUtc { get; }
        public DateTimeOffset? EvidenceToUtc { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class OnlineRewardRecordsHttpResponse
    {
        public OnlineRewardRecordsHttpResponse(IEnumerable<RewardEligibilitySnapshot> records) =>
            Records = records.Select(record => new OnlineRewardRecordHttpResponse(record)).ToArray();

        public IReadOnlyList<OnlineRewardRecordHttpResponse> Records { get; }
    }
}
