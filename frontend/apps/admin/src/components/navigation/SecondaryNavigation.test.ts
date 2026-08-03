import type { NavigationEntryProjection } from '../../app/navigation/navigationTypes'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import SecondaryNavigation from './SecondaryNavigation.vue'

const items = [
  {
    id: 'server',
    groupId: 'operations',
    routeName: '/operations/server',
    labelKey: 'serverOperations.title',
    icon: 'i-lucide-server',
  },
] as const satisfies readonly NavigationEntryProjection[]

describe('secondary navigation', () => {
  it('exposes a canonical anchor and delegates navigation exactly once', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/operations/server', name: '/operations/server', component: { template: '<main />' } },
      ],
    })
    await router.push('/operations/server')
    await router.isReady()

    const wrapper = mount(SecondaryNavigation, {
      props: { items },
      global: { plugins: [router], stubs: { UIcon: true } },
    })
    const link = wrapper.get('a')

    expect(link.attributes('href')).toBe('/operations/server')
    await link.trigger('click')
    await flushPromises()

    expect(wrapper.emitted('select')).toEqual([['/operations/server']])
    expect(router.currentRoute.value.fullPath).toBe('/operations/server')
  })
})
