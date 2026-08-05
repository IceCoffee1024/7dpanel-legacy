import type {
  AutomationAction,
  AutomationActionResultStatus,
  AutomationActionType,
  AutomationConcurrencyPolicy,
  AutomationCondition,
  AutomationConditionKind,
  AutomationConditionOperator,
  AutomationCooldownScope,
  AutomationDryRunResult,
  AutomationExecution,
  AutomationExecutionStatus,
  AutomationFailurePolicy,
  AutomationPredicate,
  AutomationRule,
  AutomationRuleDraft,
  AutomationTarget,
  AutomationTargetKind,
  AutomationTimeOfDay,
  AutomationTimeWindow,
  AutomationTrigger,
  AutomationTriggerSnapshot,
  AutomationTriggerType,
  AutomationTruth,
  AutomationValidation,
} from './automation.types'

import { requestJson } from '../../../shared/api/http'

export type * from './automation.types'

const triggerTypes = new Set<AutomationTriggerType>(['PlayerJoined', 'PlayerLeft', 'ChatMessage', 'Cron', 'BloodMoonPhaseEntered'])
const conditionKinds = new Set<AutomationConditionKind>(['All', 'Any', 'Not', 'Predicate'])
const conditionOperators = new Set<AutomationConditionOperator>(['Equals', 'NotEquals', 'InSet', 'NumberRange', 'TimeWindow', 'PlayerGroup', 'Permission', 'Cooldown'])
const actionTypes = new Set<AutomationActionType>(['BroadcastMessage', 'PrivateMessage', 'Announcement', 'GrantItem', 'GrantRewardPackage', 'AdjustEconomy', 'KickPlayer', 'MutePlayer', 'RestrictedCommand', 'DiscordMessage'])
const targetKinds = new Set<AutomationTargetKind>(['Global', 'TriggerPlayer', 'StablePlayer', 'DiscordTarget'])
const cooldownScopes = new Set<AutomationCooldownScope>(['Rule', 'RulePlayer'])
const concurrencyPolicies = new Set<AutomationConcurrencyPolicy>(['SkipIfRunning', 'QueueOne'])
const failurePolicies = new Set<AutomationFailurePolicy>(['StopOnFailure', 'Continue'])
const truthValues = new Set<AutomationTruth>(['Matched', 'NotMatched', 'Unknown'])
const executionStatuses = new Set<AutomationExecutionStatus>(['Pending', 'Running', 'Queued', 'Skipped', 'Succeeded', 'Failed', 'ResultUnknown'])
const actionResultStatuses = new Set<AutomationActionResultStatus>(['Pending', 'Running', 'Succeeded', 'Failed', 'ResultUnknown'])

function invalid(): never {
  throw new Error('Invalid server protocol')
}
function record(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    invalid()
  return value as Record<string, unknown>
}
function keys(value: Record<string, unknown>, allowed: readonly string[]) {
  if (Object.keys(value).some(key => !allowed.includes(key)))
    invalid()
}
function text(value: unknown): string {
  return typeof value === 'string' ? value : invalid()
}
function boolean(value: unknown): boolean {
  return typeof value === 'boolean' ? value : invalid()
}
function integer(value: unknown, minimum = 0): number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum ? value : invalid()
}
function utc(value: unknown): string {
  const result = text(value)
  return Number.isFinite(Date.parse(result)) && /(?:Z|[+-]00:00)$/.test(result) ? result : invalid()
}
function nullableText(value: unknown): string | null {
  return value === null ? null : text(value)
}
function nullableUtc(value: unknown): string | null {
  return value === null ? null : utc(value)
}
function parseTrigger(value: unknown): AutomationTrigger {
  const source = record(value)
  keys(source, ['type'])
  if (typeof source.type !== 'string' || !triggerTypes.has(source.type as AutomationTriggerType))
    invalid()
  return Object.freeze({ type: source.type as AutomationTriggerType })
}

function parseTimeOfDay(value: unknown): AutomationTimeOfDay {
  const source = record(value)
  keys(source, ['hour', 'minute'])
  const hour = integer(source.hour)
  const minute = integer(source.minute)
  if (hour > 23 || minute > 59)
    invalid()
  return Object.freeze({ hour, minute })
}

function parsePredicate(value: unknown): AutomationPredicate {
  const source = record(value)
  keys(source, ['fieldKey', 'operator', 'scalarValue', 'minimumInclusive', 'maximumInclusive', 'setValues', 'window'])
  if (typeof source.operator !== 'string' || !conditionOperators.has(source.operator as AutomationConditionOperator))
    invalid()
  const predicate: AutomationPredicate = {
    fieldKey: text(source.fieldKey),
    operator: source.operator as AutomationConditionOperator,
    ...(source.scalarValue === undefined ? {} : { scalarValue: text(source.scalarValue) }),
    ...(source.minimumInclusive === undefined ? {} : { minimumInclusive: integer(source.minimumInclusive, Number.MIN_SAFE_INTEGER) }),
    ...(source.maximumInclusive === undefined ? {} : { maximumInclusive: integer(source.maximumInclusive, Number.MIN_SAFE_INTEGER) }),
    ...(source.setValues === undefined ? {} : { setValues: Object.freeze((Array.isArray(source.setValues) ? source.setValues : invalid()).map(text)) }),
    ...(source.window === undefined ? {} : { window: parseWindow(source.window) }),
  }
  return Object.freeze(predicate)
}

function parseWindow(value: unknown): AutomationTimeWindow {
  const source = record(value)
  keys(source, ['timeZoneId', 'startInclusive', 'endInclusive'])
  return Object.freeze({ timeZoneId: text(source.timeZoneId), startInclusive: parseTimeOfDay(source.startInclusive), endInclusive: parseTimeOfDay(source.endInclusive) })
}

function parseCondition(value: unknown, depth = 0): AutomationCondition {
  if (depth > 5)
    invalid()
  const source = record(value)
  keys(source, ['nodeId', 'kind', 'predicate', 'children'])
  if (typeof source.kind !== 'string' || !conditionKinds.has(source.kind as AutomationConditionKind))
    invalid()
  const kind = source.kind as AutomationConditionKind
  const children = source.children === undefined ? undefined : Object.freeze((Array.isArray(source.children) ? source.children : invalid()).map(child => parseCondition(child, depth + 1)))
  const predicate = source.predicate === undefined ? undefined : parsePredicate(source.predicate)
  if ((kind === 'Predicate') !== (predicate !== undefined) || (kind === 'Predicate') === (children !== undefined))
    invalid()
  return Object.freeze({ nodeId: text(source.nodeId), kind, ...(predicate === undefined ? {} : { predicate }), ...(children === undefined ? {} : { children }) })
}

function parseTarget(value: unknown): AutomationTarget {
  const source = record(value)
  keys(source, ['kind', 'referenceId'])
  if (typeof source.kind !== 'string' || !targetKinds.has(source.kind as AutomationTargetKind))
    invalid()
  const target = Object.freeze({ kind: source.kind as AutomationTargetKind, ...(source.referenceId === undefined ? {} : { referenceId: text(source.referenceId) }) })
  if ((target.kind === 'StablePlayer' || target.kind === 'DiscordTarget') !== ('referenceId' in target))
    invalid()
  return target
}

function body(value: unknown, allowed: readonly string[]): Record<string, unknown> {
  const source = record(value)
  keys(source, allowed)
  return source
}

function parseAction(value: unknown): AutomationAction {
  const source = record(value)
  const payloadKeys = ['broadcastMessage', 'privateMessage', 'announcement', 'grantItem', 'grantRewardPackage', 'adjustEconomy', 'kickPlayer', 'mutePlayer', 'restrictedCommand', 'discordMessage'] as const
  keys(source, ['id', 'type', 'target', ...payloadKeys])
  if (typeof source.type !== 'string' || !actionTypes.has(source.type as AutomationActionType))
    invalid()
  const actionType = source.type as AutomationActionType
  const expectedKey = `${actionType[0]!.toLowerCase()}${actionType.slice(1)}`
  if (payloadKeys.filter(key => source[key] !== undefined).length !== 1 || source[expectedKey] === undefined)
    invalid()
  const result: Record<string, unknown> = { id: text(source.id), type: actionType, target: parseTarget(source.target) }
  const payload = source[expectedKey]
  if (['BroadcastMessage', 'PrivateMessage', 'Announcement', 'DiscordMessage'].includes(actionType)) {
    const parsed = body(payload, ['message'])
    result[expectedKey] = Object.freeze({ message: text(parsed.message) })
  }
  else if (actionType === 'GrantItem') {
    const parsed = body(payload, ['resourceId', 'amount'])
    result[expectedKey] = Object.freeze({ resourceId: text(parsed.resourceId), amount: integer(parsed.amount, 1) })
  }
  else if (actionType === 'GrantRewardPackage') {
    const parsed = body(payload, ['rewardPackageId'])
    result[expectedKey] = Object.freeze({ rewardPackageId: text(parsed.rewardPackageId) })
  }
  else if (actionType === 'AdjustEconomy') {
    const parsed = body(payload, ['amount'])
    result[expectedKey] = Object.freeze({ amount: integer(parsed.amount, Number.MIN_SAFE_INTEGER) })
  }
  else if (actionType === 'KickPlayer') {
    const parsed = body(payload, ['reason'])
    result[expectedKey] = Object.freeze({ reason: text(parsed.reason) })
  }
  else if (actionType === 'MutePlayer') {
    const parsed = body(payload, ['durationSeconds', 'reason'])
    result[expectedKey] = Object.freeze({ durationSeconds: integer(parsed.durationSeconds, 1), reason: text(parsed.reason) })
  }
  else if (actionType === 'RestrictedCommand') {
    const parsed = body(payload, ['commandCatalogKey'])
    result[expectedKey] = Object.freeze({ commandCatalogKey: text(parsed.commandCatalogKey) })
  }
  return Object.freeze(result) as unknown as AutomationAction
}

function parseRule(value: unknown): AutomationRule {
  const source = record(value)
  keys(source, ['id', 'version', 'name', 'isEnabled', 'trigger', 'condition', 'actions', 'cooldownSeconds', 'cooldownScope', 'concurrencyPolicy', 'failurePolicy', 'createdAtUtc', 'updatedAtUtc'])
  if (!Array.isArray(source.actions)
    || typeof source.cooldownScope !== 'string' || !cooldownScopes.has(source.cooldownScope as AutomationCooldownScope)
    || typeof source.concurrencyPolicy !== 'string' || !concurrencyPolicies.has(source.concurrencyPolicy as AutomationConcurrencyPolicy)
    || typeof source.failurePolicy !== 'string' || !failurePolicies.has(source.failurePolicy as AutomationFailurePolicy)) {
    invalid()
  }
  return Object.freeze({
    id: text(source.id),
    version: integer(source.version, 1),
    name: text(source.name),
    isEnabled: boolean(source.isEnabled),
    trigger: parseTrigger(source.trigger),
    condition: parseCondition(source.condition),
    actions: Object.freeze(source.actions.map(parseAction)),
    cooldownSeconds: integer(source.cooldownSeconds),
    cooldownScope: source.cooldownScope as AutomationCooldownScope,
    concurrencyPolicy: source.concurrencyPolicy as AutomationConcurrencyPolicy,
    failurePolicy: source.failurePolicy as AutomationFailurePolicy,
    createdAtUtc: utc(source.createdAtUtc),
    updatedAtUtc: utc(source.updatedAtUtc),
  })
}

export function parseAutomationRules(value: unknown): readonly AutomationRule[] {
  if (!Array.isArray(value))
    invalid()
  return Object.freeze(value.map(parseRule))
}

function parseValidation(value: unknown): AutomationValidation {
  const source = record(value)
  keys(source, ['isValid', 'issues'])
  if (!Array.isArray(source.issues))
    invalid()
  return Object.freeze({ isValid: boolean(source.isValid), issues: Object.freeze(source.issues.map((item) => {
    const issue = record(item)
    keys(issue, ['code', 'path'])
    return Object.freeze({ code: text(issue.code), path: text(issue.path) })
  })) })
}

function parseDryRun(value: unknown): AutomationDryRunResult {
  const source = record(value)
  keys(source, ['validation', 'evaluation', 'plannedActions'])
  if (!Array.isArray(source.plannedActions))
    invalid()
  let evaluation: AutomationDryRunResult['evaluation']
  if (source.evaluation !== undefined && source.evaluation !== null) {
    const item = record(source.evaluation)
    keys(item, ['truth', 'trace'])
    if (typeof item.truth !== 'string' || !truthValues.has(item.truth as AutomationTruth) || !Array.isArray(item.trace))
      invalid()
    evaluation = Object.freeze({ truth: item.truth as AutomationTruth, trace: Object.freeze(item.trace.map((entry) => {
      const trace = record(entry)
      keys(trace, ['nodeId', 'fieldKey', 'truth', 'isValueKnown'])
      if (typeof trace.truth !== 'string' || !truthValues.has(trace.truth as AutomationTruth))
        invalid()
      return Object.freeze({ nodeId: text(trace.nodeId), ...(trace.fieldKey === undefined ? {} : { fieldKey: text(trace.fieldKey) }), truth: trace.truth as AutomationTruth, isValueKnown: boolean(trace.isValueKnown) })
    })) })
  }
  const plannedActions = Object.freeze(source.plannedActions.map((entry) => {
    const item = record(entry)
    keys(item, ['ordinal', 'actionId', 'actionType', 'dependency', 'target', 'wouldExecute'])
    if (typeof item.actionType !== 'string' || !actionTypes.has(item.actionType as AutomationActionType))
      invalid()
    const dependency = body(item.dependency, ['status', 'errorCode'])
    const target = body(item.target, ['isResolved', 'errorCode'])
    return Object.freeze({
      ordinal: integer(item.ordinal),
      actionId: text(item.actionId),
      actionType: item.actionType as AutomationActionType,
      dependency: Object.freeze({ status: text(dependency.status), ...(dependency.errorCode === undefined ? {} : { errorCode: text(dependency.errorCode) }) }),
      target: Object.freeze({ isResolved: boolean(target.isResolved), ...(target.errorCode === undefined ? {} : { errorCode: text(target.errorCode) }) }),
      wouldExecute: boolean(item.wouldExecute),
    })
  }))
  return Object.freeze({ validation: parseValidation(source.validation), ...(evaluation === undefined ? {} : { evaluation }), plannedActions })
}

function headers(authorization: string, json = false): HeadersInit {
  return { Authorization: authorization, ...(json ? { 'Content-Type': 'application/json' } : {}) }
}

export async function listAutomationRules(authorization: string, signal?: AbortSignal): Promise<readonly AutomationRule[]> {
  return parseAutomationRules(await requestJson<unknown>('/api/v1/automations', { headers: headers(authorization), signal }))
}

export async function saveAutomationRule(authorization: string, draft: AutomationRuleDraft, signal?: AbortSignal): Promise<AutomationRule> {
  const creating = draft.expectedVersion === undefined
  const value = await requestJson<unknown>(creating ? '/api/v1/automations' : `/api/v1/automations/${encodeURIComponent(draft.id)}`, {
    method: creating ? 'POST' : 'PUT',
    headers: headers(authorization, true),
    body: JSON.stringify(draft),
    expectedStatus: creating ? 201 : 200,
    signal,
  })
  return parseAutomationRules([value])[0]!
}

export async function deleteAutomationRule(authorization: string, rule: AutomationRule, signal?: AbortSignal): Promise<void> {
  await requestJson<void>(`/api/v1/automations/${encodeURIComponent(rule.id)}?expectedVersion=${rule.version}`, { method: 'DELETE', headers: headers(authorization), expectedStatus: 204, signal })
}

export async function validateAutomationRule(authorization: string, draft: AutomationRuleDraft, signal?: AbortSignal): Promise<AutomationValidation> {
  return parseValidation(await requestJson<unknown>('/api/v1/automations/validate', { method: 'POST', headers: headers(authorization, true), body: JSON.stringify(draft), signal }))
}

export async function dryRunAutomationRule(authorization: string, draft: AutomationRuleDraft, snapshot: AutomationTriggerSnapshot, signal?: AbortSignal): Promise<AutomationDryRunResult> {
  return parseDryRun(await requestJson<unknown>('/api/v1/automations/dry-run', { method: 'POST', headers: headers(authorization, true), body: JSON.stringify({ rule: draft, snapshot }), signal }))
}

function parseExecution(value: unknown): AutomationExecution {
  const source = record(value)
  keys(source, ['executionId', 'ruleId', 'triggerId', 'status', 'correlationId', 'startedAtUtc', 'completedAtUtc', 'errorCode', 'conditions', 'actions'])
  if (typeof source.status !== 'string' || !executionStatuses.has(source.status as AutomationExecutionStatus) || !Array.isArray(source.conditions) || !Array.isArray(source.actions))
    invalid()
  const conditions = Object.freeze(source.conditions.map((entry) => {
    const item = record(entry)
    keys(item, ['nodeId', 'truth'])
    if (typeof item.truth !== 'string' || !truthValues.has(item.truth as AutomationTruth))
      invalid()
    return Object.freeze({ nodeId: text(item.nodeId), truth: item.truth as AutomationTruth })
  }))
  const actions = Object.freeze(source.actions.map((entry) => {
    const item = record(entry)
    keys(item, ['ordinal', 'actionType', 'status', 'errorCode', 'startedAtUtc', 'completedAtUtc'])
    if (typeof item.actionType !== 'string' || !actionTypes.has(item.actionType as AutomationActionType)
      || typeof item.status !== 'string' || !actionResultStatuses.has(item.status as AutomationActionResultStatus)) {
      invalid()
    }
    return Object.freeze({
      ordinal: integer(item.ordinal),
      actionType: item.actionType as AutomationActionType,
      status: item.status as AutomationActionResultStatus,
      errorCode: nullableText(item.errorCode),
      startedAtUtc: utc(item.startedAtUtc),
      completedAtUtc: nullableUtc(item.completedAtUtc),
    })
  }))
  return Object.freeze({
    executionId: text(source.executionId),
    ruleId: text(source.ruleId),
    triggerId: text(source.triggerId),
    status: source.status as AutomationExecutionStatus,
    correlationId: text(source.correlationId),
    startedAtUtc: nullableUtc(source.startedAtUtc),
    completedAtUtc: nullableUtc(source.completedAtUtc),
    errorCode: nullableText(source.errorCode),
    conditions,
    actions,
  })
}

export async function queryAutomationExecutions(authorization: string, signal?: AbortSignal): Promise<readonly AutomationExecution[]> {
  const value = await requestJson<unknown>('/api/v1/automations/executions', { headers: headers(authorization), signal })
  return Array.isArray(value) ? Object.freeze(value.map(parseExecution)) : invalid()
}

export async function getAutomationExecution(authorization: string, executionId: string, signal?: AbortSignal): Promise<AutomationExecution> {
  return parseExecution(await requestJson<unknown>(`/api/v1/automations/executions/${encodeURIComponent(executionId)}`, { headers: headers(authorization), signal }))
}
