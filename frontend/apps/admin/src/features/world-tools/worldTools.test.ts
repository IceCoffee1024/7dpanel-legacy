import type {
  UndoWorldChangeSetPreflight,
  WorldOperationRecord,
  WorldResourcesTransport,
} from './api/worldTools.types'
import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { defineComponent, isReadonly } from 'vue'
import { HttpError } from '../../shared/api/http'

import { useUndoPreflight } from './model/useUndoPreflight'
import { useWorldOperations } from './model/useWorldOperations'
import { useWorldResources } from './model/useWorldResources'
import {
  authorization,
  operation,
  readyPreflight,
  receiptJson,
  submissions,
} from './worldTools.test-fixtures'

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

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
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
    const summary = {
      sourceState: 'Success' as const,
      worldId: 'world-1',
      worldVersion: 'world-v7',
      seed: null,
      width: 8192,
      height: 8192,
      gameVersion: '3.0.1-b4',
      mapResourceVersion: 'map-v3',
      availableExtent: null,
      observedAtUtc: '2026-07-26T10:00:00.000Z',
    }
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
