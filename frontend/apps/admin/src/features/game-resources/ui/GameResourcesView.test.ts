import type { GameResourcePage, GameResourceViewState } from '../api/gameResources'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, readonly, shallowRef } from 'vue'

import { createMemoryHistory, createRouter } from 'vue-router'
import GameResourcesView from './GameResourcesView.vue'

const useGameResourcesMock = vi.hoisted(() => vi.fn())

vi.mock('../model/useGameResources', () => ({
  useGameResources: useGameResourcesMock,
}))

function page(overrides: Partial<GameResourcePage> = {}): GameResourcePage {
  return Object.freeze({
    catalogVersion: 'catalog-1',
    gameVersion: 'v3.0.1-b4',
    observedAtUtc: '2026-07-26T08:00:00Z',
    total: 1,
    page: 1,
    pageSize: 50,
    warnings: Object.freeze([]),
    items: Object.freeze([Object.freeze({
      resourceId: 'resource-1',
      numericId: 1,
      internalName: 'resourceStone',
      localizedName: '石头',
      kind: 'item' as const,
      visibility: 'public' as const,
      maxStack: 6000,
      hasQuality: false,
      iconStatus: 'missing' as const,
      iconTintHex: null,
    })]),
    ...overrides,
  })
}

function controller(stateValue: GameResourceViewState, pageValue: GameResourcePage | null) {
  const state = shallowRef(stateValue)
  const currentPage = shallowRef(pageValue)
  const filters = shallowRef(Object.freeze({
    search: '',
    kind: 'all' as const,
    includeHidden: false,
    page: 1,
    pageSize: 50,
  }))
  return {
    clearFilters: vi.fn(),
    dispose: vi.fn(),
    filters: readonly(filters),
    isRefreshing: readonly(shallowRef(false)),
    page: readonly(currentPage),
    refresh: vi.fn(),
    retry: vi.fn(),
    setIncludeHidden: vi.fn(),
    setKind: vi.fn(),
    setPage: vi.fn(),
    setSearch: vi.fn(),
    state: readonly(state),
    totalPages: computed(() => currentPage.value === null ? 0 : Math.ceil(currentPage.value.total / currentPage.value.pageSize)),
  }
}

async function mountView() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/login', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  return mount(GameResourcesView, {
    global: {
      plugins: [createPinia(), router],
      stubs: {
        GameResourcesFilters: true,
        GameResourcesList: {
          props: ['items'],
          template: '<div data-testid="mobile-items"><span v-for="item in items" :key="item.resourceId">{{ item.internalName }}</span></div>',
        },
        GameResourcesTable: {
          props: ['items'],
          template: '<div data-testid="desktop-items"><span v-for="item in items" :key="item.resourceId">{{ item.internalName }}</span></div>',
        },
        UAlert: { props: ['title', 'description'], template: '<div role="alert">{{ title }} {{ description }}</div>' },
        UButton: { props: ['label'], template: '<button><slot />{{ label }}</button>' },
        UDashboardNavbar: { props: ['title'], template: '<header>{{ title }}<slot name="right" /></header>' },
        UDashboardPanel: { template: '<section><slot name="header" /><slot name="body" /><slot name="footer" /></section>' },
        UPagination: true,
        USkeleton: { template: '<div data-testid="skeleton" />' },
      },
    },
  })
}

describe('gameResourcesView', () => {
  beforeEach(() => {
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
    })
  })

  it.each([
    ['loading', '正在加载游戏资源'],
    ['building', '游戏资源目录正在构建'],
    ['unavailable', '游戏资源目录暂不可用'],
    ['forbidden', '无权查看这些游戏资源'],
    ['empty', '没有匹配的游戏资源'],
  ] as const)('renders the %s state', async (state, message) => {
    useGameResourcesMock.mockReturnValue(controller(state, state === 'empty' ? page({ total: 0, items: Object.freeze([]) }) : null))

    const wrapper = await mountView()

    expect(wrapper.text()).toContain(message)
  })

  it.each(['success', 'stale', 'partial'] as const)('keeps read-only rows visible in %s', async (state) => {
    const current = page({
      warnings: state === 'partial' ? Object.freeze(['game-resource-localization-partial']) : Object.freeze([]),
    })
    useGameResourcesMock.mockReturnValue(controller(state, current))

    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="desktop-items"]').text()).toContain('resourceStone')
    expect(wrapper.get('[data-testid="mobile-items"]').text()).toContain('resourceStone')
    expect(wrapper.text()).toContain('catalog-1')
    expect(wrapper.text()).not.toMatch(/发放|删除|商店|奖励|自动化/)
  })
})
