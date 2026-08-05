import { requestJson } from '../../../shared/api/http'
import { isRecord, isValidUtcTimestamp } from '../../players/api/playerSnapshot'

export const MAP_LAYER_IDS = [
  'historical-player-locations',
  'traders',
  'claims',
  'vehicles',
  'drones',
  'animals',
  'hostiles',
] as const

export type MapLayerId = typeof MAP_LAYER_IDS[number]
export type MapFeatureKind = 'historical-player' | 'trader' | 'claim' | 'vehicle' | 'drone' | 'animal' | 'hostile'

interface MapFeatureBase {
  readonly id: string
  readonly kind: MapFeatureKind
  readonly x: number
  readonly z: number
  readonly observedAtUtc: string
}

export interface HistoricalPlayerMapFeature extends MapFeatureBase {
  readonly kind: 'historical-player'
  readonly name: string
  readonly playerCombinedId: string
}

export interface TraderMapFeature extends MapFeatureBase {
  readonly kind: 'trader'
  readonly name: string | null
  readonly prefab: string | null
  readonly protectionRadius: number | null
  readonly isOpen: boolean | null
}

export interface ClaimMapFeature extends MapFeatureBase {
  readonly kind: 'claim'
  readonly ownerCrossplatformId: string | null
  readonly protectionRadius: number | null
  readonly isValid: boolean | null
  readonly ownerLastLoginUtc: string | null
}

export interface VehicleMapFeature extends MapFeatureBase {
  readonly kind: 'vehicle'
  readonly vehicleType: string | null
  readonly ownerCrossplatformId: string | null
  readonly loadState: 'loaded' | 'unloaded'
  readonly fuelPercentage: number | null
  readonly quality: number | null
  readonly isLocked: boolean | null
  readonly storageItemCount: number | null
}

export interface DroneMapFeature extends MapFeatureBase {
  readonly kind: 'drone'
  readonly ownerCrossplatformId: string | null
  readonly loadState: 'loaded' | 'unloaded'
}

export interface TransientEntityMapFeature extends MapFeatureBase {
  readonly kind: 'animal' | 'hostile'
  readonly entityType: string
}

export type MapBusinessFeature
  = | HistoricalPlayerMapFeature
    | TraderMapFeature
    | ClaimMapFeature
    | VehicleMapFeature
    | DroneMapFeature
    | TransientEntityMapFeature

export interface MapLayerQuery {
  readonly worldId: string
  readonly extent: readonly [number, number, number, number]
  readonly zoom: number
  readonly limit: number
}

export interface MapVectorLayerResponse {
  readonly observedAtUtc: string
  readonly items: readonly MapBusinessFeature[]
}

function exactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

function nonBlankString(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '')
    throw new Error('string')
  return value
}

function nullableString(value: unknown): string | null {
  return value === null ? null : nonBlankString(value)
}

function finiteNumber(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value))
    throw new Error('number')
  return value
}

function nullableNumber(value: unknown, minimum = -Infinity, maximum = Infinity): number | null {
  if (value === null)
    return null
  const parsed = finiteNumber(value)
  if (parsed < minimum || parsed > maximum)
    throw new Error('number range')
  return parsed
}

function nullableInteger(value: unknown): number | null {
  if (value === null)
    return null
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0)
    throw new Error('integer')
  return value
}

function nullableBoolean(value: unknown): boolean | null {
  if (value !== null && typeof value !== 'boolean')
    throw new Error('boolean')
  return value
}

function timestamp(value: unknown): string {
  if (typeof value !== 'string' || !isValidUtcTimestamp(value))
    throw new Error('timestamp')
  return value
}

function nullableTimestamp(value: unknown): string | null {
  return value === null ? null : timestamp(value)
}

function parseBase<Kind extends MapFeatureKind>(value: Record<string, unknown>, kind: Kind) {
  if (value.kind !== kind)
    throw new Error('kind')
  return {
    id: nonBlankString(value.id),
    kind,
    x: finiteNumber(value.x),
    z: finiteNumber(value.z),
    observedAtUtc: timestamp(value.observedAtUtc),
  }
}

function parseItem(layerId: MapLayerId, value: unknown): MapBusinessFeature {
  if (!isRecord(value))
    throw new Error('item')
  switch (layerId) {
    case 'historical-player-locations': {
      if (!exactKeys(value, ['id', 'kind', 'x', 'z', 'observedAtUtc', 'name', 'playerCombinedId']))
        throw new Error('shape')
      return Object.freeze({
        ...parseBase(value, 'historical-player'),
        name: nonBlankString(value.name),
        playerCombinedId: nonBlankString(value.playerCombinedId),
      })
    }
    case 'traders': {
      if (!exactKeys(value, ['id', 'kind', 'x', 'z', 'observedAtUtc', 'name', 'prefab', 'protectionRadius', 'isOpen']))
        throw new Error('shape')
      return Object.freeze({
        ...parseBase(value, 'trader'),
        name: nullableString(value.name),
        prefab: nullableString(value.prefab),
        protectionRadius: nullableNumber(value.protectionRadius, 0),
        isOpen: nullableBoolean(value.isOpen),
      })
    }
    case 'claims': {
      if (!exactKeys(value, ['id', 'kind', 'x', 'z', 'observedAtUtc', 'ownerCrossplatformId', 'protectionRadius', 'isValid', 'ownerLastLoginUtc']))
        throw new Error('shape')
      return Object.freeze({
        ...parseBase(value, 'claim'),
        ownerCrossplatformId: nullableString(value.ownerCrossplatformId),
        protectionRadius: nullableNumber(value.protectionRadius, 0),
        isValid: nullableBoolean(value.isValid),
        ownerLastLoginUtc: nullableTimestamp(value.ownerLastLoginUtc),
      })
    }
    case 'vehicles': {
      if (!exactKeys(value, ['id', 'kind', 'x', 'z', 'observedAtUtc', 'vehicleType', 'ownerCrossplatformId', 'loadState', 'fuelPercentage', 'quality', 'isLocked', 'storageItemCount']))
        throw new Error('shape')
      if (value.loadState !== 'loaded' && value.loadState !== 'unloaded')
        throw new Error('load state')
      return Object.freeze({
        ...parseBase(value, 'vehicle'),
        vehicleType: nullableString(value.vehicleType),
        ownerCrossplatformId: nullableString(value.ownerCrossplatformId),
        loadState: value.loadState,
        fuelPercentage: nullableNumber(value.fuelPercentage, 0, 100),
        quality: nullableInteger(value.quality),
        isLocked: nullableBoolean(value.isLocked),
        storageItemCount: nullableInteger(value.storageItemCount),
      })
    }
    case 'drones': {
      if (!exactKeys(value, ['id', 'kind', 'x', 'z', 'observedAtUtc', 'ownerCrossplatformId', 'loadState']))
        throw new Error('shape')
      if (value.loadState !== 'loaded' && value.loadState !== 'unloaded')
        throw new Error('load state')
      return Object.freeze({
        ...parseBase(value, 'drone'),
        ownerCrossplatformId: nullableString(value.ownerCrossplatformId),
        loadState: value.loadState,
      })
    }
    case 'animals':
    case 'hostiles': {
      const kind = layerId === 'animals' ? 'animal' : 'hostile'
      if (!exactKeys(value, ['id', 'kind', 'x', 'z', 'observedAtUtc', 'entityType']))
        throw new Error('shape')
      return Object.freeze({
        ...parseBase(value, kind),
        entityType: nonBlankString(value.entityType),
      })
    }
  }
}

export function mapLayerPath(layerId: MapLayerId, query: MapLayerQuery): string {
  const [minimumX, minimumZ, maximumX, maximumZ] = query.extent
  const parameters = new URLSearchParams({
    worldId: query.worldId,
    minimumX: String(minimumX),
    minimumZ: String(minimumZ),
    maximumX: String(maximumX),
    maximumZ: String(maximumZ),
    zoom: String(query.zoom),
    limit: String(query.limit),
  })
  return `/api/v1/map/layers/${layerId}?${parameters.toString()}`
}

export function parseMapVectorLayerResponse(layerId: MapLayerId, value: unknown): MapVectorLayerResponse {
  try {
    if (!isRecord(value) || !exactKeys(value, ['observedAtUtc', 'items']) || !Array.isArray(value.items))
      throw new Error('shape')
    return Object.freeze({
      observedAtUtc: timestamp(value.observedAtUtc),
      items: Object.freeze(value.items.map(item => parseItem(layerId, item))),
    })
  }
  catch {
    throw new Error(`Invalid ${layerId} map layer response`)
  }
}

export async function fetchMapVectorLayer(
  layerId: MapLayerId,
  authorizationHeader: string,
  query: MapLayerQuery,
  signal: AbortSignal,
): Promise<MapVectorLayerResponse> {
  const value = await requestJson<unknown>(mapLayerPath(layerId, query), {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseMapVectorLayerResponse(layerId, value)
}
