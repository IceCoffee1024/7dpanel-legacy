import type { NavigationEntryProjection } from '../../app/navigation/navigationTypes'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import SectionTabs from './SectionTabs.vue'

const items = [
  {
    id: 'live-chat',
    groupId: 'community',
    routeName: '/community/chat/live',
    labelKey: 'gameChat.title',
    icon: 'i-lucide-messages-square',
  },
  {
    id: 'chat-history',
    groupId: 'community',
    routeName: '/community/chat/history',
    labelKey: 'gameChat.history.title',
    icon: 'i-lucide-history',
  },
] as const satisfies readonly NavigationEntryProjection[]

function render(initialPath = '/community/chat/live') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/community/chat/live', name: '/community/chat/live', component: { template: '<main>live</main>' } },
      { path: '/community/chat/history', name: '/community/chat/history', component: { template: '<main>history</main>' } },
    ],
  })

  return router.push(initialPath).then(async () => {
    await router.isReady()
    return {
      router,
      wrapper: mount(SectionTabs, {
        props: { items },
        global: { plugins: [router], stubs: { UIcon: true } },
      }),
    }
  })
}

describe('sectionTabs', () => {
  it('renders typed route items as canonical links and identifies the current route', async () => {
    const { wrapper } = await render()
    const links = wrapper.findAll('a')

    expect(links).toHaveLength(2)
    expect(links[0]!.attributes('href')).toBe('/community/chat/live')
    expect(links[1]!.attributes('href')).toBe('/community/chat/history')
    expect(links[0]!.attributes('aria-current')).toBe('page')
    expect(links[1]!.attributes('aria-current')).toBeUndefined()
  })

  it('uses router navigation so the selected tab has its own URL and browser history entry', async () => {
    const { router, wrapper } = await render()

    await wrapper.findAll('a')[1]!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/community/chat/history')

    router.back()
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/community/chat/live')
  })
})
