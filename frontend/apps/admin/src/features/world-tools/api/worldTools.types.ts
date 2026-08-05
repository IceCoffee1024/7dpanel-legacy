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

export interface UndoWorldChangeSetPreflight {
  sourceOperationId: string
  changeSetId: string
  worldId: string
  worldVersion: string
  afterHash: string
  currentRegionHash: string | null
  currentHashMatches: boolean | null
  status: string
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
