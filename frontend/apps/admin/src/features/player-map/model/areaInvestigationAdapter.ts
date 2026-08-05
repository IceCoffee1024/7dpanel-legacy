import type {
  AreaInvestigationPlayer,
  AreaInvestigationQuery,
  AreaInvestigationResponse,
} from './areaInvestigationProjection'

import { requestJson } from '../../../shared/api/http'
import { isRecord, isValidUtcTimestamp } from '../../players/api/playerSnapshot'
import {
  areaInvestigationPath,
  MAX_AREA_INVESTIGATION_LIMIT,
  positiveInteger,
} from './areaInvestigationProjection'

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

function finiteNumber(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value))
    throw new Error('number')
  return value
}

function nonBlankString(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '')
    throw new Error('string')
  return value
}

function nonNegativeInteger(value: unknown, maximum = Number.MAX_SAFE_INTEGER): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0 || value > maximum)
    throw new Error('integer')
  return value
}

function utcTimestamp(value: unknown): string {
  if (typeof value !== 'string' || !isValidUtcTimestamp(value))
    throw new Error('timestamp')
  return value
}

function parsePlayer(value: unknown): AreaInvestigationPlayer {
  if (!isRecord(value) || !hasExactKeys(value, [
    'crossplatformId',
    'displayName',
    'firstHitUtc',
    'lastHitUtc',
    'hitObservationCount',
    'lastPosition',
  ]) || !isRecord(value.lastPosition) || !hasExactKeys(value.lastPosition, ['x', 'y', 'z'])) {
    throw new Error('player')
  }
  const firstHitUtc = utcTimestamp(value.firstHitUtc)
  const lastHitUtc = utcTimestamp(value.lastHitUtc)
  if (Date.parse(firstHitUtc) > Date.parse(lastHitUtc))
    throw new Error('observation order')
  const position = Object.freeze({
    x: finiteNumber(value.lastPosition.x),
    y: finiteNumber(value.lastPosition.y),
    z: finiteNumber(value.lastPosition.z),
  })
  return Object.freeze({
    combinedId: nonBlankString(value.crossplatformId),
    displayName: nonBlankString(value.displayName),
    firstMatchingObservation: Object.freeze({ observedAtUtc: firstHitUtc }),
    lastMatchingObservation: Object.freeze({ observedAtUtc: lastHitUtc, position }),
    matchingObservationCount: positiveInteger(value.hitObservationCount),
  })
}

export function parseAreaInvestigationResponse(value: unknown): AreaInvestigationResponse {
  try {
    if (!isRecord(value) || !hasExactKeys(value, [
      'hits',
      'candidateObservationCount',
      'matchingObservationCount',
      'candidateObservationLimitReached',
      'playerResultLimitReached',
    ]) || !Array.isArray(value.hits) || value.hits.length > MAX_AREA_INVESTIGATION_LIMIT
    || typeof value.candidateObservationLimitReached !== 'boolean'
    || typeof value.playerResultLimitReached !== 'boolean') {
      throw new Error('shape')
    }
    const players = value.hits.map(parsePlayer)
    const combinedIds = new Set(players.map(player => player.combinedId))
    if (combinedIds.size !== players.length)
      throw new Error('duplicate player')
    const candidateObservationCount = nonNegativeInteger(value.candidateObservationCount, 20_000)
    const matchingObservationCount = nonNegativeInteger(value.matchingObservationCount, candidateObservationCount)
    const representedMatchingObservations = players.reduce((total, player) => total + player.matchingObservationCount, 0)
    if (representedMatchingObservations > matchingObservationCount
      || (!value.playerResultLimitReached && representedMatchingObservations !== matchingObservationCount)) {
      throw new Error('observation count')
    }
    const truncation = Object.freeze({
      candidateObservations: value.candidateObservationLimitReached,
      playerResults: value.playerResultLimitReached,
    })
    return Object.freeze({
      players: Object.freeze(players),
      candidateObservationCount,
      matchingObservationCount,
      truncated: truncation.candidateObservations || truncation.playerResults,
      truncation,
    })
  }
  catch {
    throw new Error('Invalid area investigation response')
  }
}

export async function fetchAreaInvestigation(
  authorizationHeader: string,
  query: AreaInvestigationQuery,
  signal: AbortSignal,
): Promise<AreaInvestigationResponse> {
  const value = await requestJson<unknown>(areaInvestigationPath(query), {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseAreaInvestigationResponse(value)
}
