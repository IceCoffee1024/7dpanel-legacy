import type { MapMetadata } from '../api/playerMap'

import ImageTile from 'ol/ImageTile.js'
import TileState from 'ol/TileState.js'
import { describe, expect, it, vi } from 'vitest'

import { createAuthenticatedTileLayerController } from './useAuthenticatedTileLayer'

const metadata: MapMetadata = {
  availability: 'available',
  observedAtUtc: '2026-07-26T08:29:00Z',
  worldId: 'world/id',
  worldName: 'Navezgane',
  extent: { minimumX: -4096, minimumZ: -4096, maximumX: 4096, maximumZ: 4096 },
  axes: { xAxisDirection: 'east', zAxisDirection: 'north' },
  availableZoomLevels: [0, 1, 2, 3, 4, 5],
  tileSize: 256,
  mapResourceVersion: 'map-v1',
}

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

function imageTile(image: HTMLImageElement) {
  const tile = new ImageTile([0, 0, 0], TileState.IDLE, '', {}, () => {})
  tile.setImage(image)
  return tile
}

describe('authenticated tile layer controller', () => {
  it('uses a Bearer header Blob request and converts only the tile index to TMS y', async () => {
    const blob = new Blob(['tile'], { type: 'image/png' })
    const fetchImpl = vi.fn().mockResolvedValue(new Response(blob, { status: 200 }))
    const createObjectURL = vi.fn(() => 'blob:tile-one')
    const controller = createAuthenticatedTileLayerController({
      metadata,
      authorizationHeader: () => 'Bearer secret',
      fetchImpl,
      createObjectURL,
      revokeObjectURL: vi.fn(),
      visibility: visibility(),
    })

    expect(controller.enabled.value).toBe(false)
    expect(controller.layer.getVisible()).toBe(false)
    const attribution = controller.source.getAttributions()?.({} as never)
    expect(Array.isArray(attribution) ? attribution.join(' ') : attribution).toContain('© The Fun Pimps LLC')

    controller.setEnabled(true)
    const tileUrl = controller.source.getTileUrlFunction()([3, 4, -6], 1, null as never)
    expect(tileUrl).toBe('/api/v1/map/tiles/world%2Fid/3/4/5')

    const image = document.createElement('img')
    controller.source.getTileLoadFunction()(imageTile(image), tileUrl!)
    await vi.waitFor(() => expect(image.src).toBe('blob:tile-one'))

    expect(fetchImpl).toHaveBeenCalledWith(tileUrl, {
      credentials: 'omit',
      headers: { Authorization: 'Bearer secret' },
      signal: expect.any(AbortSignal),
    })
    expect(createObjectURL).toHaveBeenCalledWith(blob)
    controller.dispose()
  })

  it('revokes object URLs on replacement, reload, disable and disposal', async () => {
    const fetchImpl = vi.fn().mockImplementation(async () => new Response(new Blob(['tile']), { status: 200 }))
    const createObjectURL = vi.fn()
      .mockReturnValueOnce('blob:first')
      .mockReturnValueOnce('blob:second')
      .mockReturnValueOnce('blob:third')
    const revokeObjectURL = vi.fn()
    const controller = createAuthenticatedTileLayerController({
      metadata,
      authorizationHeader: () => 'Bearer secret',
      fetchImpl,
      createObjectURL,
      revokeObjectURL,
      visibility: visibility(),
    })
    const image = document.createElement('img')
    const load = controller.source.getTileLoadFunction()

    controller.setEnabled(true)
    load(imageTile(image), '/one')
    await vi.waitFor(() => expect(image.src).toBe('blob:first'))
    load(imageTile(image), '/two')
    await vi.waitFor(() => expect(image.src).toBe('blob:second'))
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:first')

    controller.reload()
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:second')
    expect(controller.source.getRevision()).toBeGreaterThan(0)

    load(imageTile(image), '/three')
    await vi.waitFor(() => expect(image.src).toBe('blob:third'))
    controller.setEnabled(false)
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:third')

    controller.dispose()
    expect(revokeObjectURL).toHaveBeenCalledTimes(3)
  })

  it('aborts while hidden and refreshes on resume without starting a server job', () => {
    const page = visibility(true)
    const fetchImpl = vi.fn<typeof fetch>(() => new Promise<Response>(() => {}))
    const controller = createAuthenticatedTileLayerController({
      metadata,
      authorizationHeader: () => 'Bearer secret',
      fetchImpl,
      visibility: page,
    })
    const refresh = vi.spyOn(controller.source, 'refresh')

    controller.setEnabled(true)
    controller.source.getTileLoadFunction()(imageTile(document.createElement('img')), '/tile')
    const signal = fetchImpl.mock.calls[0]?.[1]?.signal

    page.set(false)
    expect(signal?.aborted).toBe(true)
    const hiddenRefreshes = refresh.mock.calls.length

    page.set(true)
    expect(refresh.mock.calls.length).toBe(hiddenRefreshes + 1)
    expect(fetchImpl).toHaveBeenCalledOnce()
    controller.dispose()
  })

  it('keeps tile failures isolated as retryable state', async () => {
    const controller = createAuthenticatedTileLayerController({
      metadata,
      authorizationHeader: () => 'Bearer expired',
      fetchImpl: vi.fn().mockResolvedValue(new Response(null, { status: 401 })),
      visibility: visibility(),
    })

    controller.setEnabled(true)
    controller.source.getTileLoadFunction()(imageTile(document.createElement('img')), '/tile')

    await vi.waitFor(() => expect(controller.error.value).toBe('Tiles could not be loaded'))
    expect(controller.loading.value).toBe(false)
    controller.retry()
    expect(controller.error.value).toBeNull()
    controller.dispose()
  })
})
