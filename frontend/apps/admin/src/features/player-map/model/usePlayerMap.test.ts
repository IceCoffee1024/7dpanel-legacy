import type { MapMetadata, PlayerTrack } from '../api/playerMap'
import { flushPromises } from '@vue/test-utils'

import { describe, expect, it, vi } from 'vitest'

import { HttpError } from '../../../shared/api/http'
import { createPlayerMapController } from './usePlayerMap'

const metadata: MapMetadata = {
  availability: 'available',
  observedAtUtc: '2026-07-26T08:29:00Z',
  worldId: 'world-navezgane',
  worldName: 'Navezgane',
  extent: { minimumX: -4096, minimumZ: -4096, maximumX: 4096, maximumZ: 4096 },
  axes: { xAxisDirection: 'east', zAxisDirection: 'north' },
  availableZoomLevels: [0, 1, 2, 3, 4, 5, 6],
  tileSize: 256,
  mapResourceVersion: null,
}

const track: PlayerTrack = {
  crossplatformId: 'EOS_ada',
  segments: [{ points: [
    { snapshotId: 1, name: 'Ada', x: 1, y: 2, z: 3, observedAtUtc: '2026-07-26T08:00:00Z' },
    { snapshotId: 2, name: 'Ada', x: 4, y: 5, z: 6, observedAtUtc: '2026-07-26T08:10:00Z' },
  ] }],
}

const onlineSnapshot = {
  players: [{
    name: 'Ada',
    platformIdentity: { combinedId: 'Steam_ada', platform: 'Steam' },
    crossplatformIdentity: { combinedId: 'EOS_ada', platform: 'EOS' },
    position: { x: 10, y: 20, z: 30 },
    observedAtUtc: '2026-07-26T08:00:00Z',
  } as never],
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((next) => {
    resolve = next
  })
  return { promise, resolve }
}

const restoredPlayer = {
  player: {
    crossplatformId: 'EOS_ada',
    latestName: 'Ada',
    firstObservedAtUtc: '2026-07-20T08:00:00Z',
    lastObservedAtUtc: '2026-07-26T08:00:00Z',
    totalObservationCount: 2,
    retainedSnapshotCount: 2,
    compactedSnapshotCount: 0,
    hasGaps: false,
  },
  gapSummary: { gapCount: 0, droppedObservationCount: 0 },
}

function createController(overrides: Record<string, unknown> = {}) {
  const replace = vi.fn()
  const controller = createPlayerMapController({
    authorizationHeader: () => 'Bearer test',
    initialQuery: new URLSearchParams('player=EOS_ada&from=2026-07-25T00%3A00%3A00Z&to=2026-07-26T00%3A00%3A00Z&observation=2'),
    replaceQuery: replace,
    fetchMetadata: vi.fn().mockResolvedValue(metadata),
    fetchGameTime: vi.fn().mockResolvedValue({ availability: 'available', day: 17, hour: 9, minute: 4, observedAtUtc: '2026-07-26T08:31:00Z' }),
    fetchOnline: vi.fn().mockResolvedValue({ players: [] }),
    fetchPlayers: vi.fn().mockResolvedValue({ players: [], nextCursor: null }),
    fetchPlayer: vi.fn().mockResolvedValue(restoredPlayer),
    fetchTrack: vi.fn().mockResolvedValue(track),
    ...overrides,
  })
  return { controller, replace }
}

describe('player map local controller', () => {
  it('restores player, UTC range and selected observation from the URL', () => {
    const { controller } = createController()

    expect(controller.filters.value).toEqual({
      player: 'EOS_ada',
      fromUtc: '2026-07-25T00:00:00Z',
      toUtc: '2026-07-26T00:00:00Z',
    })
    expect(controller.selectedSnapshotId.value).toBe(2)
  })

  it('aborts an old track request when filters change', async () => {
    const first = deferred<PlayerTrack>()
    const signals: AbortSignal[] = []
    const fetchTrack = vi.fn((_header, _filters, signal: AbortSignal) => {
      signals.push(signal)
      return signals.length === 1 ? first.promise : Promise.resolve(track)
    })
    const { controller } = createController({ fetchTrack })

    const oldRequest = controller.refreshTrack()
    controller.setRange('2026-07-25T01:00:00Z', '2026-07-26T00:00:00Z')
    const nextRequest = controller.refreshTrack()
    await nextRequest

    expect(signals[0]?.aborted).toBe(true)
    first.resolve(track)
    await oldRequest
  })

  it('searches historical players with the entered query and aborts the old search', async () => {
    const first = deferred<{ players: readonly never[], nextCursor: null }>()
    const signals: AbortSignal[] = []
    const fetchPlayers = vi.fn((_header, _options, signal: AbortSignal) => {
      signals.push(signal)
      return signals.length === 1 ? first.promise : Promise.resolve({ players: [], nextCursor: null })
    })
    const { controller } = createController({ fetchPlayers })

    const oldSearch = controller.searchHistoricalPlayers('Ada')
    const nextSearch = controller.searchHistoricalPlayers('Grace')
    await nextSearch

    expect(signals[0]?.aborted).toBe(true)
    expect(fetchPlayers.mock.calls[1]?.[1]).toMatchObject({ query: 'Grace', pageSize: 50, cursor: null })
    first.resolve({ players: [], nextCursor: null })
    await oldSearch
  })

  it('loads a restored player exactly when the first page does not contain it', async () => {
    const fetchPlayer = vi.fn().mockResolvedValue(restoredPlayer)
    const { controller } = createController({ fetchPlayer })

    await controller.searchHistoricalPlayers('')

    expect(fetchPlayer).toHaveBeenCalledWith('Bearer test', 'EOS_ada', expect.any(AbortSignal))
    expect(controller.historicalPlayers.value[0]?.crossplatformId).toBe('EOS_ada')
  })

  it('requests fit only for the first successful result of the same track query', async () => {
    const { controller } = createController()

    await controller.refreshTrack()
    const firstFit = controller.fitRequest.value
    await controller.refreshTrack()

    expect(firstFit).not.toBeNull()
    expect(controller.fitRequest.value).toBe(firstFit)
  })

  it('selects the first returned observation when the query has no restored selection', async () => {
    const { controller, replace } = createController({
      initialQuery: new URLSearchParams('player=EOS_ada&from=2026-07-25T00%3A00%3A00Z&to=2026-07-26T00%3A00%3A00Z'),
    })

    await controller.refreshTrack()

    expect(controller.selectedSnapshotId.value).toBe(1)
    expect(replace.mock.lastCall?.[0].get('observation')).toBe('1')
  })

  it('clears world-bound state and aborts the old track request before accepting a changed world', async () => {
    const nextMetadata = {
      ...metadata,
      worldId: 'world-random-gen',
      worldName: 'Random Gen',
      extent: { minimumX: -2048, minimumZ: -1024, maximumX: 2048, maximumZ: 1024 },
    }
    const fetchMetadata = vi.fn()
      .mockResolvedValueOnce(metadata)
      .mockResolvedValueOnce(nextMetadata)
    const oldWorldTrack = deferred<PlayerTrack>()
    const newWorldTrack = deferred<PlayerTrack>()
    const signals: AbortSignal[] = []
    const fetchTrack = vi.fn((_header, _filters, signal: AbortSignal) => {
      signals.push(signal)
      if (signals.length === 1)
        return Promise.resolve(track)
      return signals.length === 2 ? oldWorldTrack.promise : newWorldTrack.promise
    })
    const { controller } = createController({ fetchMetadata, fetchTrack })

    await controller.refresh()
    const oldRequest = controller.refreshTrack()
    const refreshForNewWorld = controller.refresh()
    await flushPromises()

    expect(signals[1]?.aborted).toBe(true)
    expect(controller.metadata.value?.worldId).toBe('world-random-gen')
    expect(controller.track.value).toBeNull()
    expect(controller.selectedSnapshotId.value).toBeNull()
    expect(controller.fitRequest.value).toBeNull()

    oldWorldTrack.resolve(track)
    newWorldTrack.resolve({ crossplatformId: 'EOS_ada', segments: [] })
    await Promise.all([oldRequest, refreshForNewWorld])
  })

  it('clears old-world online players when the new-world online refresh fails', async () => {
    const nextMetadata = {
      ...metadata,
      worldId: 'world-random-gen',
      worldName: 'Random Gen',
      extent: { minimumX: -2048, minimumZ: -1024, maximumX: 2048, maximumZ: 1024 },
    }
    const fetchMetadata = vi.fn()
      .mockResolvedValueOnce(metadata)
      .mockResolvedValueOnce(nextMetadata)
    const fetchOnline = vi.fn()
      .mockResolvedValueOnce(onlineSnapshot)
      .mockRejectedValueOnce(new Error('offline'))
    const { controller } = createController({ fetchMetadata, fetchOnline })

    await controller.refresh()
    expect(controller.onlinePlayers.value).toHaveLength(1)

    await controller.refresh()

    expect(controller.metadata.value?.worldId).toBe('world-random-gen')
    expect(controller.onlinePlayers.value).toEqual([])
    expect(controller.onlineState.value).toBe('failed')
  })

  it('removes the obsolete map and all world-bound state when metadata is explicitly unavailable', async () => {
    const unavailableMetadata = {
      availability: 'unavailable' as const,
      observedAtUtc: null,
      worldId: null,
      worldName: null,
      extent: null,
      axes: null,
      availableZoomLevels: null,
      tileSize: null,
      mapResourceVersion: null,
    }
    const fetchMetadata = vi.fn()
      .mockResolvedValueOnce(metadata)
      .mockResolvedValueOnce(unavailableMetadata)
    const fetchOnline = vi.fn().mockResolvedValue(onlineSnapshot)
    const fetchTrack = vi.fn().mockResolvedValue(track)
    const { controller } = createController({ fetchMetadata, fetchOnline, fetchTrack })

    await controller.refresh()
    expect(controller.metadata.value).not.toBeNull()
    expect(controller.track.value).not.toBeNull()

    await controller.refresh()

    expect(controller.metadata.value).toBeNull()
    expect(controller.onlinePlayers.value).toEqual([])
    expect(controller.track.value).toBeNull()
    expect(controller.selectedSnapshotId.value).toBeNull()
    expect(controller.fitRequest.value).toBeNull()
    expect(fetchTrack).toHaveBeenCalledOnce()
  })

  it('does not start downstream track work after the page becomes hidden', async () => {
    const pendingMetadata = deferred<MapMetadata>()
    let visible = true
    const visibilityListeners: Array<() => void> = []
    const visibility = {
      isVisible: () => visible,
      subscribe(listener: () => void) {
        visibilityListeners.push(listener)
        return () => {
          const index = visibilityListeners.indexOf(listener)
          if (index >= 0) {
            visibilityListeners.splice(index, 1)
          }
        }
      },
    }
    const fetchTrack = vi.fn().mockResolvedValue(track)
    const { controller } = createController({
      fetchMetadata: vi.fn().mockReturnValue(pendingMetadata.promise),
      fetchTrack,
      visibility,
    })

    controller.start()
    visible = false
    visibilityListeners[0]?.()
    pendingMetadata.resolve(metadata)
    await flushPromises()

    expect(fetchTrack).not.toHaveBeenCalled()
    controller.dispose()
  })

  it('reports a track-specific owner authorization failure', async () => {
    const { controller } = createController({
      fetchTrack: vi.fn().mockRejectedValue(new HttpError('http', 'forbidden', { status: 403 })),
    })

    await controller.refreshTrack()

    expect(controller.trackState.value).toBe('forbidden')
    expect(controller.state.value).toBe('forbidden')
  })

  it('maps a stale metadata envelope to the stale page state while keeping its world', async () => {
    const { controller } = createController({
      fetchMetadata: vi.fn().mockResolvedValue({ ...metadata, availability: 'stale' }),
    })

    await controller.refresh()

    expect(controller.metadata.value?.worldId).toBe('world-navezgane')
    expect(controller.state.value).toBe('stale')
  })

  it('refreshes game time every 30 seconds and keeps the last success as stale on failure', async () => {
    vi.useFakeTimers()
    const fetchGameTime = vi.fn()
      .mockResolvedValueOnce({ availability: 'available', day: 17, hour: 9, minute: 4, observedAtUtc: '2026-07-26T08:31:00Z' })
      .mockRejectedValueOnce(new Error('offline'))
    const { controller } = createController({ fetchGameTime })

    controller.start()
    await flushPromises()
    await vi.advanceTimersByTimeAsync(30_000)

    expect(fetchGameTime).toHaveBeenCalledTimes(2)
    expect(controller.gameTime.value?.day).toBe(17)
    expect(controller.gameTimeState.value).toBe('stale')
    controller.dispose()
  })
})
