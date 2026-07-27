import { requestJson } from '../../../shared/api/http'

export type WorldSourceState = 'Success' | 'Partial' | 'Stale' | 'Unavailable'
export type WorldOperationStatus
  = | 'Queued'
    | 'Running'
    | 'Succeeded'
    | 'Failed'
    | 'Cancelled'
    | 'Interrupted'
    | 'ResultUnknown'
    | 'RollbackFailed'

export type WorldOperationKind
  = | 'DeleteLandClaim'
    | 'MoveOnlinePlayer'
    | 'MoveEntity'
    | 'RefreshMapResources'
    | 'RenderExploredMap'
    | 'RenderFullMap'
    | 'CopyRegion'
    | 'FillRegion'
    | 'ClearRegion'
    | 'PasteRegion'
    | 'SetBlock'
    | 'PlacePrefab'
    | 'RemovePrefab'
    | 'SpawnEntity'
    | 'DeleteEntity'
    | 'CleanupEntities'
    | 'ReloadBlocks'
    | 'ReloadItems'
    | 'ReloadEntityClasses'
    | 'ReloadPrefabs'
    | 'CollectGarbage'
    | 'UndoChangeSet'

export interface WorldPosition {
  x: number
  y: number
  z: number
}

export interface WorldExtent {
  minimumX: number
  minimumZ: number
  maximumX: number
  maximumZ: number
}

export interface WorldSummary {
  sourceState: WorldSourceState
  worldId: string | null
  worldVersion: string | null
  seed: string | null
  width: number | null
  height: number | null
  gameVersion: string | null
  mapResourceVersion: string | null
  availableExtent: WorldExtent | null
  observedAtUtc: string | null
}

export interface ApprovedWorldItem {
  resourceId: string
  count: number
  quality: number | null
}

export interface WorldContainer {
  serverId: string
  stableIdentity: string
  parentStableIdentity: string
  position: WorldPosition
  loadState: string
  isLocked: boolean | null
  slotCount: number | null
  usedSlotCount: number | null
  items: readonly ApprovedWorldItem[] | null
}

export interface WorldLandClaim {
  serverId: string
  stableIdentity: string
  position: WorldPosition
  ownerStableIdentity: string | null
  protectionRadius: number | null
  isValid: boolean | null
  ownerLastLoginUtc: string | null
}

export interface WorldVehicle {
  serverId: string
  stableIdentity: string
  entityTypeResourceId: string | null
  ownerStableIdentity: string | null
  position: WorldPosition
  loadState: string
  isLocked: boolean | null
  fuelPercentage: number | null
  quality: number | null
  container: WorldContainer | null
}

export interface WorldDrone {
  serverId: string
  stableIdentity: string
  entityTypeResourceId: string | null
  ownerStableIdentity: string | null
  position: WorldPosition
  loadState: string
  isLocked: boolean | null
  quality: number | null
  container: WorldContainer | null
}

export interface WorldCollection<T> {
  sourceState: WorldSourceState
  observedAtUtc: string | null
  items: readonly T[]
}

export interface WorldCatalog {
  sourceState: WorldSourceState
  catalogVersion: string | null
  observedAtUtc: string | null
  items: readonly string[]
}

export interface WorldCoordinateRequest {
  x: number
  y: number
  z: number
}

export interface WorldRegionRequest {
  first: WorldCoordinateRequest
  second: WorldCoordinateRequest
}

export interface WorldMapBoundsRequest {
  minimumX: number
  minimumZ: number
  maximumX: number
  maximumZ: number
}

export interface ConfirmedWorldRequest {
  worldId: string
  worldVersion: string
  mapResourceVersion: string | null
  confirmed: true
}

export interface StrongConfirmedWorldRequest extends ConfirmedWorldRequest {
  strongConfirmed: true
}

export interface DeleteLandClaimRequest extends ConfirmedWorldRequest {
  claimId: string
  ownerStableIdentity: string
  center: WorldCoordinateRequest
  protectionRadius: number
}

export interface MoveOnlinePlayerRequest extends ConfirmedWorldRequest {
  crossplatformId: string
  entityId: number
  onlineObservedAtUtc: string
  destination: WorldCoordinateRequest
}

export interface MoveWorldEntityRequest extends ConfirmedWorldRequest {
  targetId: string
  entityId: number
  entityTypeResourceId: string
  ownerStableIdentity: string | null
  observedPosition: WorldCoordinateRequest
  destination: WorldCoordinateRequest
}

export interface CopyRegionRequest extends ConfirmedWorldRequest { region: WorldRegionRequest }
export interface FillRegionRequest extends StrongConfirmedWorldRequest { region: WorldRegionRequest, catalogVersion: string, blockInternalName: string }
export interface ClearRegionRequest extends StrongConfirmedWorldRequest { region: WorldRegionRequest }
export interface PasteRegionRequest extends StrongConfirmedWorldRequest { region: WorldRegionRequest, sourceChangeSetId: string }

export interface SetBlockRequest extends StrongConfirmedWorldRequest {
  catalogVersion: string
  coordinate: WorldCoordinateRequest
  blockInternalName: string
  rotation: number
  shape: 'Default' | 'Cube' | 'Ramp' | 'Wedge' | null
}

export interface PlacePrefabRequest extends StrongConfirmedWorldRequest {
  catalogVersion: string
  prefabResourceId: string
  anchor: WorldCoordinateRequest
  rotation: number
  knownBounds: WorldRegionRequest
}

export interface RemovePrefabRequest extends StrongConfirmedWorldRequest {
  catalogVersion: string
  prefabResourceId: string
  prefabInstanceId: string
  anchor: WorldCoordinateRequest
  rotation: number
  knownBounds: WorldRegionRequest
}

export interface SpawnWorldEntityRequest extends StrongConfirmedWorldRequest {
  catalogVersion: string
  entityTypeResourceId: string
  quantity: number
  center: WorldCoordinateRequest
  radius: number
}

export interface DeleteWorldEntityRequest extends StrongConfirmedWorldRequest {
  catalogVersion: string
  targetId: string
  entityId: number
  entityTypeResourceId: string
  ownerStableIdentity: string | null
  observedPosition: WorldCoordinateRequest
}

export interface CleanupWorldEntitiesRequest extends StrongConfirmedWorldRequest {
  category: 'Animal' | 'Hostile' | 'Vehicle' | 'Drone' | 'DroppedItem'
  center: WorldCoordinateRequest
  radius: number
  maximumCount: number
}

export interface ReloadWorldResourceRequest extends StrongConfirmedWorldRequest {
  resourceKind: 'Blocks' | 'Items' | 'EntityClasses' | 'Prefabs'
}

export type CollectGameGarbageRequest = ConfirmedWorldRequest

export interface UndoWorldChangeSetRequest extends StrongConfirmedWorldRequest {
  sourceOperationId: string
  changeSetId: string
  currentRegionHash: string
}

export interface RefreshMapResourcesRequest extends ConfirmedWorldRequest { bounds: WorldMapBoundsRequest | null }
export interface RenderExploredMapRequest extends ConfirmedWorldRequest { bounds: WorldMapBoundsRequest | null }
export interface RenderFullMapRequest extends StrongConfirmedWorldRequest { bounds: WorldMapBoundsRequest | null }

export type WorldOperationSubmission
  = | { type: 'deleteLandClaim', request: DeleteLandClaimRequest }
    | { type: 'moveOnlinePlayer', request: MoveOnlinePlayerRequest }
    | { type: 'moveEntity', request: MoveWorldEntityRequest }
    | { type: 'copyRegion', request: CopyRegionRequest }
    | { type: 'fillRegion', request: FillRegionRequest }
    | { type: 'clearRegion', request: ClearRegionRequest }
    | { type: 'pasteRegion', request: PasteRegionRequest }
    | { type: 'setBlock', request: SetBlockRequest }
    | { type: 'placePrefab', request: PlacePrefabRequest }
    | { type: 'removePrefab', request: RemovePrefabRequest }
    | { type: 'spawnEntity', request: SpawnWorldEntityRequest }
    | { type: 'deleteEntity', request: DeleteWorldEntityRequest }
    | { type: 'cleanupEntities', request: CleanupWorldEntitiesRequest }
    | { type: 'reloadResource', request: ReloadWorldResourceRequest }
    | { type: 'collectGarbage', request: CollectGameGarbageRequest }
    | { type: 'undoChangeSet', request: UndoWorldChangeSetRequest }
    | { type: 'refreshMapResources', request: RefreshMapResourcesRequest }
    | { type: 'renderExploredMap', request: RenderExploredMapRequest }
    | { type: 'renderFullMap', request: RenderFullMapRequest }

export interface WorldOperationReceipt {
  operationId: string
  jobId: string
  status: WorldOperationStatus
  correlationId: string
  createdAtUtc: string
}

export interface WorldOperationProgress {
  current: number | null
  total: number | null
}

export interface WorldOperationRecord {
  operationId: string
  jobId: string
  kind: WorldOperationKind
  worldId: string
  worldVersion: string
  mapResourceVersion: string | null
  correlationId: string
  confirmationSummary: string
  isReversible: boolean
  changeSetId: string | null
  status: WorldOperationStatus
  progress: WorldOperationProgress | null
  errorCode: string | null
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
}

export interface WorldResourcesTransport {
  fetchSummary: (authorizationHeader: string, signal?: AbortSignal) => Promise<WorldSummary>
  fetchLandClaims: (authorizationHeader: string, signal?: AbortSignal) => Promise<WorldCollection<WorldLandClaim>>
  fetchVehicles: (authorizationHeader: string, signal?: AbortSignal) => Promise<WorldCollection<WorldVehicle>>
  fetchDrones: (authorizationHeader: string, signal?: AbortSignal) => Promise<WorldCollection<WorldDrone>>
  fetchContainers: (authorizationHeader: string, signal?: AbortSignal) => Promise<WorldCollection<WorldContainer>>
  fetchBlockCatalog: (authorizationHeader: string, signal?: AbortSignal) => Promise<WorldCatalog>
  fetchPrefabCatalog: (authorizationHeader: string, signal?: AbortSignal) => Promise<WorldCatalog>
  fetchEntityTypeCatalog: (authorizationHeader: string, signal?: AbortSignal) => Promise<WorldCatalog>
}

const sourceStates = new Set(['Available', 'Success', 'Partial', 'Stale', 'Unavailable'])
const operationStatuses = new Set<WorldOperationStatus>([
  'Queued', 'Running', 'Succeeded', 'Failed', 'Cancelled', 'Interrupted', 'ResultUnknown', 'RollbackFailed',
])
const operationKinds = new Set<WorldOperationKind>([
  'DeleteLandClaim', 'MoveOnlinePlayer', 'MoveEntity', 'RefreshMapResources', 'RenderExploredMap', 'RenderFullMap',
  'CopyRegion', 'FillRegion', 'ClearRegion', 'PasteRegion', 'SetBlock', 'PlacePrefab', 'RemovePrefab', 'SpawnEntity',
  'DeleteEntity', 'CleanupEntities', 'ReloadBlocks', 'ReloadItems', 'ReloadEntityClasses', 'ReloadPrefabs',
  'CollectGarbage', 'UndoChangeSet',
])

function record(value: unknown, message = 'Invalid world tools response'): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new Error(message)
  return value as Record<string, unknown>
}

function text(value: unknown, message = 'Invalid world tools response'): string {
  if (typeof value !== 'string' || value.trim() === '')
    throw new Error(message)
  return value
}

function nullableText(value: unknown): string | null {
  return value === null ? null : text(value)
}

function finite(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value))
    throw new Error('Invalid world tools response')
  return value
}

function nullableFinite(value: unknown): number | null {
  return value === null ? null : finite(value)
}

function integer(value: unknown): number {
  if (!Number.isSafeInteger(value))
    throw new Error('Invalid world tools response')
  return value as number
}

function nullableInteger(value: unknown): number | null {
  return value === null ? null : integer(value)
}

function nullableBoolean(value: unknown): boolean | null {
  if (value !== null && typeof value !== 'boolean')
    throw new Error('Invalid world tools response')
  return value as boolean | null
}

function utc(value: unknown): string {
  const result = text(value)
  if (!Number.isFinite(Date.parse(result)))
    throw new Error('Invalid world tools response')
  return result
}

function nullableUtc(value: unknown): string | null {
  return value === null ? null : utc(value)
}

function sourceState(value: unknown): WorldSourceState {
  if (typeof value !== 'string' || !sourceStates.has(value))
    throw new Error('Invalid world source state')
  return value === 'Available' ? 'Success' : value as WorldSourceState
}

function parsePosition(value: unknown): WorldPosition {
  const source = record(value)
  return Object.freeze({ x: finite(source.x), y: finite(source.y), z: finite(source.z) })
}

function parseExtent(value: unknown): WorldExtent {
  const source = record(value)
  return Object.freeze({
    minimumX: finite(source.minimumX),
    minimumZ: finite(source.minimumZ),
    maximumX: finite(source.maximumX),
    maximumZ: finite(source.maximumZ),
  })
}

export function parseWorldSummary(value: unknown): WorldSummary {
  const source = record(value)
  return Object.freeze({
    sourceState: sourceState(source.sourceState),
    worldId: nullableText(source.worldId),
    worldVersion: nullableText(source.worldVersion),
    seed: nullableText(source.seed),
    width: nullableInteger(source.width),
    height: nullableInteger(source.height),
    gameVersion: nullableText(source.gameVersion),
    mapResourceVersion: nullableText(source.mapResourceVersion),
    availableExtent: source.availableExtent === null ? null : parseExtent(source.availableExtent),
    observedAtUtc: nullableUtc(source.observedAtUtc),
  })
}

function parseApprovedItem(value: unknown): ApprovedWorldItem {
  const source = record(value)
  return Object.freeze({
    resourceId: text(source.resourceId),
    count: integer(source.count),
    quality: nullableInteger(source.quality),
  })
}

function parseContainer(value: unknown): WorldContainer {
  const source = record(value)
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
  const source = record(value)
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
  const source = record(value)
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
  const source = record(value)
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
  const source = record(value)
  if (!Array.isArray(source.items))
    throw new Error('Invalid world collection response')
  return Object.freeze({
    sourceState: sourceState(source.sourceState),
    observedAtUtc: nullableUtc(source.observedAtUtc),
    items: Object.freeze(source.items.map(parseItem)),
  })
}

function parseCatalog(value: unknown): WorldCatalog {
  const source = record(value)
  if (!Array.isArray(source.items) || source.items.some(item => typeof item !== 'string' || item.trim() === ''))
    throw new Error('Invalid world catalog response')
  return Object.freeze({
    sourceState: sourceState(source.sourceState),
    catalogVersion: nullableText(source.catalogVersion),
    observedAtUtc: nullableUtc(source.observedAtUtc),
    items: Object.freeze([...source.items] as string[]),
  })
}

function parseOperationStatus(value: unknown): WorldOperationStatus {
  if (typeof value !== 'string' || !operationStatuses.has(value as WorldOperationStatus))
    throw new Error('Invalid world operation status')
  return value as WorldOperationStatus
}

export function parseWorldOperationReceipt(value: unknown): WorldOperationReceipt {
  const source = record(value)
  return Object.freeze({
    operationId: text(source.operationId),
    jobId: text(source.jobId),
    status: parseOperationStatus(source.status),
    correlationId: text(source.correlationId),
    createdAtUtc: utc(source.createdAtUtc),
  })
}

export function parseWorldOperation(value: unknown): WorldOperationRecord {
  const source = record(value)
  if (typeof source.kind !== 'string' || !operationKinds.has(source.kind as WorldOperationKind))
    throw new Error('Invalid world operation kind')
  let progress: WorldOperationProgress | null = null
  if (source.progress !== null) {
    const progressSource = record(source.progress)
    progress = Object.freeze({
      current: nullableInteger(progressSource.current),
      total: nullableInteger(progressSource.total),
    })
  }
  if (typeof source.isReversible !== 'boolean')
    throw new Error('Invalid world operation response')
  return Object.freeze({
    operationId: text(source.operationId),
    jobId: text(source.jobId),
    kind: source.kind as WorldOperationKind,
    worldId: text(source.worldId),
    worldVersion: text(source.worldVersion),
    mapResourceVersion: nullableText(source.mapResourceVersion),
    correlationId: text(source.correlationId),
    confirmationSummary: text(source.confirmationSummary),
    isReversible: source.isReversible,
    changeSetId: nullableText(source.changeSetId),
    status: parseOperationStatus(source.status),
    progress,
    errorCode: nullableText(source.errorCode),
    createdAtUtc: utc(source.createdAtUtc),
    startedAtUtc: nullableUtc(source.startedAtUtc),
    completedAtUtc: nullableUtc(source.completedAtUtc),
  })
}

function get<T>(path: string, authorizationHeader: string, parser: (value: unknown) => T, signal?: AbortSignal): Promise<T> {
  return requestJson<unknown>(path, { headers: { Authorization: authorizationHeader }, signal }).then(parser)
}

export const fetchWorldSummary = (authorizationHeader: string, signal?: AbortSignal) =>
  get('/api/v1/world/summary', authorizationHeader, parseWorldSummary, signal)
export const fetchWorldLandClaims = (authorizationHeader: string, signal?: AbortSignal) =>
  get('/api/v1/world/land-claims', authorizationHeader, value => parseCollection(value, parseLandClaim), signal)
export const fetchWorldVehicles = (authorizationHeader: string, signal?: AbortSignal) =>
  get('/api/v1/world/vehicles', authorizationHeader, value => parseCollection(value, parseVehicle), signal)
export const fetchWorldDrones = (authorizationHeader: string, signal?: AbortSignal) =>
  get('/api/v1/world/drones', authorizationHeader, value => parseCollection(value, parseDrone), signal)
export const fetchWorldContainers = (authorizationHeader: string, signal?: AbortSignal) =>
  get('/api/v1/world/containers', authorizationHeader, value => parseCollection(value, parseContainer), signal)
export const fetchWorldBlockCatalog = (authorizationHeader: string, signal?: AbortSignal) =>
  get('/api/v1/world/catalogs/blocks', authorizationHeader, parseCatalog, signal)
export const fetchWorldPrefabCatalog = (authorizationHeader: string, signal?: AbortSignal) =>
  get('/api/v1/world/catalogs/prefabs', authorizationHeader, parseCatalog, signal)
export const fetchWorldEntityTypeCatalog = (authorizationHeader: string, signal?: AbortSignal) =>
  get('/api/v1/world/catalogs/entity-types', authorizationHeader, parseCatalog, signal)

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

async function postOperation<TRequest>(
  path: string,
  authorizationHeader: string,
  request: TRequest,
  signal?: AbortSignal,
): Promise<WorldOperationReceipt> {
  const response = await requestJson<unknown>(path, {
    method: 'POST',
    headers: { Authorization: authorizationHeader, 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
    expectedStatus: 202,
    signal,
  })
  return parseWorldOperationReceipt(response)
}

export const deleteLandClaim = (authorizationHeader: string, request: DeleteLandClaimRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/land-claims/delete', authorizationHeader, request, signal)
export const moveOnlinePlayer = (authorizationHeader: string, request: MoveOnlinePlayerRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/players/move', authorizationHeader, request, signal)
export const moveWorldEntity = (authorizationHeader: string, request: MoveWorldEntityRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/entities/move', authorizationHeader, request, signal)
export const copyWorldRegion = (authorizationHeader: string, request: CopyRegionRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/regions/copy', authorizationHeader, request, signal)
export const fillWorldRegion = (authorizationHeader: string, request: FillRegionRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/regions/fill', authorizationHeader, request, signal)
export const clearWorldRegion = (authorizationHeader: string, request: ClearRegionRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/regions/clear', authorizationHeader, request, signal)
export const pasteWorldRegion = (authorizationHeader: string, request: PasteRegionRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/regions/paste', authorizationHeader, request, signal)
export const setWorldBlock = (authorizationHeader: string, request: SetBlockRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/blocks/set', authorizationHeader, request, signal)
export const placeWorldPrefab = (authorizationHeader: string, request: PlacePrefabRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/prefabs/place', authorizationHeader, request, signal)
export const removeWorldPrefab = (authorizationHeader: string, request: RemovePrefabRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/prefabs/remove', authorizationHeader, request, signal)
export const spawnWorldEntity = (authorizationHeader: string, request: SpawnWorldEntityRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/entities/spawn', authorizationHeader, request, signal)
export const deleteWorldEntity = (authorizationHeader: string, request: DeleteWorldEntityRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/entities/delete', authorizationHeader, request, signal)
export const cleanupWorldEntities = (authorizationHeader: string, request: CleanupWorldEntitiesRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/entities/cleanup', authorizationHeader, request, signal)
export const reloadWorldResource = (authorizationHeader: string, request: ReloadWorldResourceRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/xml/reload', authorizationHeader, request, signal)
export const collectGameGarbage = (authorizationHeader: string, request: CollectGameGarbageRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/gc', authorizationHeader, request, signal)
export const undoWorldChangeSet = (authorizationHeader: string, request: UndoWorldChangeSetRequest, signal?: AbortSignal) => postOperation('/api/v1/world-operations/undo', authorizationHeader, request, signal)
export const refreshMapResources = (authorizationHeader: string, request: RefreshMapResourcesRequest, signal?: AbortSignal) => postOperation('/api/v1/map-jobs/refresh-resources', authorizationHeader, request, signal)
export const renderExploredMap = (authorizationHeader: string, request: RenderExploredMapRequest, signal?: AbortSignal) => postOperation('/api/v1/map-jobs/render-explored', authorizationHeader, request, signal)
export const renderFullMap = (authorizationHeader: string, request: RenderFullMapRequest, signal?: AbortSignal) => postOperation('/api/v1/map-jobs/render-full', authorizationHeader, request, signal)

export function submitWorldOperation(
  authorizationHeader: string,
  submission: WorldOperationSubmission,
  signal?: AbortSignal,
): Promise<WorldOperationReceipt> {
  switch (submission.type) {
    case 'deleteLandClaim': return deleteLandClaim(authorizationHeader, submission.request, signal)
    case 'moveOnlinePlayer': return moveOnlinePlayer(authorizationHeader, submission.request, signal)
    case 'moveEntity': return moveWorldEntity(authorizationHeader, submission.request, signal)
    case 'copyRegion': return copyWorldRegion(authorizationHeader, submission.request, signal)
    case 'fillRegion': return fillWorldRegion(authorizationHeader, submission.request, signal)
    case 'clearRegion': return clearWorldRegion(authorizationHeader, submission.request, signal)
    case 'pasteRegion': return pasteWorldRegion(authorizationHeader, submission.request, signal)
    case 'setBlock': return setWorldBlock(authorizationHeader, submission.request, signal)
    case 'placePrefab': return placeWorldPrefab(authorizationHeader, submission.request, signal)
    case 'removePrefab': return removeWorldPrefab(authorizationHeader, submission.request, signal)
    case 'spawnEntity': return spawnWorldEntity(authorizationHeader, submission.request, signal)
    case 'deleteEntity': return deleteWorldEntity(authorizationHeader, submission.request, signal)
    case 'cleanupEntities': return cleanupWorldEntities(authorizationHeader, submission.request, signal)
    case 'reloadResource': return reloadWorldResource(authorizationHeader, submission.request, signal)
    case 'collectGarbage': return collectGameGarbage(authorizationHeader, submission.request, signal)
    case 'undoChangeSet': return undoWorldChangeSet(authorizationHeader, submission.request, signal)
    case 'refreshMapResources': return refreshMapResources(authorizationHeader, submission.request, signal)
    case 'renderExploredMap': return renderExploredMap(authorizationHeader, submission.request, signal)
    case 'renderFullMap': return renderFullMap(authorizationHeader, submission.request, signal)
  }
}

export async function fetchWorldOperation(
  authorizationHeader: string,
  operationId: string,
  signal?: AbortSignal,
): Promise<WorldOperationRecord> {
  const response = await requestJson<unknown>(`/api/v1/world-operations/${encodeURIComponent(operationId)}`, {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseWorldOperation(response)
}
