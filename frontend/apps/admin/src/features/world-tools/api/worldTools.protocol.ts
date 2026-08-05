import type { WorldOperationKind, WorldOperationStatus, WorldPosition, WorldSourceState } from './worldTools.types'
import { requestJson } from '../../../shared/api/http'

const sourceStates = new Set(['Available', 'Success', 'Partial', 'Stale', 'Unavailable'])
const operationStatuses = new Set<WorldOperationStatus>([
  'Queued',
  'Running',
  'Succeeded',
  'Failed',
  'Cancelled',
  'Interrupted',
  'ResultUnknown',
  'RollbackFailed',
])
const operationKinds = new Set<WorldOperationKind>([
  'DeleteLandClaim',
  'MoveOnlinePlayer',
  'MoveEntity',
  'RefreshMapResources',
  'RenderExploredMap',
  'RenderFullMap',
  'CopyRegion',
  'FillRegion',
  'ClearRegion',
  'PasteRegion',
  'SetBlock',
  'PlacePrefab',
  'RemovePrefab',
  'SpawnEntity',
  'DeleteEntity',
  'CleanupEntities',
  'ReloadBlocks',
  'ReloadItems',
  'ReloadEntityClasses',
  'ReloadPrefabs',
  'CollectGarbage',
  'UndoChangeSet',
])

const positionKeys = ['x', 'y', 'z'] as const
const extentKeys = ['minimumX', 'minimumZ', 'maximumX', 'maximumZ'] as const

export function record(value: unknown, keys: readonly string[], message = 'Invalid world tools response'): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new Error(message)
  const source = value as Record<string, unknown>
  const actual = Object.keys(source).sort()
  const expected = [...keys].sort()
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index]))
    throw new Error(message)
  return source
}

export function text(value: unknown, message = 'Invalid world tools response'): string {
  if (typeof value !== 'string' || value.trim() === '')
    throw new Error(message)
  return value
}

export function nullableText(value: unknown): string | null {
  return value === null ? null : text(value)
}

export function finite(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value))
    throw new Error('Invalid world tools response')
  return value
}

export function nullableFinite(value: unknown): number | null {
  return value === null ? null : finite(value)
}

export function integer(value: unknown): number {
  if (!Number.isSafeInteger(value))
    throw new Error('Invalid world tools response')
  return value as number
}

export function nullableInteger(value: unknown): number | null {
  return value === null ? null : integer(value)
}

export function nullableBoolean(value: unknown): boolean | null {
  if (value !== null && typeof value !== 'boolean')
    throw new Error('Invalid world tools response')
  return value as boolean | null
}

export function utc(value: unknown): string {
  const result = text(value)
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(result)
  if (match === null)
    throw new Error('Invalid world tools response')
  const [year, month, day, hour, minute, second] = match.slice(1, 7).map(Number)
  const milliseconds = Number((match[7] ?? '').padEnd(3, '0').slice(0, 3))
  const timestamp = Date.parse(result)
  const parsed = new Date(timestamp)
  if (!Number.isFinite(timestamp)
    || parsed.getUTCFullYear() !== year
    || parsed.getUTCMonth() + 1 !== month
    || parsed.getUTCDate() !== day
    || parsed.getUTCHours() !== hour
    || parsed.getUTCMinutes() !== minute
    || parsed.getUTCSeconds() !== second
    || parsed.getUTCMilliseconds() !== milliseconds) {
    throw new Error('Invalid world tools response')
  }
  return result
}

export function nullableUtc(value: unknown): string | null {
  return value === null ? null : utc(value)
}

export function sourceState(value: unknown): WorldSourceState {
  if (typeof value !== 'string' || !sourceStates.has(value))
    throw new Error('Invalid world source state')
  return value === 'Available' ? 'Success' : value as WorldSourceState
}

export function parsePosition(value: unknown): WorldPosition {
  const source = record(value, positionKeys)
  return Object.freeze({ x: finite(source.x), y: finite(source.y), z: finite(source.z) })
}

export function parseExtent(value: unknown) {
  const source = record(value, extentKeys)
  return Object.freeze({
    minimumX: finite(source.minimumX),
    minimumZ: finite(source.minimumZ),
    maximumX: finite(source.maximumX),
    maximumZ: finite(source.maximumZ),
  })
}

export function parseOperationStatus(value: unknown): WorldOperationStatus {
  if (typeof value !== 'string' || !operationStatuses.has(value as WorldOperationStatus))
    throw new Error('Invalid world operation status')
  return value as WorldOperationStatus
}

export function parseOperationKind(value: unknown): WorldOperationKind {
  if (typeof value !== 'string' || !operationKinds.has(value as WorldOperationKind))
    throw new Error('Invalid world operation kind')
  return value as WorldOperationKind
}

export function get<T>(path: string, authorizationHeader: string, parser: (value: unknown) => T, signal?: AbortSignal): Promise<T> {
  return requestJson<unknown>(path, { headers: { Authorization: authorizationHeader }, signal }).then(parser)
}
