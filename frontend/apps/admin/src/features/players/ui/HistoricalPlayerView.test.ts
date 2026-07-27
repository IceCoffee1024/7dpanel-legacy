import type { HistoricalPlayerSnapshot } from '../api/historyPlayers'
import type { HistoricalPlayerController } from '../model/useHistoricalPlayer'

import { mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import HistoricalPlayerView from './HistoricalPlayerView.vue'

const { useHistoricalPlayerMock } = vi.hoisted(() => ({ useHistoricalPlayerMock: vi.fn() }))

vi.mock('../model/useHistoricalPlayer', () => ({
  useHistoricalPlayer: useHistoricalPlayerMock,
}))

vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal<typeof import('vue-router')>(),
  useRoute: () => ({ params: { crossplatformId: 'EOS_ada' } }),
}))

function mountView() {
  const snapshot: HistoricalPlayerSnapshot = {
    snapshotId: 19,
    player: {
      entityId: 42,
      name: 'Ada',
      platformIdentity: { combinedId: 'Steam_ada', platform: 'Steam' },
      crossplatformIdentity: { combinedId: 'EOS_ada', platform: 'EOS' },
      deviceType: 'windows',
      ip: null,
      ping: 23,
      compatibilityVersion: null,
      discordUserId: null,
      permissionLevel: 1000,
      position: { x: 1, y: 2, z: 3 },
      isDead: false,
      health: 96,
      maxHealth: 100,
      level: 17,
      playGroup: null,
      lastLoginUtc: null,
      gameStage: null,
      expToNextLevel: null,
      skillPoints: null,
      bedroll: null,
      score: 827,
      zombieKills: 317,
      playerKills: 2,
      deaths: 4,
      totalTimePlayedMinutes: 1,
      distanceWalkedMeters: 2,
      totalItemsCrafted: 3,
      longestLifeMinutes: 4,
      currentLifeMinutes: 5,
      observedAtUtc: '2026-07-22T08:30:00Z',
    },
  }
  const controller: HistoricalPlayerController = {
    state: readonly(shallowRef('ready')),
    details: readonly(shallowRef({
      player: { crossplatformId: 'EOS_ada', latestName: 'Ada', firstObservedAtUtc: '2026-07-22T08:00:00Z', lastObservedAtUtc: '2026-07-22T08:30:00Z', totalObservationCount: 8, retainedSnapshotCount: 5, compactedSnapshotCount: 3, hasGaps: true },
      gapSummary: { gapCount: 1, droppedObservationCount: 3 },
    })),
    snapshots: readonly(shallowRef([snapshot])),
    gaps: readonly(shallowRef([{ gapId: 'gap-1', crossplatformId: 'EOS_ada', startedAtUtc: '2026-07-22T08:10:00Z', completedAtUtc: '2026-07-22T08:12:00Z', droppedCount: 3, reason: 'queue_full' as const, recordedAtUtc: '2026-07-22T08:12:01Z' }])),
    nextBeforeSnapshotId: readonly(shallowRef(null)),
    errorCode: readonly(shallowRef(null)),
    isRefreshing: readonly(shallowRef(false)),
    isLoadingMore: readonly(shallowRef(false)),
    refresh: vi.fn().mockResolvedValue(undefined),
    loadMore: vi.fn().mockResolvedValue(undefined),
    retry: vi.fn().mockResolvedValue(undefined),
    dispose: vi.fn(),
  }
  useHistoricalPlayerMock.mockReturnValue(controller)
  return {
    controller,
    wrapper: mount(HistoricalPlayerView, {
      global: {
        stubs: {
          Button: {
            props: ['label', 'to'],
            emits: ['click'],
            template: '<a v-if="to" :href="to">{{ label }}<slot /></a><button v-else @click="$emit(\'click\')">{{ label }}<slot /></button>',
          },
          PlayersSectionNavigation: true,
          HistoricalSnapshotTimeline: { props: ['snapshots', 'gaps'], emits: ['selectSnapshot', 'loadMore'], template: '<button data-testid="snapshot-row" @click="$emit(\'selectSnapshot\', snapshots[0])">{{ snapshots[0]?.player.name }}</button>' },
          HistoricalSnapshotDetailsSlideover: { props: ['open', 'snapshot'], emits: ['update:open'], template: '<section v-if="open" data-testid="snapshot-details">{{ snapshot?.snapshotId }}</section>' },
        },
      },
    }),
  }
}

it('renders summary and timeline data, then opens a read-only snapshot detail', async () => {
  const { wrapper } = mountView()

  expect(wrapper.text()).toContain('Ada')
  expect(wrapper.text()).toContain('1')
  expect(wrapper.get('a[href="/players/profile/EOS_ada"]').text()).toBe('查看只读档案')
  await wrapper.get('[data-testid="snapshot-row"]').trigger('click')
  expect(wrapper.get('[data-testid="snapshot-details"]').text()).toContain('19')
  expect(wrapper.text()).not.toMatch(/踢出|封禁|传送|重置/)
})
