import type { MapMetadata } from '../api/playerMap'

import { mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'

import OpenLayersGameMap from './OpenLayersGameMap.vue'

const { createRuntimeMock, runtimes } = vi.hoisted(() => ({
  createRuntimeMock: vi.fn(),
  runtimes: [] as Array<{
    dispose: ReturnType<typeof vi.fn>
    updateOnlinePlayers: ReturnType<typeof vi.fn>
    updateTrack: ReturnType<typeof vi.fn>
    updateSelection: ReturnType<typeof vi.fn>
    updateAreaGeometry: ReturnType<typeof vi.fn>
    updateAreaInteractionMode: ReturnType<typeof vi.fn>
    updateAreaResultSelection: ReturnType<typeof vi.fn>
    applyFit: ReturnType<typeof vi.fn>
    options: Record<string, (...args: never[]) => void>
  }>,
}))

vi.mock('./openLayersGameMapRuntime', () => ({
  createOpenLayersGameMapRuntime: createRuntimeMock,
}))

const firstMetadata: MapMetadata = {
  availability: 'available',
  observedAtUtc: '2026-07-26T08:29:00Z',
  worldId: 'world-navezgane',
  worldName: 'Navezgane',
  extent: { minimumX: -100, minimumZ: -100, maximumX: 100, maximumZ: 100 },
  axes: { xAxisDirection: 'east', zAxisDirection: 'north' },
  availableZoomLevels: [0, 1, 2, 3, 4],
  tileSize: 256,
  mapResourceVersion: null,
}

const oldTrack = {
  crossplatformId: 'EOS_ada',
  segments: [{ points: [
    { snapshotId: 1, name: 'Ada', x: 1, y: 2, z: 3, observedAtUtc: '2026-07-26T08:00:00Z' },
  ] }],
}

function makeRuntime(options: Record<string, (...args: never[]) => void>) {
  const runtime = {
    map: {},
    dispose: vi.fn(),
    updateOnlinePlayers: vi.fn(),
    updateTrack: vi.fn(),
    updateSelection: vi.fn(),
    updateAreaGeometry: vi.fn(),
    updateAreaInteractionMode: vi.fn(),
    updateAreaResultSelection: vi.fn(),
    applyFit: vi.fn(),
    options,
  }
  runtimes.push(runtime)
  return runtime
}

beforeEach(() => {
  createRuntimeMock.mockReset()
  runtimes.length = 0
})

it('keeps the same runtime and current track selection for same-signature metadata refreshes', async () => {
  createRuntimeMock.mockImplementation((options: Record<string, (...args: never[]) => void>) => makeRuntime(options))
  const fitRequest = { queryKey: 'current-world-query', extent: [0, 0, 2, 2] as const }
  const wrapper = mount(OpenLayersGameMap, {
    props: {
      metadata: firstMetadata,
      onlinePlayers: [],
      track: oldTrack,
      selectedSnapshotId: 1,
      selectedOnlineCombinedId: 'EOS_ada',
      fitRequest,
    },
  })
  const first = runtimes[0]!

  await wrapper.setProps({
    metadata: {
      ...firstMetadata,
      availability: 'stale',
      observedAtUtc: '2026-07-26T08:30:00Z',
    },
  })

  expect(createRuntimeMock).toHaveBeenCalledOnce()
  expect(first.dispose).not.toHaveBeenCalled()
  expect(first.options).toMatchObject({
    track: oldTrack,
    selectedSnapshotId: 1,
    selectedOnlineCombinedId: 'EOS_ada',
    fitRequest,
  })

  wrapper.unmount()
})

it('keeps area geometry, interaction mode and selected result controlled by props', async () => {
  createRuntimeMock.mockImplementation((options: Record<string, (...args: never[]) => void>) => makeRuntime(options))
  const geometry = { kind: 'rectangle', minimumX: 1, minimumZ: 2, maximumX: 3, maximumZ: 4 } as const
  const selectedPlayer = {
    combinedId: 'EOS_ada',
    displayName: 'Ada',
    firstMatchingObservation: { observedAtUtc: '2026-07-26T08:00:00Z' },
    lastMatchingObservation: { observedAtUtc: '2026-07-26T08:10:00Z', position: { x: 9, y: 2, z: 8 } },
    matchingObservationCount: 2,
  }
  const wrapper = mount(OpenLayersGameMap, {
    props: {
      metadata: firstMetadata,
      onlinePlayers: [],
      track: null,
      selectedSnapshotId: null,
      selectedOnlineCombinedId: null,
      fitRequest: null,
      areaGeometry: null,
      areaInteractionMode: null,
      selectedAreaPlayer: null,
    },
  })
  const current = runtimes[0]!

  await wrapper.setProps({
    areaGeometry: geometry,
    areaInteractionMode: 'modify',
    selectedAreaPlayer: selectedPlayer,
  })

  expect(current.updateAreaGeometry).toHaveBeenLastCalledWith(geometry)
  expect(current.updateAreaInteractionMode).toHaveBeenLastCalledWith('modify')
  expect(current.updateAreaResultSelection).toHaveBeenLastCalledWith(selectedPlayer)

  current.options.onAreaGeometryChange({ kind: 'circle', centerX: 3, centerZ: 4, radius: 5 } as never)
  expect(wrapper.emitted('updateAreaGeometry')?.[0]).toEqual([
    { kind: 'circle', centerX: 3, centerZ: 4, radius: 5 },
  ])
  wrapper.unmount()
})

it('rebuilds a same-world resource version with all current props intact', async () => {
  createRuntimeMock.mockImplementation((options: Record<string, (...args: never[]) => void>) => makeRuntime(options))
  const fitRequest = { queryKey: 'current-world-query', extent: [0, 0, 2, 2] as const }
  const wrapper = mount(OpenLayersGameMap, {
    props: {
      metadata: firstMetadata,
      onlinePlayers: [],
      track: oldTrack,
      selectedSnapshotId: 1,
      selectedOnlineCombinedId: 'EOS_ada',
      fitRequest,
    },
  })

  await wrapper.setProps({
    metadata: { ...firstMetadata, mapResourceVersion: 'map-v2' },
  })

  expect(createRuntimeMock).toHaveBeenCalledTimes(2)
  expect(createRuntimeMock.mock.calls[1]?.[0]).toMatchObject({
    track: oldTrack,
    selectedSnapshotId: 1,
    selectedOnlineCombinedId: 'EOS_ada',
    fitRequest,
  })

  wrapper.unmount()
})

it('rebuilds for changed metadata, routes selections and disposes every world runtime', async () => {
  createRuntimeMock.mockImplementation((options: Record<string, (...args: never[]) => void>) => makeRuntime(options))
  const wrapper = mount(OpenLayersGameMap, {
    props: {
      metadata: firstMetadata,
      onlinePlayers: [],
      track: oldTrack,
      selectedSnapshotId: 1,
      selectedOnlineCombinedId: 'EOS_ada',
      fitRequest: { queryKey: 'old-world-query', extent: [0, 0, 2, 2] },
    },
  })
  const first = runtimes[0]!
  first.options.onPointerCoordinate({ x: 1, z: 2 } as never)
  first.options.onSelectedCoordinate({ x: 3, z: 4 } as never)
  first.options.onSelectOnlinePlayer('EOS_ada' as never)
  await wrapper.vm.$nextTick()

  expect(wrapper.get('[data-testid="pointer-coordinate"]').text()).toContain('X 1.0 · Z 2.0')
  expect(wrapper.get('[data-testid="selected-coordinate"]').text()).toContain('X 3.0 · Z 4.0')
  expect(wrapper.emitted('selectOnlinePlayer')?.[0]).toEqual(['EOS_ada'])

  await wrapper.setProps({
    metadata: {
      ...firstMetadata,
      worldId: 'world-random-gen',
      worldName: 'Random Gen',
      extent: { minimumX: -500, minimumZ: -600, maximumX: 700, maximumZ: 800 },
      mapResourceVersion: 'world-18',
    },
  })

  expect(first.dispose).toHaveBeenCalledOnce()
  expect(createRuntimeMock).toHaveBeenCalledTimes(2)
  expect(createRuntimeMock.mock.calls[1]?.[0].metadata.worldName).toBe('Random Gen')
  expect(createRuntimeMock.mock.calls[1]?.[0]).toMatchObject({
    track: null,
    selectedSnapshotId: null,
    selectedOnlineCombinedId: null,
    fitRequest: null,
  })
  expect(wrapper.find('[data-testid="pointer-coordinate"]').exists()).toBe(false)
  expect(wrapper.find('[data-testid="selected-coordinate"]').exists()).toBe(false)

  wrapper.unmount()
  expect(runtimes[1]?.dispose).toHaveBeenCalledOnce()
})
