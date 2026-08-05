export type AutomationTriggerType = 'PlayerJoined' | 'PlayerLeft' | 'ChatMessage' | 'Cron' | 'BloodMoonPhaseEntered'
export type AutomationConditionKind = 'All' | 'Any' | 'Not' | 'Predicate'
export type AutomationConditionOperator = 'Equals' | 'NotEquals' | 'InSet' | 'NumberRange' | 'TimeWindow' | 'PlayerGroup' | 'Permission' | 'Cooldown'
export type AutomationTruth = 'Matched' | 'NotMatched' | 'Unknown'
export type AutomationActionType = 'BroadcastMessage' | 'PrivateMessage' | 'Announcement' | 'GrantItem' | 'GrantRewardPackage' | 'AdjustEconomy' | 'KickPlayer' | 'MutePlayer' | 'RestrictedCommand' | 'DiscordMessage'
export type AutomationTargetKind = 'Global' | 'TriggerPlayer' | 'StablePlayer' | 'DiscordTarget'
export type AutomationCooldownScope = 'Rule' | 'RulePlayer'
export type AutomationConcurrencyPolicy = 'SkipIfRunning' | 'QueueOne'
export type AutomationFailurePolicy = 'StopOnFailure' | 'Continue'
export type AutomationExecutionStatus = 'Pending' | 'Running' | 'Queued' | 'Skipped' | 'Succeeded' | 'Failed' | 'ResultUnknown'
export type AutomationActionResultStatus = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'ResultUnknown'

export interface AutomationTrigger { readonly type: AutomationTriggerType }
export interface AutomationTimeOfDay { readonly hour: number, readonly minute: number }
export interface AutomationTimeWindow { readonly timeZoneId: string, readonly startInclusive: AutomationTimeOfDay, readonly endInclusive: AutomationTimeOfDay }
export interface AutomationPredicate {
  readonly fieldKey: string
  readonly operator: AutomationConditionOperator
  readonly scalarValue?: string
  readonly minimumInclusive?: number
  readonly maximumInclusive?: number
  readonly setValues?: readonly string[]
  readonly window?: AutomationTimeWindow
}
export interface AutomationCondition {
  readonly nodeId: string
  readonly kind: AutomationConditionKind
  readonly predicate?: AutomationPredicate
  readonly children?: readonly AutomationCondition[]
}
export interface AutomationTarget { readonly kind: AutomationTargetKind, readonly referenceId?: string }
export interface AutomationAction {
  readonly id: string
  readonly type: AutomationActionType
  readonly target: AutomationTarget
  readonly broadcastMessage?: Readonly<{ message: string }>
  readonly privateMessage?: Readonly<{ message: string }>
  readonly announcement?: Readonly<{ message: string }>
  readonly grantItem?: Readonly<{ resourceId: string, amount: number }>
  readonly grantRewardPackage?: Readonly<{ rewardPackageId: string }>
  readonly adjustEconomy?: Readonly<{ amount: number }>
  readonly kickPlayer?: Readonly<{ reason: string }>
  readonly mutePlayer?: Readonly<{ durationSeconds: number, reason: string }>
  readonly restrictedCommand?: Readonly<{ commandCatalogKey: string }>
  readonly discordMessage?: Readonly<{ message: string }>
}

export interface AutomationRuleDraft {
  readonly id: string
  readonly expectedVersion?: number
  readonly name: string
  readonly isEnabled: boolean
  readonly trigger: AutomationTrigger
  readonly condition: AutomationCondition
  readonly actions: readonly AutomationAction[]
  readonly cooldownSeconds: number
  readonly cooldownScope: AutomationCooldownScope
  readonly concurrencyPolicy: AutomationConcurrencyPolicy
  readonly failurePolicy: AutomationFailurePolicy
}

export interface AutomationRule extends Omit<AutomationRuleDraft, 'expectedVersion'> {
  readonly version: number
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
}

export interface AutomationValidationIssue { readonly code: string, readonly path: string }
export interface AutomationValidation { readonly isValid: boolean, readonly issues: readonly AutomationValidationIssue[] }
export interface AutomationTriggerSnapshot {
  readonly triggerId: string
  readonly trigger: AutomationTrigger
  readonly occurredAtUtc: string
  readonly actor?: Readonly<{ crossplatformId?: string, entityId?: number, group?: string, permissionLevel?: number }>
  readonly chat?: Readonly<{ text?: string }>
  readonly cron?: Readonly<{ scheduledForUtc?: string }>
  readonly bloodMoon?: Readonly<{ phase?: string }>
  readonly gapIds: readonly string[]
}
export interface AutomationDryRunResult {
  readonly validation: AutomationValidation
  readonly evaluation?: Readonly<{
    truth: AutomationTruth
    trace: readonly Readonly<{ nodeId: string, fieldKey?: string, truth: AutomationTruth, isValueKnown: boolean }>[]
  }>
  readonly plannedActions: readonly Readonly<{
    ordinal: number
    actionId: string
    actionType: AutomationActionType
    dependency: Readonly<{ status: string, errorCode?: string }>
    target: Readonly<{ isResolved: boolean, errorCode?: string }>
    wouldExecute: boolean
  }>[]
}

export interface AutomationConditionResult {
  readonly nodeId: string
  readonly truth: AutomationTruth
}

export interface AutomationActionResult {
  readonly ordinal: number
  readonly actionType: AutomationActionType
  readonly status: AutomationActionResultStatus
  readonly errorCode: string | null
  readonly startedAtUtc: string
  readonly completedAtUtc: string | null
}

export interface AutomationExecution {
  readonly executionId: string
  readonly ruleId: string
  readonly triggerId: string
  readonly status: AutomationExecutionStatus
  readonly correlationId: string
  readonly startedAtUtc: string | null
  readonly completedAtUtc: string | null
  readonly errorCode: string | null
  readonly conditions: readonly AutomationConditionResult[]
  readonly actions: readonly AutomationActionResult[]
}
