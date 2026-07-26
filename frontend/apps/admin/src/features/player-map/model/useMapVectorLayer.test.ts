import type { MapBusinessFeature, MapLayerQuery } from './useMapVectorLayer'

import { describe, expect, it, vi } from 'vitest'

import {
  createMapVectorLayerController,
  mapLayerPath,
  parseMapVectorLayerResponse,
} from './useMapVectorLayer'

const query: MapLayerQuery = {
  worldId: 'world/id',
  extent: [-100, -200, 300, 400],
  zoom: 7,
  limit: 250,
}

const historical = {
  id: 'history:EOS_ada',
  kind: 'historical-player',
  x: 10,
  z: -20,
  observedAtUtc: '2026-07-26T08:00:00Z',
  name: 'Ada',
  playerCombinedId: 'EOS_ada',
}

const trader = {
  id: 'trader:1',
  kind: 'trader',
  x: 30,
  z: 40,
  observedAtUtc: '2026-07-26T08:00:00Z',
  name: null,
  prefab: 'trader_jen',
  protectionRadius: null,
  isOpen: null,
} satisfies MapBusinessFeature

const animal = {
  id: 'animal:42',
  kind: 'animal',
  x: 12,
  z: -34,
  observedAtUtc: '2026-07-26T08:00:00Z',
  entityType: 'animalStag',
} satisfies MapBusinessFeature

function visibility(initial = true) {
  let visible = initial
  let listener = () => {}
  return {
    isVisible: () => visible,
    subscribe: (next: () => void) => {
      listener = next
      return () => listener = () => {}
    },
    set(next: boolean) {
      visible = next
      listener()
    },
  }
}

describe('map vector layer transport', () => {
  it('keeps routes isolated and sends bounded world, extent, zoom and limit inputs', () => {
    expect(mapLayerPath('traders', query)).toBe(
      '/api/v1/map/layers/traders?worldId=world%2Fid&minimumX=-100&minimumZ=-200&maximumX=300&maximumZ=400&zoom=7&limit=250',
    )
  })

  it('strictly parses nullable read-only values and rejects extra dangerous fields', () => {
    expect(parseMapVectorLayerResponse('traders', {
      observedAtUtc: '2026-07-26T08:00:00Z',
      items: [trader],
    })).toEqual({
      observedAtUtc: '2026-07-26T08:00:00Z',
      items: [trader],
    })

    expect(() => parseMapVectorLayerResponse('traders', {
      observedAtUtc: '2026-07-26T08:00:00Z',
      items: [{ ...trader, inventory: [] }],
    })).toThrow('Invalid traders map layer response')

    expect(parseMapVectorLayerResponse('animals', {
      observedAtUtc: '2026-07-26T08:00:00Z',
      items: [animal],
    })).toEqual({
      observedAtUtc: '2026-07-26T08:00:00Z',
      items: [animal],
    })
  })
})

describe('map vector layer controller', () => {
  it('defaults off and requires the configured zoom before loading', async () => {
    const request = vi.fn().mockResolvedValue({ observedAtUtc: '2026-07-26T08:00:00Z', items: [historical] })
    const controller = createMapVectorLayerController({
      layerId: 'historical-player-locations',
      minimumZoom: 6,
      authorizationHeader: () => 'Bearer owner',
      request,
      visibility: visibility(),
    })

    expect(controller.enabled.value).toBe(false)
    expect(controller.layer.getVisible()).toBe(false)
    expect(controller.count.value).toBeNull()

    controller.updateView({ ...query, zoom: 5 })
    controller.setEnabled(true)
    expect(controller.state.value).toBe('zoom-required')
    expect(request).not.toHaveBeenCalled()

    controller.updateView(query)
    await vi.waitFor(() => expect(controller.state.value).toBe('ready'))
    expect(request).toHaveBeenCalledWith('Bearer owner', query, expect.any(AbortSignal))
    expect(controller.count.value).toBe(1)
    expect(controller.source.getFeatures()[0]?.getGeometry()?.getCoordinates()).toEqual([10, -20])
    controller.dispose()
  })

  it('aborts obsolete requests, pauses hidden layers and refreshes on resume', async () => {
    const page = visibility(true)
    const pending: Array<{ signal: AbortSignal, resolve: (value: { observedAtUtc: string, items: readonly MapBusinessFeature[] }) => void }> = []
    const request = vi.fn((_authorization: string, _query: MapLayerQuery, signal: AbortSignal) =>
      new Promise<{ observedAtUtc: string, items: readonly MapBusinessFeature[] }>((resolve) => {
        pending.push({ signal, resolve })
      }))
    const controller = createMapVectorLayerController({
      layerId: 'traders',
      minimumZoom: 4,
      authorizationHeader: () => 'Bearer owner',
      request,
      visibility: page,
    })

    controller.updateView(query)
    controller.setEnabled(true)
    controller.updateView({ ...query, extent: [-50, -50, 50, 50] })
    expect(pending[0]?.signal.aborted).toBe(true)

    page.set(false)
    expect(pending[1]?.signal.aborted).toBe(true)
    expect(controller.state.value).toBe('paused')
    const callsWhileHidden = request.mock.calls.length

    page.set(true)
    expect(request.mock.calls.length).toBe(callsWhileHidden + 1)
    pending[pending.length - 1]?.resolve({ observedAtUtc: '2026-07-26T08:00:00Z', items: [trader] })
    await vi.waitFor(() => expect(controller.state.value).toBe('ready'))
    controller.dispose()
  })

  it('retains validated items on failure and retries independently', async () => {
    const request = vi.fn()
      .mockResolvedValueOnce({ observedAtUtc: '2026-07-26T08:00:00Z', items: [trader] })
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce({ observedAtUtc: '2026-07-26T08:10:00Z', items: [] })
    const otherRequest = vi.fn().mockResolvedValue({ observedAtUtc: '2026-07-26T08:00:00Z', items: [historical] })
    const controller = createMapVectorLayerController({
      layerId: 'traders',
      minimumZoom: 4,
      authorizationHeader: () => 'Bearer owner',
      request,
      visibility: visibility(),
    })
    const other = createMapVectorLayerController({
      layerId: 'historical-player-locations',
      minimumZoom: 4,
      authorizationHeader: () => 'Bearer owner',
      request: otherRequest,
      visibility: visibility(),
    })

    controller.updateView(query)
    other.updateView(query)
    controller.setEnabled(true)
    other.setEnabled(true)
    await vi.waitFor(() => expect(controller.count.value).toBe(1))
    await vi.waitFor(() => expect(other.count.value).toBe(1))

    await controller.refresh()
    expect(controller.state.value).toBe('stale')
    expect(controller.count.value).toBe(1)
    expect(other.state.value).toBe('ready')

    controller.retry()
    await vi.waitFor(() => expect(controller.count.value).toBe(0))
    expect(controller.error.value).toBeNull()
    controller.dispose()
    other.dispose()
  })
})
