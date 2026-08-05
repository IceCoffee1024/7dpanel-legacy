import { requestJson } from '../../../shared/api/http'
import * as historyPlayers from '../../players/api/historyPlayers'

const { isRecord, isValidUtcTimestamp } = historyPlayers
export type FetchHistoricalPlayersOptions = historyPlayers.FetchHistoricalPlayersOptions
export type HistoricalPlayerDetails = historyPlayers.HistoricalPlayerDetails
export type HistoricalPlayersPage = historyPlayers.HistoricalPlayersPage
export type HistoricalPlayerSummary = historyPlayers.HistoricalPlayerSummary
export { isRecord, isValidUtcTimestamp }

export interface GameExtent {
  readonly minimumX: number
  readonly minimumZ: number
  readonly maximumX: number
  readonly maximumZ: number
}

export type MapAvailability = 'available' | 'stale' | 'unavailable'

export interface MapMetadata {
  readonly availability: 'available' | 'stale'
  readonly observedAtUtc: string
  readonly worldId: string
  readonly worldName: string
  readonly extent: GameExtent
  readonly axes: Readonly<{ xAxisDirection: 'east', zAxisDirection: 'north' }>
  readonly availableZoomLevels: readonly number[]
  readonly tileSize: number
  readonly mapResourceVersion: string | null
}

export interface UnavailableMapMetadata {
  readonly availability: 'unavailable'
  readonly observedAtUtc: null
  readonly worldId: null
  readonly worldName: null
  readonly extent: null
  readonly axes: null
  readonly availableZoomLevels: null
  readonly tileSize: null
  readonly mapResourceVersion: null
}

export type MapMetadataEnvelope = MapMetadata | UnavailableMapMetadata

export interface MapGameTime {
  readonly availability: 'available' | 'stale'
  readonly day: number
  readonly hour: number
  readonly minute: number
  readonly observedAtUtc: string
}

export interface UnavailableMapGameTime {
  readonly availability: 'unavailable'
  readonly day: null
  readonly hour: null
  readonly minute: null
  readonly observedAtUtc: null
}

export type MapGameTimeEnvelope = MapGameTime | UnavailableMapGameTime

export interface PlayerTrackPoint {
  readonly snapshotId: number
  readonly name: string
  readonly x: number
  readonly y: number
  readonly z: number
  readonly observedAtUtc: string
}

export interface PlayerTrackSegment {
  readonly points: readonly PlayerTrackPoint[]
}

export interface PlayerTrack {
  readonly crossplatformId: string
  readonly segments: readonly PlayerTrackSegment[]
}

export interface PlayerTrackFilters {
  readonly player: string
  readonly fromUtc: string
  readonly toUtc: string
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

function finiteNumber(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value))
    throw new Error('invalid number')
  return value
}

function safeInteger(value: unknown, minimum: number, maximum = Number.MAX_SAFE_INTEGER): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < minimum || value > maximum)
    throw new Error('invalid integer')
  return value
}

function nonBlankString(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '')
    throw new Error('invalid string')
  return value
}

function utcTimestamp(value: unknown): string {
  if (typeof value !== 'string' || !isValidUtcTimestamp(value))
    throw new Error('invalid timestamp')
  return value
}

export function parseMapMetadata(value: unknown): MapMetadataEnvelope {
  try {
    if (!isRecord(value) || !hasExactKeys(value, [
      'availability',
      'observedAtUtc',
      'worldId',
      'worldName',
      'extent',
      'axes',
      'availableZoomLevels',
      'tileSize',
      'mapResourceVersion',
    ])) {
      throw new Error('shape')
    }
    if (value.availability === 'unavailable') {
      if (value.observedAtUtc !== null || value.worldId !== null || value.worldName !== null
        || value.extent !== null || value.axes !== null || value.availableZoomLevels !== null
        || value.tileSize !== null || value.mapResourceVersion !== null) {
        throw new Error('unavailable fields')
      }
      return Object.freeze({
        availability: 'unavailable',
        observedAtUtc: null,
        worldId: null,
        worldName: null,
        extent: null,
        axes: null,
        availableZoomLevels: null,
        tileSize: null,
        mapResourceVersion: null,
      })
    }
    if (value.availability !== 'available' && value.availability !== 'stale')
      throw new Error('availability')
    if (!isRecord(value.extent) || !hasExactKeys(value.extent, ['minimumX', 'minimumZ', 'maximumX', 'maximumZ']))
      throw new Error('extent')
    if (!isRecord(value.axes) || !hasExactKeys(value.axes, ['xAxisDirection', 'zAxisDirection'])
      || value.axes.xAxisDirection !== 'east' || value.axes.zAxisDirection !== 'north') {
      throw new Error('axes')
    }
    if (!Array.isArray(value.availableZoomLevels) || value.availableZoomLevels.length === 0)
      throw new Error('zoom levels')

    const extent = Object.freeze({
      minimumX: finiteNumber(value.extent.minimumX),
      minimumZ: finiteNumber(value.extent.minimumZ),
      maximumX: finiteNumber(value.extent.maximumX),
      maximumZ: finiteNumber(value.extent.maximumZ),
    })
    if (extent.minimumX >= extent.maximumX || extent.minimumZ >= extent.maximumZ)
      throw new Error('extent order')
    const availableZoomLevels = value.availableZoomLevels.map(level => safeInteger(level, 0, 30))
    if (availableZoomLevels.some((level, index) => index > 0 && level <= availableZoomLevels[index - 1]!))
      throw new Error('zoom order')
    if (value.mapResourceVersion !== null && (typeof value.mapResourceVersion !== 'string' || value.mapResourceVersion.trim() === ''))
      throw new Error('resource version')

    return Object.freeze({
      availability: value.availability,
      observedAtUtc: utcTimestamp(value.observedAtUtc),
      worldId: nonBlankString(value.worldId),
      worldName: nonBlankString(value.worldName),
      extent,
      axes: Object.freeze({ xAxisDirection: 'east' as const, zAxisDirection: 'north' as const }),
      availableZoomLevels: Object.freeze(availableZoomLevels),
      tileSize: safeInteger(value.tileSize, 1, 4096),
      mapResourceVersion: value.mapResourceVersion,
    })
  }
  catch {
    throw new Error('Invalid map metadata response')
  }
}

export function parseMapGameTime(value: unknown): MapGameTimeEnvelope {
  try {
    if (!isRecord(value) || !hasExactKeys(value, ['availability', 'day', 'hour', 'minute', 'observedAtUtc']))
      throw new Error('shape')
    if (value.availability === 'unavailable') {
      if (value.day !== null || value.hour !== null || value.minute !== null || value.observedAtUtc !== null)
        throw new Error('unavailable fields')
      return Object.freeze({ availability: 'unavailable', day: null, hour: null, minute: null, observedAtUtc: null })
    }
    if (value.availability !== 'available' && value.availability !== 'stale')
      throw new Error('availability')
    return Object.freeze({
      availability: value.availability,
      day: safeInteger(value.day, 0),
      hour: safeInteger(value.hour, 0, 23),
      minute: safeInteger(value.minute, 0, 59),
      observedAtUtc: utcTimestamp(value.observedAtUtc),
    })
  }
  catch {
    throw new Error('Invalid map game time response')
  }
}

function parseTrackPoint(value: unknown): PlayerTrackPoint {
  if (!isRecord(value) || !hasExactKeys(value, [
    'snapshotId',
    'name',
    'x',
    'y',
    'z',
    'observedAtUtc',
  ])) {
    throw new Error('point')
  }
  return Object.freeze({
    snapshotId: safeInteger(value.snapshotId, 1),
    name: nonBlankString(value.name),
    x: finiteNumber(value.x),
    y: finiteNumber(value.y),
    z: finiteNumber(value.z),
    observedAtUtc: utcTimestamp(value.observedAtUtc),
  })
}

export function parsePlayerTrack(value: unknown): PlayerTrack {
  try {
    if (!isRecord(value) || !hasExactKeys(value, ['crossplatformId', 'segments']) || !Array.isArray(value.segments))
      throw new Error('shape')
    const segments = value.segments.map((segment) => {
      if (!isRecord(segment) || !hasExactKeys(segment, ['points']) || !Array.isArray(segment.points) || segment.points.length === 0)
        throw new Error('segment')
      return Object.freeze({ points: Object.freeze(segment.points.map(parseTrackPoint)) })
    })
    return Object.freeze({
      crossplatformId: nonBlankString(value.crossplatformId),
      segments: Object.freeze(segments),
    })
  }
  catch {
    throw new Error('Invalid player track response')
  }
}

export async function fetchMapMetadata(authorizationHeader: string, signal?: AbortSignal): Promise<MapMetadataEnvelope> {
  const response = await requestJson<unknown>('/api/v1/map/metadata', {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseMapMetadata(response)
}

export async function fetchMapGameTime(authorizationHeader: string, signal?: AbortSignal): Promise<MapGameTimeEnvelope> {
  const response = await requestJson<unknown>('/api/v1/map/game-time', {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseMapGameTime(response)
}

export async function fetchPlayerTrack(
  authorizationHeader: string,
  filters: PlayerTrackFilters,
  signal?: AbortSignal,
): Promise<PlayerTrack> {
  const query = new URLSearchParams({ fromUtc: filters.fromUtc, toUtc: filters.toUtc })
  const response = await requestJson<unknown>(
    `/api/v1/map/players/${encodeURIComponent(filters.player)}/track?${query.toString()}`,
    { headers: { Authorization: authorizationHeader }, signal },
  )
  return parsePlayerTrack(response)
}
