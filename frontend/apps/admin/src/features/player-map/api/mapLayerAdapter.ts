import type {
  MapBusinessFeature,
  MapLayerId,
  MapLayerQuery,
  MapVectorLayerResponse,
} from '../model/useMapVectorLayer'

import { requestJson } from '../../../shared/api/http'
import { isRecord, isValidUtcTimestamp } from '../../players/api/playerSnapshot'

const endpointLayerNames: Record<MapLayerId, string> = {
  'historical-player-locations': 'historical-player-locations',
  'traders': 'traders',
  'claims': 'land-claims',
  'vehicles': 'vehicles',
  'drones': 'drones',
  'animals': 'animals',
  'hostiles': 'hostiles',
}

function text(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null
}

function number(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

function boolean(value: unknown): boolean | null {
  return typeof value === 'boolean' ? value : null
}

function timestamp(value: unknown): string | null {
  return typeof value === 'string' && isValidUtcTimestamp(value) ? value : null
}

function position(value: Record<string, unknown>): { x: number, z: number } {
  const nested = isRecord(value.position) ? value.position : value
  const x = number(nested.x)
  const z = number(nested.z)
  if (x === null || z === null)
    throw new Error('Invalid map feature position')
  return { x, z }
}

function loadState(value: unknown): 'loaded' | 'unloaded' {
  if (typeof value !== 'string')
    throw new Error('Invalid map feature load state')
  const normalized = value.toLowerCase()
  if (normalized !== 'loaded' && normalized !== 'unloaded')
    throw new Error('Invalid map feature load state')
  return normalized
}

function observedAt(value: Record<string, unknown>, envelopeObservedAt: string | null): string {
  const result = timestamp(value.observedAtUtc) ?? envelopeObservedAt
  if (result === null)
    throw new Error('Invalid map feature observation time')
  return result
}

function normalizeFeature(
  layerId: MapLayerId,
  value: unknown,
  envelopeObservedAt: string | null,
): MapBusinessFeature {
  if (!isRecord(value))
    throw new Error('Invalid map feature')
  const coordinates = position(value)

  if (layerId === 'historical-player-locations') {
    const playerCombinedId = text(value.playerCombinedId) ?? text(value.crossplatformId)
    const name = text(value.name) ?? text(value.displayName)
    const snapshotId = number(value.snapshotId)
    if (playerCombinedId === null || name === null)
      throw new Error('Invalid historical player map feature')
    return Object.freeze({
      id: text(value.id) ?? `history:${snapshotId ?? playerCombinedId}`,
      kind: 'historical-player',
      ...coordinates,
      observedAtUtc: observedAt(value, envelopeObservedAt),
      name,
      playerCombinedId,
    })
  }

  const id = text(value.id)
  if (id === null)
    throw new Error('Invalid map feature identifier')
  const common = {
    id,
    ...coordinates,
    observedAtUtc: observedAt(value, envelopeObservedAt),
  }

  switch (layerId) {
    case 'traders':
      return Object.freeze({
        ...common,
        kind: 'trader' as const,
        name: text(value.name),
        prefab: text(value.prefab),
        prefabBounds: isRecord(value.prefabBounds) ? Object.freeze({ ...value.prefabBounds }) : null,
        protectionRadius: number(value.protectionRadius),
        isOpen: boolean(value.isOpen),
      })
    case 'claims':
      return Object.freeze({
        ...common,
        kind: 'claim' as const,
        ownerCrossplatformId: text(value.ownerCrossplatformId),
        protectionRadius: number(value.protectionRadius),
        isValid: boolean(value.isValid),
        ownerLastLoginUtc: timestamp(value.ownerLastLoginUtc),
      })
    case 'vehicles':
      return Object.freeze({
        ...common,
        kind: 'vehicle' as const,
        vehicleType: text(value.vehicleType),
        ownerCrossplatformId: text(value.ownerCrossplatformId),
        loadState: loadState(value.loadState),
        fuelPercentage: number(value.fuelPercentage),
        quality: number(value.quality),
        isLocked: boolean(value.isLocked),
        storageItemCount: number(value.storageItemCount),
      })
    case 'drones':
      return Object.freeze({
        ...common,
        kind: 'drone' as const,
        ownerCrossplatformId: text(value.ownerCrossplatformId),
        loadState: loadState(value.loadState),
      })
    case 'animals':
    case 'hostiles': {
      const entityType = text(value.entityType)
      if (entityType === null)
        throw new Error('Invalid transient entity map feature')
      return Object.freeze({
        ...common,
        kind: layerId === 'animals' ? 'animal' as const : 'hostile' as const,
        entityType,
      })
    }
  }
}

function responseItems(layerId: MapLayerId, value: Record<string, unknown>): readonly unknown[] {
  if (Array.isArray(value.items))
    return value.items
  if (Array.isArray(value.features))
    return value.features
  if (layerId === 'historical-player-locations' && Array.isArray(value.locations))
    return value.locations
  throw new Error('Invalid map layer response')
}

export function mapLayerEndpointPath(layerId: MapLayerId, query: MapLayerQuery): string {
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
  return `/api/v1/map/layers/${endpointLayerNames[layerId]}?${parameters.toString()}`
}

export async function fetchCurrentMapLayer(
  layerId: MapLayerId,
  authorizationHeader: string,
  query: MapLayerQuery,
  signal: AbortSignal,
): Promise<MapVectorLayerResponse> {
  const value = await requestJson<unknown>(mapLayerEndpointPath(layerId, query), {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  if (!isRecord(value))
    throw new Error('Invalid map layer response')
  if (value.availability === 'unavailable')
    throw new Error('Map layer unavailable')
  const envelopeObservedAt = timestamp(value.observedAtUtc)
  const items = responseItems(layerId, value).map(item => normalizeFeature(layerId, item, envelopeObservedAt))
  return Object.freeze({
    observedAtUtc: envelopeObservedAt ?? items[0]?.observedAtUtc ?? '',
    items: Object.freeze(items),
  })
}
