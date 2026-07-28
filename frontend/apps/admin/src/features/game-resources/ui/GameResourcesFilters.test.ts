import ui from '@nuxt/ui/vue-plugin'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import GameResourcesFilters from './GameResourcesFilters.vue'

const filters = Object.freeze({
  search: '',
  kind: 'all' as const,
  includeHidden: false,
  page: 1,
  pageSize: 50,
})

function routerPlugin() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div />' } }],
  })
}

describe('gameResourcesFilters', () => {
  it('shows the hidden-resource filter only to Owners', () => {
    const owner = mount(GameResourcesFilters, {
      props: { count: 12, filters, isOwner: true, isRefreshing: false },
      global: { plugins: [routerPlugin(), ui] },
    })
    const viewer = mount(GameResourcesFilters, {
      props: { count: 12, filters, isOwner: false, isRefreshing: false },
      global: { plugins: [routerPlugin(), ui] },
    })

    expect(owner.text()).toContain('包含隐藏资源')
    expect(viewer.text()).not.toContain('包含隐藏资源')
    expect(viewer.text()).toContain('12')
  })

  it('emits search input and refresh through its public component contract', async () => {
    const wrapper = mount(GameResourcesFilters, {
      props: { count: 0, filters, isOwner: true, isRefreshing: false },
      global: { plugins: [routerPlugin(), ui] },
    })

    const search = wrapper.get('[data-testid="game-resource-search"]')
    expect(search.attributes()).toMatchObject({
      'aria-label': '搜索内部名称或本地化名称',
      'id': 'game-resource-search',
      'name': 'game-resource-search',
    })
    await search.setValue('steel')
    await wrapper.get('[data-testid="game-resource-refresh"]').trigger('click')

    expect(wrapper.emitted('search')?.slice(-1)[0]).toEqual(['steel'])
    expect(wrapper.emitted('refresh')).toHaveLength(1)
  })
})
