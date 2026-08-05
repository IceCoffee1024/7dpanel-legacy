import { afterEach, describe, expect, it, vi } from 'vitest'

import * as worldToolsApi from './api/worldTools'
import { parseWorldOperation } from './api/worldTools.history'
import { parseWorldOperationReceipt, submitWorldOperation } from './api/worldTools.operation'
import { fetchUndoWorldChangeSetPreflight, parseUndoWorldChangeSetPreflight } from './api/worldTools.preflight'
import { parseWorldSummary } from './api/worldTools.read'
import { fetchWorldBlockCatalog, fetchWorldContainers } from './api/worldTools.resource'
import {
  authorization,
  expectedPaths,
  operation,
  readyPreflight,
  receiptJson,
  submissions,
} from './worldTools.test-fixtures'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('world-tools transport', () => {
  it('keeps the public API entry point as a re-export surface', () => {
    expect(worldToolsApi.parseWorldSummary).toBe(parseWorldSummary)
    expect(worldToolsApi.parseWorldOperation).toBe(parseWorldOperation)
    expect(worldToolsApi.submitWorldOperation).toBe(submitWorldOperation)
  })

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

  it('rejects unknown fields and non-UTC offsets across nested world payloads', async () => {
    const summary = {
      sourceState: 'Success',
      worldId: 'world-1',
      worldVersion: 'world-v7',
      seed: null,
      width: 8192,
      height: 8192,
      gameVersion: '3.0.1-b4',
      mapResourceVersion: 'map-v3',
      availableExtent: { minimumX: -10, minimumZ: -20, maximumX: 10, maximumZ: 20 },
      observedAtUtc: '2026-07-26T10:00:00.000Z',
    }
    const nestedPosition = { x: 1, y: 2, z: 3 }
    const container = {
      serverId: 'server-1',
      stableIdentity: 'container-1',
      parentStableIdentity: 'root',
      position: nestedPosition,
      loadState: 'Loaded',
      isLocked: null,
      slotCount: null,
      usedSlotCount: null,
      items: null,
    }
    const collection = {
      sourceState: 'Success',
      observedAtUtc: '2026-07-26T10:00:00.000Z',
      items: [container],
    }

    expect(() => parseWorldSummary({ ...summary, extra: true })).toThrow('Invalid world tools response')
    expect(() => parseWorldSummary({
      ...summary,
      availableExtent: { ...summary.availableExtent, extra: true },
    })).toThrow('Invalid world tools response')
    expect(() => parseWorldSummary({ ...summary, observedAtUtc: '2026-07-26T18:00:00.000+08:00' })).toThrow('Invalid world tools response')
    expect(() => parseWorldOperation({ ...operation('Running'), progress: { current: 1, total: 2, extra: true } })).toThrow('Invalid world tools response')
    expect(() => parseWorldOperation({ ...operation('Queued'), createdAtUtc: '2026-07-26T18:00:00.000+08:00' })).toThrow('Invalid world tools response')
    expect(() => parseWorldOperationReceipt({ ...receiptJson, extra: true })).toThrow('Invalid world tools response')
    expect(() => parseWorldOperationReceipt({ ...receiptJson, createdAtUtc: '2026-07-26T18:00:00.000+08:00' })).toThrow('Invalid world tools response')
    expect(() => parseUndoWorldChangeSetPreflight({ ...readyPreflight, extra: true })).toThrow('Invalid undo preflight response')

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(collection), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })))
    await expect(fetchWorldContainers(authorization)).resolves.toEqual(collection)

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      ...collection,
      items: [{ ...container, position: { ...nestedPosition, extra: true } }],
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })))
    await expect(fetchWorldContainers(authorization)).rejects.toThrow('Invalid world tools response')

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      sourceState: 'Success',
      catalogVersion: 'catalog-1',
      observedAtUtc: '2026-07-26T18:00:00.000+08:00',
      items: [],
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })))
    await expect(fetchWorldBlockCatalog(authorization)).rejects.toThrow('Invalid world tools response')
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
