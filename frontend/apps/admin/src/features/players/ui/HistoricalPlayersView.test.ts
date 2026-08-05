import type { HistoricalPlayersController } from '../model/useHistoricalPlayers'

import { mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import HistoricalPlayersView from './HistoricalPlayersView.vue'

const { useHistoricalPlayersMock } = vi.hoisted(() => ({ useHistoricalPlayersMock: vi.fn() }))

vi.mock('../model/useHistoricalPlayers', () => ({
  useHistoricalPlayers: useHistoricalPlayersMock,
}))

function mountView(state: HistoricalPlayersController['state']['value'] = 'ready') {
  const search = shallowRef('')
  const controller: HistoricalPlayersController = {
    state: readonly(shallowRef(state)),
    players: readonly(shallowRef([{
      crossplatformId: 'EOS_0002d12af0fe4add9c7de0fbc238d431',
      latestName: 'Ada',
      firstObservedAtUtc: '2026-07-22T08:00:00Z',
      lastObservedAtUtc: '2026-07-22T08:30:00Z',
      totalObservationCount: 8,
      retainedSnapshotCount: 5,
      compactedSnapshotCount: 3,
      hasGaps: true,
    }])),
    nextCursor: readonly(shallowRef('next-cursor')),
    search,
    errorCode: readonly(shallowRef(null)),
    isRefreshing: readonly(shallowRef(false)),
    isLoadingMore: readonly(shallowRef(false)),
    refresh: vi.fn().mockResolvedValue(undefined),
    loadMore: vi.fn().mockResolvedValue(undefined),
    retry: vi.fn().mockResolvedValue(undefined),
    dispose: vi.fn(),
  }
  useHistoricalPlayersMock.mockReturnValue(controller)
  return {
    controller,
    wrapper: mount(HistoricalPlayersView, {
      global: {
        stubs: {
          PlayersSectionNavigation: { template: '<nav />' },
          RouterLink: { props: ['to'], template: '<a :href="to"><slot /></a>' },
          UDashboardSidebarToggle: true,
          UButton: { props: ['label', 'to'], emits: ['click'], template: '<button :data-to="to" @click="$emit(\'click\')">{{ label }}<slot /></button>' },
          UInput: { props: ['modelValue'], emits: ['update:modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)">' },
        },
      },
    }),
  }
}

it('renders a searchable summary list, quality state and a load-more action', async () => {
  const { controller, wrapper } = mountView()

  expect(wrapper.text()).toContain('Ada')
  expect(wrapper.text()).toContain('存在历史缺口')
  expect(wrapper.text()).toContain('EOS_0002d12af0fe4add9c7de0fbc238d431')
  const search = wrapper.get('[data-testid="history-search"]')
  expect(search.attributes()).toMatchObject({
    'aria-label': '按名称或跨平台身份搜索',
    'id': 'historical-player-search',
    'name': 'historical-player-search',
  })
  await search.setValue('Ada')
  expect(controller.search.value).toBe('Ada')
  await wrapper.get('[data-testid="history-load-more"]').trigger('click')
  expect(controller.loadMore).toHaveBeenCalledOnce()
})

it('renders owner authorization failure without player entries', () => {
  const { wrapper } = mountView('forbidden')

  expect(wrapper.text()).toContain('无权查看历史玩家')
})
