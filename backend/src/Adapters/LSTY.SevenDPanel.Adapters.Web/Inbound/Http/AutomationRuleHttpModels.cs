using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public abstract class AutomationStrictHttpModel
    {
        [JsonExtensionData(ReadData = true, WriteData = false)]
        private readonly IDictionary<string, JToken> unknownProperties =
            new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

        internal bool HasUnknownProperties => unknownProperties.Count != 0;
    }

    public sealed class AutomationRuleHttpRequest : AutomationStrictHttpModel
    {
        public string? Id { get; set; }
        public long? ExpectedVersion { get; set; }
        public string? Name { get; set; }
        public bool? IsEnabled { get; set; }
        public AutomationTriggerHttpModel? Trigger { get; set; }
        public AutomationConditionHttpModel? Condition { get; set; }
        public AutomationActionHttpModel[]? Actions { get; set; }
        public long? CooldownSeconds { get; set; }
        public string? CooldownScope { get; set; }
        public string? ConcurrencyPolicy { get; set; }
        public string? FailurePolicy { get; set; }
    }

    public sealed class AutomationTriggerHttpModel : AutomationStrictHttpModel
    {
        public string? Type { get; set; }
    }

    public sealed class AutomationConditionHttpModel : AutomationStrictHttpModel
    {
        public string? NodeId { get; set; }
        public string? Kind { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationConditionPredicateHttpModel? Predicate { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationConditionHttpModel[]? Children { get; set; }
    }

    public sealed class AutomationConditionPredicateHttpModel : AutomationStrictHttpModel
    {
        public string? FieldKey { get; set; }
        public string? Operator { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? ScalarValue { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long? MinimumInclusive { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long? MaximumInclusive { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string[]? SetValues { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationTimeWindowHttpModel? Window { get; set; }
    }

    public sealed class AutomationTimeWindowHttpModel : AutomationStrictHttpModel
    {
        public string? TimeZoneId { get; set; }
        public AutomationTimeOfDayHttpModel? StartInclusive { get; set; }
        public AutomationTimeOfDayHttpModel? EndInclusive { get; set; }
    }

    public sealed class AutomationTimeOfDayHttpModel : AutomationStrictHttpModel
    {
        public int? Hour { get; set; }
        public int? Minute { get; set; }
    }

    public sealed class AutomationTargetHttpModel : AutomationStrictHttpModel
    {
        public string? Kind { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? ReferenceId { get; set; }
    }

    public sealed class AutomationActionHttpModel : AutomationStrictHttpModel
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public AutomationTargetHttpModel? Target { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationMessageActionHttpModel? BroadcastMessage { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationMessageActionHttpModel? PrivateMessage { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationMessageActionHttpModel? Announcement { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationGrantItemActionHttpModel? GrantItem { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationGrantRewardPackageActionHttpModel? GrantRewardPackage { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationAmountActionHttpModel? AdjustEconomy { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationReasonActionHttpModel? KickPlayer { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationMutePlayerActionHttpModel? MutePlayer { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationRestrictedCommandActionHttpModel? RestrictedCommand { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationMessageActionHttpModel? DiscordMessage { get; set; }
    }

    public sealed class AutomationMessageActionHttpModel : AutomationStrictHttpModel
    {
        public string? Message { get; set; }
    }

    public sealed class AutomationGrantItemActionHttpModel : AutomationStrictHttpModel
    {
        public string? ResourceId { get; set; }
        public long? Amount { get; set; }
    }

    public sealed class AutomationGrantRewardPackageActionHttpModel : AutomationStrictHttpModel
    {
        public string? RewardPackageId { get; set; }
    }

    public sealed class AutomationAmountActionHttpModel : AutomationStrictHttpModel
    {
        public long? Amount { get; set; }
    }

    public sealed class AutomationReasonActionHttpModel : AutomationStrictHttpModel
    {
        public string? Reason { get; set; }
    }

    public sealed class AutomationMutePlayerActionHttpModel : AutomationStrictHttpModel
    {
        public long? DurationSeconds { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class AutomationRestrictedCommandActionHttpModel : AutomationStrictHttpModel
    {
        public string? CommandCatalogKey { get; set; }
    }
}
