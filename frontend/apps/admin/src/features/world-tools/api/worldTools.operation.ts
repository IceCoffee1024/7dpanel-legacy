import type {
  CleanupWorldEntitiesRequest,
  ClearRegionRequest,
  CollectGameGarbageRequest,
  CopyRegionRequest,
  DeleteLandClaimRequest,
  DeleteWorldEntityRequest,
  FillRegionRequest,
  MoveOnlinePlayerRequest,
  MoveWorldEntityRequest,
  PasteRegionRequest,
  PlacePrefabRequest,
  RefreshMapResourcesRequest,
  ReloadWorldResourceRequest,
  RemovePrefabRequest,
  RenderExploredMapRequest,
  RenderFullMapRequest,
  SetBlockRequest,
  SpawnWorldEntityRequest,
  UndoWorldChangeSetRequest,
  WorldOperationReceipt,
  WorldOperationSubmission,
} from './worldTools.types'

import { requestJson } from '../../../shared/api/http'

import { parseOperationStatus, record, text, utc } from './worldTools.protocol'

const operationReceiptKeys = ['operationId', 'jobId', 'status', 'correlationId', 'createdAtUtc'] as const

export function parseWorldOperationReceipt(value: unknown): WorldOperationReceipt {
  const source = record(value, operationReceiptKeys)
  return Object.freeze({
    operationId: text(source.operationId),
    jobId: text(source.jobId),
    status: parseOperationStatus(source.status),
    correlationId: text(source.correlationId),
    createdAtUtc: utc(source.createdAtUtc),
  })
}

async function postOperation<TRequest>(
  path: string,
  authorizationHeader: string,
  request: TRequest,
  signal?: AbortSignal,
): Promise<WorldOperationReceipt> {
  const response = await requestJson<unknown>(path, {
    method: 'POST',
    headers: { 'Authorization': authorizationHeader, 'Content-Type': 'application/json' },
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
