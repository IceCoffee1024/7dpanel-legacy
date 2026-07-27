import { requestJson } from '../../../shared/api/http'

export const DAILY_REWARD_RULE_ID = 'daily'

export interface DailyRewardPolicy {
  readonly ruleId: string
  readonly rewardPackageId: string
  readonly enabled: boolean
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface DailyRewardPolicyUpdateRequest {
  readonly rewardPackageId: string
  readonly enabled: boolean
  readonly expectedRowVersion: bigint | null
}

const policyKeys = ['ruleId', 'rewardPackageId', 'enabled', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const

function invalid(): never {
  throw new Error('Invalid daily reward policy response')
}

function record(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    return invalid()
  const source = value as Record<string, unknown>
  const actual = Object.keys(source).sort()
  const expected = [...policyKeys].sort()
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index]))
    return invalid()
  return source
}

function text(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '')
    return invalid()
  return value
}

function utc(value: unknown): string {
  const candidate = text(value)
  if (!Number.isFinite(Date.parse(candidate)) || !/Z$|[+]00:00$/.test(candidate))
    return invalid()
  return candidate
}

function rowVersion(value: unknown): bigint {
  const parsed = typeof value === 'number' && Number.isSafeInteger(value)
    ? BigInt(value)
    : typeof value === 'string' && /^\d+$/.test(value)
      ? BigInt(value)
      : null
  if (parsed === null || parsed < 0n)
    return invalid()
  return parsed
}

function wire(value: bigint): number | string {
  const numberValue = Number(value)
  return Number.isSafeInteger(numberValue) ? numberValue : value.toString()
}

function headers(authorization: string, json = false): Record<string, string> {
  return json
    ? { Authorization: authorization, 'Content-Type': 'application/json' }
    : { Authorization: authorization }
}

export function parseDailyRewardPolicy(value: unknown): DailyRewardPolicy {
  const source = record(value)
  const createdAtUtc = utc(source.createdAtUtc)
  const updatedAtUtc = utc(source.updatedAtUtc)
  if (Date.parse(updatedAtUtc) < Date.parse(createdAtUtc) || typeof source.enabled !== 'boolean')
    return invalid()
  return Object.freeze({
    ruleId: text(source.ruleId),
    rewardPackageId: text(source.rewardPackageId),
    enabled: source.enabled,
    createdAtUtc,
    updatedAtUtc,
    rowVersion: rowVersion(source.rowVersion),
  })
}

export function toDailyRewardPolicyUpdateRequest(
  input: DailyRewardPolicyUpdateRequest,
): { rewardPackageId: string, enabled: boolean, expectedRowVersion: number | string | null } {
  const rewardPackageId = input.rewardPackageId.trim()
  if (rewardPackageId === '')
    throw new Error('Daily reward package is required')
  return {
    rewardPackageId,
    enabled: input.enabled,
    expectedRowVersion: input.expectedRowVersion === null
      ? null
      : wire(input.expectedRowVersion),
  }
}

export async function fetchDailyRewardPolicy(
  authorization: string,
  signal?: AbortSignal,
): Promise<DailyRewardPolicy> {
  return parseDailyRewardPolicy(await requestJson<unknown>(
    `/api/v1/daily-reward-rules/${DAILY_REWARD_RULE_ID}`,
    { headers: headers(authorization), expectedStatus: 200, signal },
  ))
}

export async function saveDailyRewardPolicy(
  authorization: string,
  input: DailyRewardPolicyUpdateRequest,
  signal?: AbortSignal,
): Promise<DailyRewardPolicy> {
  const response = await requestJson<unknown>(
    `/api/v1/daily-reward-rules/${DAILY_REWARD_RULE_ID}`,
    {
      method: 'PUT',
      headers: headers(authorization, true),
      body: JSON.stringify(toDailyRewardPolicyUpdateRequest(input)),
      expectedStatus: 200,
      signal,
    },
  )
  const policy = parseDailyRewardPolicy(response)
  if (policy.ruleId !== DAILY_REWARD_RULE_ID)
    return invalid()
  return policy
}
