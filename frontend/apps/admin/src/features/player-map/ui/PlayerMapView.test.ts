import type { PlayerMapController, PlayerMapPageState } from '../model/usePlayerMap'

import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import { useAuthStore } from '../../auth'
import MapAreaInvestigation from './MapAreaInvestigation.vue'
import PlayerMapView from './PlayerMapView.vue'

const { pushMock, replaceMock, usePlayerMapMock } = vi.hoisted(() => ({
  pushMock: vi.fn(),
  replaceMock: vi.fn(),
  usePlayerMapMock: vi.fn(),
}))

vi.mock('../model/usePlayerMap', () => ({ usePlayerMap: usePlayerMapMock }))
vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ push: pushMock, replace: replaceMock }),
}))

const observations = [{ points: [
  { snapshotId: 1, name: 'Ada', x: 1, y: 2, z: 3, observedAtUtc: '2026-07-26T08:00:00Z' },
  { snapshotId: 2, name: 'Ada', x: 4, y: 5, z: 6, observedAtUtc: '2026-07-26T08:10:00Z' },
] }]

const mapMetadata = {
  availability: 'available' as const,
  observedAtUtc: '2026-07-26T08:29:00Z',
  worldId: 'world-navezgane',
  worldName: 'Navezgane',
  extent: { minimumX: -100, minimumZ: -100, maximumX: 100, maximumZ: 100 },
  axes: { xAxisDirection: 'east' as const, zAxisDirection: 'north' as const },
  availableZoomLevels: [0, 1, 2],
  tileSize: 256,
  mapResourceVersion: null,
}

function mountState(
  state: PlayerMapPageState,
  withTrack = false,
  withMetadata = false,
  selectedSnapshotId: number | null = withTrack ? 1 : null,
) {
  const metadata = shallowRef(withMetadata ? mapMetadata : null)
  const controller = {
    state: readonly(shallowRef(state)),
    metadata: readonly(metadata),
    onlinePlayers: readonly(shallowRef(withMetadata
      ? [{
          combinedId: 'EOS_ada',
          name: 'Ada',
          position: { x: 1, y: 2, z: 3 },
          observedAtUtc: '2026-07-26T08:00:00Z',
        }]
      : [])),
    onlineState: readonly(shallowRef('ready')),
    historicalPlayers: readonly(shallowRef([])),
    historyState: readonly(shallowRef('failed')),
    playerSearch: shallowRef(''),
    track: readonly(shallowRef(withTrack ? { crossplatformId: 'EOS_ada', segments: observations } : null)),
    trackState: readonly(shallowRef(withTrack ? 'ready' : 'empty')),
    observationCount: readonly(shallowRef(withTrack ? 2 : 0)),
    gameTime: readonly(shallowRef(null)),
    gameTimeState: readonly(shallowRef('unavailable')),
    filters: readonly(shallowRef({ player: null, fromUtc: null, toUtc: null })),
    selectedSnapshotId: readonly(shallowRef(selectedSnapshotId)),
    fitRequest: readonly(shallowRef(null)),
    setPlayer: vi.fn(),
    setRange: vi.fn(),
    searchHistoricalPlayers: vi.fn().mockResolvedValue(undefined),
    selectObservation: vi.fn(),
    refresh: vi.fn(),
    refreshTrack: vi.fn(),
    start: vi.fn(),
    dispose: vi.fn(),
  } as unknown as PlayerMapController
  usePlayerMapMock.mockReturnValue(controller)
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.token = '7dp_t_player-map.secret'
  auth.expiresAt = Date.now() + 60_000
  const wrapper = mount(PlayerMapView, {
    global: {
      plugins: [pinia],
      stubs: {
        PlayersSectionNavigation: true,
        OpenLayersGameMap: {
          props: ['selectedOnlineCombinedId', 'areaGeometry', 'areaInteractionMode', 'selectedAreaPlayer'],
          emits: ['updateAreaGeometry'],
          template: `<div data-testid="game-map" :data-selected-online="selectedOnlineCombinedId" :data-area-kind="areaGeometry?.kind" :data-area-mode="areaInteractionMode" :data-area-player="selectedAreaPlayer?.combinedId">
            <button data-testid="map-draw-circle" type="button" @click="$emit('updateAreaGeometry', { kind: 'circle', centerX: 10, centerZ: 20, radius: 30 })">area</button>
          </div>`,
        },
        OnlinePlayerMapList: {
          template: '<button data-testid="select-online" type="button" @click="$emit(\'select\', \'EOS_ada\')">Ada</button>',
        },
        PlayerTrackObservations: true,
        Button: { props: ['label'], template: '<button type="button">{{ label }}</button>' },
        DashboardPanel: { template: '<div><slot name="header" /><slot name="body" /></div>' },
      },
    },
  })
  return { controller, metadata, wrapper }
}

describe('playerMapView', () => {
  it.each<PlayerMapPageState>(['loading', 'ready', 'empty', 'partial', 'stale', 'forbidden', 'failed'])(
    'renders an honest %s page state',
    (state) => {
      const { wrapper } = mountState(state)
      expect(wrapper.get(`[data-state="${state}"]`).attributes('data-state')).toBe(state)
    },
  )

  it('uses narrow-screen-safe structure, keeps key controls operable and exposes no dangerous action', async () => {
    const { wrapper } = mountState('ready')
    const layout = wrapper.get('[data-testid="player-map-layout"]')

    expect(layout.classes()).toEqual(expect.arrayContaining(['min-w-0', 'max-w-full', 'overflow-x-hidden']))
    expect(wrapper.get('[data-testid="player-map-filters"]').classes()).toEqual(expect.arrayContaining(['grid-cols-1', 'min-w-0', 'max-w-full']))
    await wrapper.get('[data-testid="player-search"]').setValue('Ada')
    expect(wrapper.text()).toContain('轨迹由已保留的位置观察组成')
    expect(wrapper.text()).toContain('历史玩家：不可用')
    expect(wrapper.text()).not.toMatch(/删除|传送|渲染/)
    expect(wrapper.get('[data-testid="load-track"]').attributes('type')).toBe('button')
  })

  it('keeps slider, map and list on the same selected observation', async () => {
    const { controller, wrapper } = mountState('ready', true)

    expect(wrapper.get('[data-testid="observation-time-control"]').element).toHaveProperty('value', '0')
    expect(wrapper.get('[data-testid="selected-observed-at"]').text()).toContain('2026')
    await wrapper.get('[data-testid="observation-time-control"]').setValue('1')
    expect(controller.selectObservation).toHaveBeenCalledWith(2)
  })

  it('disables the slider instead of presenting index zero when no observation is selected', () => {
    const { wrapper } = mountState('ready', true, false, null)

    expect(wrapper.get('[data-testid="observation-time-control"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="selected-observed-at"]').text()).toContain('尚未选择')
  })

  it('clears the online selection when the map world identity changes', async () => {
    const { metadata, wrapper } = mountState('ready', false, true)

    await wrapper.get('[data-testid="select-online"]').trigger('click')
    expect(wrapper.get('[data-testid="game-map"]').attributes('data-selected-online')).toBe('EOS_ada')

    metadata.value = {
      ...mapMetadata,
      worldId: 'world-random-gen',
      extent: { minimumX: -200, minimumZ: -300, maximumX: 400, maximumZ: 500 },
    }
    await wrapper.vm.$nextTick()

    expect(wrapper.get('[data-testid="game-map"]').attributes('data-selected-online')).toBeUndefined()
  })

  it('integrates area drawing, URL-backed controller state, history navigation and track loading', async () => {
    const { controller, wrapper } = mountState('ready', false, true)
    const area = wrapper.findComponent(MapAreaInvestigation)

    await wrapper.get('[data-testid="area-mode-circle"]').trigger('click')
    expect(wrapper.get('[data-testid="game-map"]').attributes('data-area-mode')).toBe('draw-circle')

    await wrapper.get('[data-testid="map-draw-circle"]').trigger('click')
    expect(wrapper.get('[data-testid="game-map"]').attributes('data-area-kind')).toBe('circle')
    expect(wrapper.get('[data-testid="game-map"]').attributes('data-area-mode')).toBeUndefined()
    expect(replaceMock).toHaveBeenCalled()

    const investigation = area.props('investigation')
    investigation.setTimeRange('2026-07-26T08:00:00.000Z', '2026-07-26T09:00:00.000Z')
    area.vm.$emit('loadHistoryTrack', 'EOS_ada')
    await wrapper.vm.$nextTick()
    expect(controller.setPlayer).toHaveBeenCalledWith('EOS_ada')
    expect(controller.setRange).toHaveBeenCalledWith('2026-07-26T08:00:00.000Z', '2026-07-26T09:00:00.000Z')
    expect(controller.refreshTrack).toHaveBeenCalled()

    area.vm.$emit('openHistoryProfile', 'EOS_ada')
    expect(pushMock).toHaveBeenCalledWith('/players/history/EOS_ada')
  })
})
