import type {
  ApprovedWorldItem,
  WorldCatalog,
  WorldCollection,
  WorldContainer,
  WorldDrone,
  WorldLandClaim,
  WorldResourcesTransport,
  WorldVehicle,
} from './worldTools.types'

import {
  get,
  integer,
  nullableBoolean,
  nullableFinite,
  nullableInteger,
  nullableText,
  nullableUtc,
  parsePosition,
  record,
  sourceState,
  text,
} from './worldTools.protocol'

import { fetchWorldSummary } from './worldTools.read'

const approvedItemKeys = ['resourceId', 'count', 'quality'] as const
const containerKeys = ['serverId', 'stableIdentity', 'parentStableIdentity', 'position', 'loadState', 'isLocked', 'slotCount', 'usedSlotCount', 'items'] as const
const landClaimKeys = ['serverId', 'stableIdentity', 'position', 'ownerStableIdentity', 'protectionRadius', 'isValid', 'ownerLastLoginUtc'] as const
const vehicleKeys = ['serverId', 'stableIdentity', 'entityTypeResourceId', 'ownerStableIdentity', 'position', 'loadState', 'isLocked', 'fuelPercentage', 'quality', 'container'] as const
const droneKeys = ['serverId', 'stableIdentity', 'entityTypeResourceId', 'ownerStableIdentity', 'position', 'loadState', 'isLocked', 'quality', 'container'] as const
const collectionKeys = ['sourceState', 'observedAtUtc', 'items'] as const
const catalogKeys = ['sourceState', 'catalogVersion', 'observedAtUtc', 'items'] as const

function parseApprovedItem(value: unknown): ApprovedWorldItem {
  const source = record(value, approvedItemKeys)
  return Object.freeze({
    resourceId: text(source.resourceId),
    count: integer(source.count),
    quality: nullableInteger(source.quality),
  })
}

function parseContainer(value: unknown): WorldContainer {
  const source = record(value, containerKeys, 'Invalid world container response')
  if (source.items !== null && !Array.isArray(source.items))
    throw new Error('Invalid world container response')
  return Object.freeze({
    serverId: text(source.serverId),
    stableIdentity: text(source.stableIdentity),
    parentStableIdentity: text(source.parentStableIdentity),
    position: parsePosition(source.position),
    loadState: text(source.loadState),
    isLocked: nullableBoolean(source.isLocked),
    slotCount: nullableInteger(source.slotCount),
    usedSlotCount: nullableInteger(source.usedSlotCount),
    items: source.items === null ? null : Object.freeze(source.items.map(parseApprovedItem)),
  })
}

function parseLandClaim(value: unknown): WorldLandClaim {
  const source = record(value, landClaimKeys)
  return Object.freeze({
    serverId: text(source.serverId),
    stableIdentity: text(source.stableIdentity),
    position: parsePosition(source.position),
    ownerStableIdentity: nullableText(source.ownerStableIdentity),
    protectionRadius: nullableFinite(source.protectionRadius),
    isValid: nullableBoolean(source.isValid),
    ownerLastLoginUtc: nullableUtc(source.ownerLastLoginUtc),
  })
}

function parseVehicle(value: unknown): WorldVehicle {
  const source = record(value, vehicleKeys)
  return Object.freeze({
    serverId: text(source.serverId),
    stableIdentity: text(source.stableIdentity),
    entityTypeResourceId: nullableText(source.entityTypeResourceId),
    ownerStableIdentity: nullableText(source.ownerStableIdentity),
    position: parsePosition(source.position),
    loadState: text(source.loadState),
    isLocked: nullableBoolean(source.isLocked),
    fuelPercentage: nullableFinite(source.fuelPercentage),
    quality: nullableInteger(source.quality),
    container: source.container === null ? null : parseContainer(source.container),
  })
}

function parseDrone(value: unknown): WorldDrone {
  const source = record(value, droneKeys)
  return Object.freeze({
    serverId: text(source.serverId),
    stableIdentity: text(source.stableIdentity),
    entityTypeResourceId: nullableText(source.entityTypeResourceId),
    ownerStableIdentity: nullableText(source.ownerStableIdentity),
    position: parsePosition(source.position),
    loadState: text(source.loadState),
    isLocked: nullableBoolean(source.isLocked),
    quality: nullableInteger(source.quality),
    container: source.container === null ? null : parseContainer(source.container),
  })
}

function parseCollection<T>(value: unknown, parseItem: (item: unknown) => T): WorldCollection<T> {
  const source = record(value, collectionKeys, 'Invalid world collection response')
  if (!Array.isArray(source.items))
    throw new Error('Invalid world collection response')
  return Object.freeze({
    sourceState: sourceState(source.sourceState),
    observedAtUtc: nullableUtc(source.observedAtUtc),
    items: Object.freeze(source.items.map(parseItem)),
  })
}

function parseCatalog(value: unknown): WorldCatalog {
  const source = record(value, catalogKeys, 'Invalid world catalog response')
  if (!Array.isArray(source.items) || source.items.some(item => typeof item !== 'string' || item.trim() === ''))
    throw new Error('Invalid world catalog response')
  return Object.freeze({
    sourceState: sourceState(source.sourceState),
    catalogVersion: nullableText(source.catalogVersion),
    observedAtUtc: nullableUtc(source.observedAtUtc),
    items: Object.freeze([...source.items] as string[]),
  })
}

export function fetchWorldLandClaims(authorizationHeader: string, signal?: AbortSignal) {
  return get('/api/v1/world/land-claims', authorizationHeader, value => parseCollection(value, parseLandClaim), signal)
}

export function fetchWorldVehicles(authorizationHeader: string, signal?: AbortSignal) {
  return get('/api/v1/world/vehicles', authorizationHeader, value => parseCollection(value, parseVehicle), signal)
}

export function fetchWorldDrones(authorizationHeader: string, signal?: AbortSignal) {
  return get('/api/v1/world/drones', authorizationHeader, value => parseCollection(value, parseDrone), signal)
}

export function fetchWorldContainers(authorizationHeader: string, signal?: AbortSignal) {
  return get('/api/v1/world/containers', authorizationHeader, value => parseCollection(value, parseContainer), signal)
}

export function fetchWorldBlockCatalog(authorizationHeader: string, signal?: AbortSignal) {
  return get('/api/v1/world/catalogs/blocks', authorizationHeader, parseCatalog, signal)
}

export function fetchWorldPrefabCatalog(authorizationHeader: string, signal?: AbortSignal) {
  return get('/api/v1/world/catalogs/prefabs', authorizationHeader, parseCatalog, signal)
}

export function fetchWorldEntityTypeCatalog(authorizationHeader: string, signal?: AbortSignal) {
  return get('/api/v1/world/catalogs/entity-types', authorizationHeader, parseCatalog, signal)
}

export const worldResourcesTransport: WorldResourcesTransport = Object.freeze({
  fetchSummary: fetchWorldSummary,
  fetchLandClaims: fetchWorldLandClaims,
  fetchVehicles: fetchWorldVehicles,
  fetchDrones: fetchWorldDrones,
  fetchContainers: fetchWorldContainers,
  fetchBlockCatalog: fetchWorldBlockCatalog,
  fetchPrefabCatalog: fetchWorldPrefabCatalog,
  fetchEntityTypeCatalog: fetchWorldEntityTypeCatalog,
})
