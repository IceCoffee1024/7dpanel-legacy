import type {
  UndoWorldChangeSetPreflight,
  WorldOperationRecord,
  WorldOperationSubmission,
  WorldResourcesTransport,
} from './api/worldTools'
import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { defineComponent, isReadonly } from 'vue'
import { HttpError } from '../../shared/api/http'

import {
  fetchUndoWorldChangeSetPreflight,
  parseUndoWorldChangeSetPreflight,
  parseWorldOperation,
  parseWorldSummary,
  submitWorldOperation,
} from './api/worldTools'
import { useUndoPreflight } from './model/useUndoPreflight'
import { useWorldOperations } from './model/useWorldOperations'
import { useWorldResources } from './model/useWorldResources'
import {
  createInitialWorldOperationForm,
  createWorldOperationReview,
} from './model/worldOperationForm'

const authorization = 'Bearer owner'
const baseRequest = {
  worldId: 'world-1',
  worldVersion: 'world-v7',
  mapResourceVersion: 'map-v3',
  confirmed: true,
} as const
const coordinate = { x: 10, y: 20, z: 30 }
const region = { first: coordinate, second: { x: 11, y: 21, z: 31 } }
const bounds = { minimumX: -100, minimumZ: -90, maximumX: 100, maximumZ: 90 }

const submissions = [
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

const expectedPaths: Record<WorldOperationSubmission['type'], string> = {
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

const receiptJson = {
  operationId: 'operation-1',
  jobId: '7257ce31-623a-48d7-a5b8-406a181fb5db',
  status: 'Queued',
  correlationId: 'correlation-1',
  createdAtUtc: '2026-07-26T10:00:00.000Z',
}

function operation(status: WorldOperationRecord['status']): WorldOperationRecord {
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

function mountOperations(options: Parameters<typeof useWorldOperations>[0]) {
  let controller!: ReturnType<typeof useWorldOperations>
  const Host = defineComponent({
    setup() {
      controller = useWorldOperations(options)
      return () => null
    },
  })
  const wrapper = mount(Host)
  return { controller: () => controller, wrapper }
}

function mountUndoPreflight(options: Parameters<typeof useUndoPreflight>[0]) {
  let controller!: ReturnType<typeof useUndoPreflight>
  const Host = defineComponent({
    setup() {
      controller = useUndoPreflight(options)
      return () => null
    },
  })
  const wrapper = mount(Host)
  return { controller: () => controller, wrapper }
}

const readyPreflight: UndoWorldChangeSetPreflight = {
  sourceOperationId: 'operation-source',
  changeSetId: 'changeset-1',
  worldId: 'world-1',
  worldVersion: 'world-v7',
  afterHash: 'sha256:after',
  currentRegionHash: 'sha256:current',
  currentHashMatches: true,
  status: 'ready',
}

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('world-tools transport', () => {
  it('normalizes backend availability while preserving partial and nullable values', () => {
    expect(parseWorldSummary({
      sourceState: 'Available',
      worldId: 'world-1',
      worldVersion: 'world-v7',
      seed: null,
      width: null,
      height: 8192,
      gameVersion: null,
      mapResourceVersion: 'map-v3',
      availableExtent: null,
      observedAtUtc: '2026-07-26T10:00:00.000Z',
    })).toMatchObject({ sourceState: 'Success', seed: null, width: null, gameVersion: null })

    expect(parseWorldSummary({
      sourceState: 'Partial',
      worldId: 'world-1',
      worldVersion: 'world-v7',
      seed: null,
      width: null,
      height: null,
      gameVersion: null,
      mapResourceVersion: null,
      availableExtent: null,
      observedAtUtc: '2026-07-26T10:00:00.000Z',
    }).sourceState).toBe('Partial')
  })

  it.each(submissions)('uses the independent $type request path and exact body', async (submission) => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(receiptJson), {
      status: 202,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await submitWorldOperation(authorization, submission)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe(expectedPaths[submission.type])
    expect(init.method).toBe('POST')
    expect(init.headers).toEqual({ 'Authorization': authorization, 'Content-Type': 'application/json' })
    expect(JSON.parse(String(init.body))).toEqual(submission.request)
  })

  it.each(['Queued', 'Running', 'Succeeded', 'Failed', 'Cancelled', 'Interrupted', 'ResultUnknown', 'RollbackFailed'] as const)(
    'parses the %s operation state without collapsing it',
    (status) => {
      expect(parseWorldOperation(operation(status))).toMatchObject({ status })
    },
  )

  it('strictly parses undo preflight and keeps current hash distinct from the recorded after hash', () => {
    expect(parseUndoWorldChangeSetPreflight(readyPreflight)).toEqual(readyPreflight)
    expect(parseUndoWorldChangeSetPreflight(readyPreflight).currentRegionHash).not.toBe(readyPreflight.afterHash)
    expect(() => parseUndoWorldChangeSetPreflight({ ...readyPreflight, currentHashMatches: 'true' })).toThrow()
    expect(() => parseUndoWorldChangeSetPreflight({ ...readyPreflight, status: '' })).toThrow()
    expect(() => parseUndoWorldChangeSetPreflight({ ...readyPreflight, changeSetId: null })).toThrow()
  })

  it('requests encoded undo preflight with authorization and caller cancellation', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(readyPreflight), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    await fetchUndoWorldChangeSetPreflight(authorization, 'source/1', controller.signal)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/v1/world-operations/source%2F1/undo-preflight')
    expect(init.headers).toEqual({ Authorization: authorization })
    expect(init.signal).toBeInstanceOf(AbortSignal)
  })
})

describe('world operation form mapping', () => {
  it.each(submissions.map(item => item.type))('builds a closed %s submission and complete confirmation summary', (type) => {
    const form = createInitialWorldOperationForm()
    Object.assign(form, {
      type,
      targetId: 'entity-2',
      ownerStableIdentity: 'owner-1',
      entityId: 2,
      onlineObservedAtUtc: '2026-07-26T10:00:00.000Z',
      entityTypeResourceId: 'zombie-template',
      observedX: 10,
      observedY: 20,
      observedZ: 30,
      destinationX: 40,
      destinationY: 50,
      destinationZ: 60,
      firstX: -5,
      firstY: 10,
      firstZ: -4,
      secondX: 5,
      secondY: 20,
      secondZ: 4,
      catalogVersion: 'catalog-4',
      blockInternalName: 'stone',
      rotation: 1,
      blockShape: 'Cube',
      prefabResourceId: 'prefab-1',
      prefabInstanceId: 'instance-1',
      quantity: 2,
      radius: 8,
      maximumCount: 5,
      entityCategory: 'Hostile',
      reloadResourceKind: 'Blocks',
      sourceOperationId: 'operation-source',
      changeSetId: 'changeset-1',
      currentRegionHash: 'sha256:abc',
      sourceChangeSetId: 'changeset-source',
      boundsEnabled: true,
      minimumX: -100,
      minimumZ: -90,
      maximumX: 100,
      maximumZ: 90,
    })
    const review = createWorldOperationReview(form, {
      sourceState: 'Success',
      worldId: 'world-1',
      worldVersion: 'world-v7',
      seed: null,
      width: 8192,
      height: 8192,
      gameVersion: '3.0.1-b4',
      mapResourceVersion: 'map-v3',
      availableExtent: null,
      observedAtUtc: '2026-07-26T10:00:00.000Z',
    })

    expect(review.submission.type).toBe(type)
    expect(review).toMatchObject({
      worldId: 'world-1',
      worldVersion: 'world-v7',
      mapResourceVersion: 'map-v3',
    })
    expect(review.target.length).toBeGreaterThan(0)
    expect(review.scope.length).toBeGreaterThan(0)
    expect(review.impact.length).toBeGreaterThan(0)
    expect(typeof review.reversible).toBe('boolean')
  })

  it('marks strong operations explicitly while leaving ordinary operations single-confirmation', () => {
    const summary = {
      sourceState: 'Success' as const,
      worldId: 'world-1',
      worldVersion: 'world-v7',
      mapResourceVersion: 'map-v3',
      seed: null,
      width: null,
      height: null,
      gameVersion: null,
      availableExtent: null,
      observedAtUtc: '2026-07-26T10:00:00.000Z',
    }
    const ordinary = createInitialWorldOperationForm()
    Object.assign(ordinary, { type: 'collectGarbage' })
    expect(createWorldOperationReview(ordinary, summary).strongConfirmation).toBe(false)

    const strong = createInitialWorldOperationForm()
    Object.assign(strong, { type: 'renderFullMap' })
    expect(createWorldOperationReview(strong, summary).strongConfirmation).toBe(true)
  })
})

describe('useWorldOperations', () => {
  it('stores only the 202 receipt, writes operationId, then polls until a terminal state', async () => {
    vi.useFakeTimers()
    const replaceOperationId = vi.fn()
    const fetchOperation = vi.fn()
      .mockResolvedValueOnce(operation('Queued'))
      .mockResolvedValueOnce(operation('Running'))
      .mockResolvedValueOnce(operation('Succeeded'))
    const mounted = mountOperations({
      auth: { authorizationHeader: authorization, role: 'Owner', expireSession: vi.fn() },
      submitOperation: vi.fn().mockResolvedValue(receiptJson),
      fetchOperation,
      replaceOperationId,
      pollIntervalMs: 25,
    })

    await mounted.controller().submit(submissions[18])
    expect(mounted.controller().receipt.value?.status).toBe('Queued')
    expect(mounted.controller().operation.value?.status).toBe('Queued')
    expect(mounted.controller().state.value).toBe('polling')
    expect(replaceOperationId).toHaveBeenCalledWith('operation-1')

    await vi.advanceTimersByTimeAsync(25)
    expect(mounted.controller().operation.value?.status).toBe('Running')
    await vi.advanceTimersByTimeAsync(25)
    expect(mounted.controller().operation.value?.status).toBe('Succeeded')
    expect(mounted.controller().state.value).toBe('terminal')
    expect(fetchOperation).toHaveBeenCalledTimes(3)
    expect(isReadonly(mounted.controller().operation)).toBe(true)
    mounted.wrapper.unmount()
  })

  it('resumes a query operation and aborts polling on unmount', async () => {
    vi.useFakeTimers()
    let requestSignal: AbortSignal | undefined
    const fetchOperation = vi.fn((_header: string, _operationId: string, signal?: AbortSignal) => {
      requestSignal = signal
      return new Promise<WorldOperationRecord>(() => {})
    })
    const mounted = mountOperations({
      auth: { authorizationHeader: authorization, role: 'Owner', expireSession: vi.fn() },
      fetchOperation,
      replaceOperationId: vi.fn(),
      pollIntervalMs: 25,
    })

    void mounted.controller().resume('operation-restored')
    await flushPromises()
    expect(fetchOperation).toHaveBeenCalledWith(authorization, 'operation-restored', expect.any(AbortSignal))
    mounted.wrapper.unmount()
    expect(requestSignal?.aborted).toBe(true)
  })
})

describe('useUndoPreflight', () => {
  it('publishes only the latest successful preflight and aborts the replaced request', async () => {
    let firstSignal: AbortSignal | undefined
    let resolveFirst!: (value: UndoWorldChangeSetPreflight) => void
    const fetchPreflight = vi.fn()
      .mockImplementationOnce((_header: string, _id: string, signal?: AbortSignal) => {
        firstSignal = signal
        return new Promise<UndoWorldChangeSetPreflight>((resolve) => {
          resolveFirst = resolve
        })
      })
      .mockResolvedValueOnce({ ...readyPreflight, sourceOperationId: 'operation-new' })
    const mounted = mountUndoPreflight({
      auth: { authorizationHeader: authorization, expireSession: vi.fn() },
      fetchPreflight,
    })

    void mounted.controller().load('operation-old')
    await mounted.controller().load('operation-new')
    resolveFirst({ ...readyPreflight, sourceOperationId: 'operation-old' })
    await flushPromises()

    expect(firstSignal?.aborted).toBe(true)
    expect(mounted.controller().data.value?.sourceOperationId).toBe('operation-new')
    expect(mounted.controller().phase.value).toBe('ready')
    mounted.wrapper.unmount()
  })

  it('classifies conflicts and expires a 401 session', async () => {
    const expireSession = vi.fn()
    const onSessionExpired = vi.fn()
    const fetchPreflight = vi.fn()
      .mockRejectedValueOnce(new HttpError('http', 'conflict', { status: 409 }))
      .mockRejectedValueOnce(new HttpError('http', 'expired', { status: 401 }))
    const mounted = mountUndoPreflight({
      auth: { authorizationHeader: authorization, expireSession },
      fetchPreflight,
      onSessionExpired,
    })

    await mounted.controller().load('operation-conflict')
    expect(mounted.controller().errorCode.value).toBe('conflict')
    await mounted.controller().load('operation-expired')
    expect(mounted.controller().errorCode.value).toBe('session-expired')
    expect(expireSession).toHaveBeenCalledTimes(1)
    expect(onSessionExpired).toHaveBeenCalledTimes(1)
    mounted.wrapper.unmount()
  })
})

describe('useWorldResources', () => {
  it('keeps independent read sources and maps a failed source to Unavailable', async () => {
    const summary = parseWorldSummary({
      sourceState: 'Available',
      worldId: 'world-1',
      worldVersion: 'world-v7',
      seed: null,
      width: 8192,
      height: 8192,
      gameVersion: '3.0.1-b4',
      mapResourceVersion: 'map-v3',
      availableExtent: null,
      observedAtUtc: '2026-07-26T10:00:00.000Z',
    })
    const emptyCollection = { sourceState: 'Success' as const, observedAtUtc: '2026-07-26T10:00:00.000Z', items: [] }
    const emptyCatalog = { sourceState: 'Success' as const, catalogVersion: 'catalog-4', observedAtUtc: '2026-07-26T10:00:00.000Z', items: [] }
    const transport: WorldResourcesTransport = {
      fetchSummary: vi.fn().mockResolvedValue(summary),
      fetchLandClaims: vi.fn().mockRejectedValue(new Error('claim source unavailable')),
      fetchVehicles: vi.fn().mockResolvedValue(emptyCollection),
      fetchDrones: vi.fn().mockResolvedValue(emptyCollection),
      fetchContainers: vi.fn().mockResolvedValue(emptyCollection),
      fetchBlockCatalog: vi.fn().mockResolvedValue(emptyCatalog),
      fetchPrefabCatalog: vi.fn().mockResolvedValue(emptyCatalog),
      fetchEntityTypeCatalog: vi.fn().mockResolvedValue(emptyCatalog),
    }
    let controller!: ReturnType<typeof useWorldResources>
    const Host = defineComponent({
      setup() {
        controller = useWorldResources({
          auth: { authorizationHeader: authorization, expireSession: vi.fn() },
          transport,
        })
        return () => null
      },
    })
    const wrapper = mount(Host)
    await flushPromises()

    expect(controller.summary.value.data?.sourceState).toBe('Success')
    expect(controller.landClaims.value).toMatchObject({ phase: 'failed', sourceState: 'Unavailable', data: null })
    expect(controller.vehicles.value).toMatchObject({ phase: 'ready', sourceState: 'Success' })
    expect(isReadonly(controller.summary)).toBe(true)
    wrapper.unmount()
  })
})
