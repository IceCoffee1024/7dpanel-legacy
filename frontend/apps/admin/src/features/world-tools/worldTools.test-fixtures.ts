import type {
  UndoWorldChangeSetPreflight,
  WorldOperationRecord,
  WorldOperationSubmission,
} from './api/worldTools.types'

export const authorization = 'Bearer owner'
export const baseRequest = {
  worldId: 'world-1',
  worldVersion: 'world-v7',
  mapResourceVersion: 'map-v3',
  confirmed: true,
} as const
export const coordinate = { x: 10, y: 20, z: 30 }
export const region = { first: coordinate, second: { x: 11, y: 21, z: 31 } }
export const bounds = { minimumX: -100, minimumZ: -90, maximumX: 100, maximumZ: 90 }

export const submissions = [
  { type: 'deleteLandClaim', request: { ...baseRequest, claimId: 'claim-1', ownerStableIdentity: 'owner-1', center: coordinate, protectionRadius: 41 } },
  { type: 'moveOnlinePlayer', request: { ...baseRequest, crossplatformId: 'EOS_1', entityId: 1, onlineObservedAtUtc: '2026-07-26T10:00:00.000Z', destination: coordinate } },
  { type: 'moveEntity', request: { ...baseRequest, targetId: 'entity-2', entityId: 2, entityTypeResourceId: 'zombie-template', ownerStableIdentity: null, observedPosition: coordinate, destination: { x: 40, y: 50, z: 60 } } },
  { type: 'copyRegion', request: { ...baseRequest, region } },
  { type: 'fillRegion', request: { ...baseRequest, strongConfirmed: true, region, catalogVersion: 'catalog-4', blockInternalName: 'stone' } },
  { type: 'clearRegion', request: { ...baseRequest, strongConfirmed: true, region } },
  { type: 'pasteRegion', request: { ...baseRequest, strongConfirmed: true, region, sourceChangeSetId: 'changeset-1' } },
  { type: 'setBlock', request: { ...baseRequest, strongConfirmed: true, catalogVersion: 'catalog-4', coordinate, blockInternalName: 'stone', rotation: 1, shape: 'Cube' } },
  { type: 'placePrefab', request: { ...baseRequest, strongConfirmed: true, catalogVersion: 'catalog-4', prefabResourceId: 'prefab-1', anchor: coordinate, rotation: 2, knownBounds: region } },
  { type: 'removePrefab', request: { ...baseRequest, strongConfirmed: true, catalogVersion: 'catalog-4', prefabResourceId: 'prefab-1', prefabInstanceId: 'instance-1', anchor: coordinate, rotation: 2, knownBounds: region } },
  { type: 'spawnEntity', request: { ...baseRequest, strongConfirmed: true, catalogVersion: 'catalog-4', entityTypeResourceId: 'zombie-template', quantity: 2, center: coordinate, radius: 8 } },
  { type: 'deleteEntity', request: { ...baseRequest, strongConfirmed: true, catalogVersion: 'catalog-4', targetId: 'entity-2', entityId: 2, entityTypeResourceId: 'zombie-template', ownerStableIdentity: null, observedPosition: coordinate } },
  { type: 'cleanupEntities', request: { ...baseRequest, strongConfirmed: true, category: 'Hostile', center: coordinate, radius: 20, maximumCount: 5 } },
  { type: 'reloadResource', request: { ...baseRequest, strongConfirmed: true, resourceKind: 'Blocks' } },
  { type: 'collectGarbage', request: baseRequest },
  { type: 'undoChangeSet', request: { ...baseRequest, strongConfirmed: true, sourceOperationId: 'operation-source', changeSetId: 'changeset-1', currentRegionHash: 'sha256:abc' } },
  { type: 'refreshMapResources', request: { ...baseRequest, bounds } },
  { type: 'renderExploredMap', request: { ...baseRequest, bounds } },
  { type: 'renderFullMap', request: { ...baseRequest, strongConfirmed: true, bounds } },
] satisfies readonly WorldOperationSubmission[]

export const expectedPaths: Record<WorldOperationSubmission['type'], string> = {
  deleteLandClaim: '/api/v1/world-operations/land-claims/delete',
  moveOnlinePlayer: '/api/v1/world-operations/players/move',
  moveEntity: '/api/v1/world-operations/entities/move',
  copyRegion: '/api/v1/world-operations/regions/copy',
  fillRegion: '/api/v1/world-operations/regions/fill',
  clearRegion: '/api/v1/world-operations/regions/clear',
  pasteRegion: '/api/v1/world-operations/regions/paste',
  setBlock: '/api/v1/world-operations/blocks/set',
  placePrefab: '/api/v1/world-operations/prefabs/place',
  removePrefab: '/api/v1/world-operations/prefabs/remove',
  spawnEntity: '/api/v1/world-operations/entities/spawn',
  deleteEntity: '/api/v1/world-operations/entities/delete',
  cleanupEntities: '/api/v1/world-operations/entities/cleanup',
  reloadResource: '/api/v1/world-operations/xml/reload',
  collectGarbage: '/api/v1/world-operations/gc',
  undoChangeSet: '/api/v1/world-operations/undo',
  refreshMapResources: '/api/v1/map-jobs/refresh-resources',
  renderExploredMap: '/api/v1/map-jobs/render-explored',
  renderFullMap: '/api/v1/map-jobs/render-full',
}

export const receiptJson = {
  operationId: 'operation-1',
  jobId: '7257ce31-623a-48d7-a5b8-406a181fb5db',
  status: 'Queued',
  correlationId: 'correlation-1',
  createdAtUtc: '2026-07-26T10:00:00.000Z',
}

export function operation(status: WorldOperationRecord['status']): WorldOperationRecord {
  return {
    operationId: 'operation-1',
    jobId: receiptJson.jobId,
    kind: 'RenderFullMap',
    worldId: 'world-1',
    worldVersion: 'world-v7',
    mapResourceVersion: 'map-v3',
    correlationId: 'correlation-1',
    confirmationSummary: 'Render full map for world-1',
    isReversible: false,
    changeSetId: null,
    status,
    progress: status === 'Running' ? { current: 1, total: 2 } : null,
    errorCode: status === 'ResultUnknown' ? 'result_unknown' : null,
    createdAtUtc: '2026-07-26T10:00:00.000Z',
    startedAtUtc: status === 'Queued' ? null : '2026-07-26T10:00:01.000Z',
    completedAtUtc: status === 'Queued' || status === 'Running' ? null : '2026-07-26T10:00:02.000Z',
  }
}

export const readyPreflight: UndoWorldChangeSetPreflight = {
  sourceOperationId: 'operation-source',
  changeSetId: 'changeset-1',
  worldId: 'world-1',
  worldVersion: 'world-v7',
  afterHash: 'sha256:after',
  currentRegionHash: 'sha256:current',
  currentHashMatches: true,
  status: 'ready',
}
