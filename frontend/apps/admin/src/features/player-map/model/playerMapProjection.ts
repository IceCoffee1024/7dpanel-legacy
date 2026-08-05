import type { MapMetadata, PlayerTrack, PlayerTrackFilters } from '../api/playerMap'

import { isValidUtcTimestamp } from '../api/playerMap'
import { toMapCoordinate } from './mapProjection'

export interface PlayerMapFilters {
  readonly player: string | null
  readonly fromUtc: string | null
  readonly toUtc: string | null
}

export type PlayerMapPageState = 'loading' | 'ready' | 'empty' | 'partial' | 'stale' | 'forbidden' | 'failed'

export function mapPlayerMapPageState(
  metadata: MapMetadata | null,
  onlinePlayerCount: number,
  historicalPlayerCount: number,
  failedCount: number,
  hadPreviousData: boolean,
): PlayerMapPageState {
  if (metadata === null)
    return 'failed'
  if (failedCount > 0)
    return hadPreviousData ? 'stale' : 'partial'
  return onlinePlayerCount === 0 && historicalPlayerCount === 0 ? 'empty' : 'ready'
}

export function restorePlayerMapFilters(query: URLSearchParams): PlayerMapFilters {
  const player = query.get('player')?.trim() || null
  const fromUtc = query.get('from')
  const toUtc = query.get('to')
  const validRange = fromUtc !== null && toUtc !== null
    && isValidUtcTimestamp(fromUtc) && isValidUtcTimestamp(toUtc)
    && Date.parse(fromUtc) <= Date.parse(toUtc)
  return Object.freeze({
    player,
    fromUtc: validRange ? fromUtc : null,
    toUtc: validRange ? toUtc : null,
  })
}

export function restorePlayerMapObservation(query: URLSearchParams): number | null {
  const value = Number(query.get('observation'))
  return Number.isSafeInteger(value) && value > 0 ? value : null
}

export function playerTrackQueryKey(filters: PlayerTrackFilters): string {
  return `${filters.player}\n${filters.fromUtc}\n${filters.toUtc}`
}

export function playerMapWorldIdentity(value: MapMetadata): string {
  const { minimumX, minimumZ, maximumX, maximumZ } = value.extent
  return `${value.worldId}\n${minimumX}\n${minimumZ}\n${maximumX}\n${maximumZ}`
}

export function playerTrackFitExtent(track: PlayerTrack): readonly [number, number, number, number] | null {
  const coordinates = track.segments.flatMap(segment => segment.points.map(toMapCoordinate))
  if (coordinates.length === 0)
    return null
  const xs = coordinates.map(coordinate => coordinate[0] ?? 0)
  const ys = coordinates.map(coordinate => coordinate[1] ?? 0)
  let minX = Math.min(...xs)
  let minY = Math.min(...ys)
  let maxX = Math.max(...xs)
  let maxY = Math.max(...ys)
  if (minX === maxX) {
    minX -= 1
    maxX += 1
  }
  if (minY === maxY) {
    minY -= 1
    maxY += 1
  }
  return Object.freeze([minX, minY, maxX, maxY])
}
