import { requestJson } from '../../../shared/api/http'

export const backupPolicyKinds = ['World', 'PanelDatabase', 'ServerConfiguration'] as const

export type BackupPolicyKind = typeof backupPolicyKinds[number]

export interface BackupPolicy {
  readonly kind: BackupPolicyKind
  readonly enabled: boolean
  readonly cronExpression: string
  readonly timeZoneId: string
  readonly backupRootId: string
  readonly retentionCount: number
  readonly retentionDays: number
  readonly compressionEnabled: boolean
  readonly rowVersion: number
}

export interface BackupPolicyUpdate {
  readonly kind: BackupPolicyKind
  readonly enabled: boolean
  readonly cronExpression: string
  readonly timeZoneId: string
  readonly backupRootId: string
  readonly retentionCount: number
  readonly retentionDays: number
  readonly compressionEnabled: boolean
  readonly rowVersion: number
}

const backupPolicyKindSet = new Set<BackupPolicyKind>(backupPolicyKinds)
const backupPolicyKeys = [
  'kind',
  'enabled',
  'cronExpression',
  'timeZoneId',
  'backupRootId',
  'retentionCount',
  'retentionDays',
  'compressionEnabled',
  'rowVersion',
] as const

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

function isNonBlankString(value: unknown): value is string {
  return typeof value === 'string' && value.trim() !== ''
}

function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

export function parseBackupPolicy(value: unknown): BackupPolicy {
  if (!isRecord(value)
    || !hasExactKeys(value, backupPolicyKeys)
    || typeof value.kind !== 'string'
    || !backupPolicyKindSet.has(value.kind as BackupPolicyKind)
    || typeof value.enabled !== 'boolean'
    || !isNonBlankString(value.cronExpression)
    || !isNonBlankString(value.timeZoneId)
    || !isNonBlankString(value.backupRootId)
    || !isNonNegativeInteger(value.retentionCount)
    || !isNonNegativeInteger(value.retentionDays)
    || typeof value.compressionEnabled !== 'boolean'
    || !isNonNegativeInteger(value.rowVersion)) {
    throw new Error('Invalid backup policy response')
  }

  return Object.freeze({
    kind: value.kind as BackupPolicyKind,
    enabled: value.enabled,
    cronExpression: value.cronExpression,
    timeZoneId: value.timeZoneId,
    backupRootId: value.backupRootId,
    retentionCount: value.retentionCount,
    retentionDays: value.retentionDays,
    compressionEnabled: value.compressionEnabled,
    rowVersion: value.rowVersion,
  })
}

export function parseBackupPolicies(value: unknown): readonly BackupPolicy[] {
  if (!Array.isArray(value))
    throw new Error('Invalid backup policy response')
  const policies = value.map(parseBackupPolicy)
  const byKind = new Map(policies.map(policy => [policy.kind, policy]))
  if (policies.length !== backupPolicyKinds.length || byKind.size !== backupPolicyKinds.length)
    throw new Error('The backup policy response is incomplete')
  return Object.freeze(backupPolicyKinds.map(kind => byKind.get(kind)!))
}

export function toBackupPolicyUpdateRequest(policy: BackupPolicyUpdate) {
  return {
    enabled: policy.enabled,
    cronExpression: policy.cronExpression.trim(),
    timeZoneId: policy.timeZoneId.trim(),
    backupRootId: policy.backupRootId.trim(),
    retentionCount: policy.retentionCount,
    retentionDays: policy.retentionDays,
    compressionEnabled: policy.compressionEnabled,
    expectedRowVersion: policy.rowVersion,
  }
}

export async function fetchBackupPolicies(
  authorizationHeader: string,
  signal?: AbortSignal,
): Promise<readonly BackupPolicy[]> {
  const response = await requestJson<unknown>('/api/v1/backups/policies', {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseBackupPolicies(response)
}

export async function saveBackupPolicy(
  authorizationHeader: string,
  policy: BackupPolicyUpdate,
  signal?: AbortSignal,
): Promise<BackupPolicy> {
  const response = await requestJson<unknown>(
    `/api/v1/backups/policies/${encodeURIComponent(policy.kind)}`,
    {
      method: 'PUT',
      headers: { Authorization: authorizationHeader, 'Content-Type': 'application/json' },
      body: JSON.stringify(toBackupPolicyUpdateRequest(policy)),
      signal,
    },
  )
  return parseBackupPolicy(response)
}
