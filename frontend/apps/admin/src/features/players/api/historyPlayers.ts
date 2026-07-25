import type { PlayerSnapshot } from './playerSnapshot'
import { requestJson } from '../../../shared/api/http'
import {
  isRecord,
  isValidUtcTimestamp,
  parsePlayerSnapshot,
} from './playerSnapshot'

export interface HistoricalPlayerSummary {
  readonly crossplatformId: string
  readonly latestName: string
  readonly firstObservedAtUtc: string
  readonly lastObservedAtUtc: string
  readonly totalObservationCount: number
  readonly retainedSnapshotCount: number
  readonly compactedSnapshotCount: number
  readonly hasGaps: boolean
}

export interface HistoricalPlayersPage {
  readonly players: readonly HistoricalPlayerSummary[]
  readonly nextCursor: string | null
}

export interface HistoricalPlayerGapSummary {
  readonly gapCount: number
  readonly droppedObservationCount: number
}

export interface HistoricalPlayerDetails {
  readonly player: HistoricalPlayerSummary
  readonly gapSummary: HistoricalPlayerGapSummary
}

export interface HistoricalPlayerSnapshot {
  readonly snapshotId: number
  readonly player: PlayerSnapshot
}

export type PlayerHistoryGapReason = 'queue_full' | 'store_failure' | 'shutdown_timeout'

export interface PlayerHistoryGap {
  readonly gapId: string
  readonly crossplatformId: string
  readonly startedAtUtc: string
  readonly completedAtUtc: string
  readonly droppedCount: number
  readonly reason: PlayerHistoryGapReason
  readonly recordedAtUtc: string
}

export interface HistoricalPlayerSnapshotsPage {
  readonly snapshots: readonly HistoricalPlayerSnapshot[]
  readonly nextBeforeSnapshotId: number | null
  readonly gaps: readonly PlayerHistoryGap[]
}

export interface FetchHistoricalPlayersOptions {
  query?: string | null
  pageSize?: number | null
  cursor?: string | null
}

export interface FetchHistoricalSnapshotsOptions {
  pageSize?: number | null
  beforeSnapshotId?: number | null
}

function invalidResponse(): never {
  throw new Error('Invalid historical players response')
}

function parseNonBlankString(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '')
    return invalidResponse()
  return value
}

function parseCrossplatformId(value: unknown): string {
  const result = parseNonBlankString(value)
  if (result.length > 256)
    return invalidResponse()
  return result
}

function parseUtcTimestamp(value: unknown): string {
  if (typeof value !== 'string' || !isValidUtcTimestamp(value))
    return invalidResponse()
  return value
}

function parseSafeInteger(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value))
    return invalidResponse()
  return value
}

function parseNonNegativeSafeInteger(value: unknown): number {
  const result = parseSafeInteger(value)
  if (result < 0)
    return invalidResponse()
  return result
}

function parsePositiveSafeInteger(value: unknown): number {
  const result = parseSafeInteger(value)
  if (result <= 0)
    return invalidResponse()
  return result
}

function parseNullableCursor(value: unknown): string | null {
  if (value === null)
    return null
  if (typeof value !== 'string' || !/^[\w-]+$/.test(value))
    return invalidResponse()
  return value
}

function parseHistoricalPlayerSummary(value: unknown): HistoricalPlayerSummary {
  if (!isRecord(value) || typeof value.hasGaps !== 'boolean')
    return invalidResponse()

  const totalObservationCount = parsePositiveSafeInteger(value.totalObservationCount)
  const retainedSnapshotCount = parsePositiveSafeInteger(value.retainedSnapshotCount)
  const compactedSnapshotCount = parseNonNegativeSafeInteger(value.compactedSnapshotCount)
  if (totalObservationCount !== retainedSnapshotCount + compactedSnapshotCount)
    return invalidResponse()

  return Object.freeze({
    crossplatformId: parseCrossplatformId(value.crossplatformId),
    latestName: parseNonBlankString(value.latestName),
    firstObservedAtUtc: parseUtcTimestamp(value.firstObservedAtUtc),
    lastObservedAtUtc: parseUtcTimestamp(value.lastObservedAtUtc),
    totalObservationCount,
    retainedSnapshotCount,
    compactedSnapshotCount,
    hasGaps: value.hasGaps,
  })
}

function parseHistoricalPlayerGapSummary(value: unknown): HistoricalPlayerGapSummary {
  if (!isRecord(value))
    return invalidResponse()

  return Object.freeze({
    gapCount: parseNonNegativeSafeInteger(value.gapCount),
    droppedObservationCount: parseNonNegativeSafeInteger(value.droppedObservationCount),
  })
}

function parseHistoricalPlayerSnapshot(value: unknown): HistoricalPlayerSnapshot {
  if (!isRecord(value))
    return invalidResponse()

  const player = parsePlayerSnapshot(value)
  if (player.crossplatformIdentity === null)
    return invalidResponse()

  return Object.freeze({
    snapshotId: parsePositiveSafeInteger(value.snapshotId),
    player,
  })
}

function parsePlayerHistoryGap(value: unknown): PlayerHistoryGap {
  if (!isRecord(value))
    return invalidResponse()

  const reason = value.reason
  if (reason !== 'queue_full' && reason !== 'store_failure' && reason !== 'shutdown_timeout')
    return invalidResponse()

  return Object.freeze({
    gapId: parseNonBlankString(value.gapId),
    crossplatformId: parseCrossplatformId(value.crossplatformId),
    startedAtUtc: parseUtcTimestamp(value.startedAtUtc),
    completedAtUtc: parseUtcTimestamp(value.completedAtUtc),
    droppedCount: parsePositiveSafeInteger(value.droppedCount),
    reason,
    recordedAtUtc: parseUtcTimestamp(value.recordedAtUtc),
  })
}

export function parseHistoricalPlayers(value: unknown): HistoricalPlayersPage {
  if (!isRecord(value) || !Array.isArray(value.players))
    return invalidResponse()

  try {
    return Object.freeze({
      players: Object.freeze(value.players.map(parseHistoricalPlayerSummary)),
      nextCursor: parseNullableCursor(value.nextCursor),
    })
  }
  catch {
    return invalidResponse()
  }
}

export function parseHistoricalPlayer(value: unknown): HistoricalPlayerDetails {
  if (!isRecord(value))
    return invalidResponse()

  try {
    return Object.freeze({
      player: parseHistoricalPlayerSummary(value.player),
      gapSummary: parseHistoricalPlayerGapSummary(value.gapSummary),
    })
  }
  catch {
    return invalidResponse()
  }
}

export function parseHistoricalSnapshots(value: unknown): HistoricalPlayerSnapshotsPage {
  if (!isRecord(value) || !Array.isArray(value.snapshots) || !Array.isArray(value.gaps))
    return invalidResponse()

  try {
    return Object.freeze({
      snapshots: Object.freeze(value.snapshots.map(parseHistoricalPlayerSnapshot)),
      nextBeforeSnapshotId: value.nextBeforeSnapshotId === null
        ? null
        : parsePositiveSafeInteger(value.nextBeforeSnapshotId),
      gaps: Object.freeze(value.gaps.map(parsePlayerHistoryGap)),
    })
  }
  catch {
    return invalidResponse()
  }
}

function withQuery(path: string, values: Record<string, string | number | null | undefined>): string {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(values)) {
    if (value !== null && value !== undefined)
      query.set(key, String(value))
  }
  const serialized = query.toString()
  return serialized === '' ? path : `${path}?${serialized}`
}

export async function fetchHistoricalPlayers(
  authorizationHeader: string,
  options: FetchHistoricalPlayersOptions = {},
  signal?: AbortSignal,
): Promise<HistoricalPlayersPage> {
  const response = await requestJson<unknown>(withQuery('/api/v1/players/history', {
    query: options.query,
    pageSize: options.pageSize,
    cursor: options.cursor,
  }), {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseHistoricalPlayers(response)
}

export async function fetchHistoricalPlayer(
  authorizationHeader: string,
  crossplatformId: string,
  signal?: AbortSignal,
): Promise<HistoricalPlayerDetails> {
  const response = await requestJson<unknown>(
    `/api/v1/players/history/${encodeURIComponent(crossplatformId)}`,
    { headers: { Authorization: authorizationHeader }, signal },
  )
  return parseHistoricalPlayer(response)
}

export async function fetchHistoricalSnapshots(
  authorizationHeader: string,
  crossplatformId: string,
  options: FetchHistoricalSnapshotsOptions = {},
  signal?: AbortSignal,
): Promise<HistoricalPlayerSnapshotsPage> {
  const response = await requestJson<unknown>(withQuery(
    `/api/v1/players/history/${encodeURIComponent(crossplatformId)}/snapshots`,
    { pageSize: options.pageSize, beforeSnapshotId: options.beforeSnapshotId },
  ), {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseHistoricalSnapshots(response)
}
