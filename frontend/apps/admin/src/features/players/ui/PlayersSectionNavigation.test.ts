import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { useAuthStore } from '../../auth'
import PlayersSectionNavigation from './PlayersSectionNavigation.vue'

it('links online, history and the protected player map as peer views', async () => {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.token = '7dp_t_test.secret'
  auth.expiresAt = Date.now() + 60_000
  auth.role = 'Owner'
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/players/', name: '/players/', component: { template: '<div />' }, meta: { requiresAuth: true } },
      { path: '/players/history/', name: '/players/history/', component: { template: '<div />' }, meta: { requiresAuth: true } },
      { path: '/players/map', name: '/players/map', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    ],
  })
  await router.push('/players/')
  await router.isReady()
  const wrapper = mount(PlayersSectionNavigation, {
    global: {
      plugins: [pinia, router],
      stubs: {
        SectionTabs: {
          props: ['items'],
          template: '<nav><a v-for="item in items" :key="item.id" :href="String(item.routeName)">{{ item.labelKey }}</a></nav>',
        },
      },
    },
  })

  expect(wrapper.get('a[href="/players/"]').text()).toBe('players.navigation')
  expect(wrapper.get('a[href="/players/history/"]').text()).toBe('players.profile.navigation')
  expect(wrapper.get('a[href="/players/map"]').text()).toBe('players.map.navigation')
})
