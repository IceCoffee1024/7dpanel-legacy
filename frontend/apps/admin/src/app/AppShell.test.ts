import type { NavigationRouteName } from './navigation/navigationTypes'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import { createMemoryHistory, createRouter } from 'vue-router'
import { useAuthStore } from '../features/auth'
import AppShell from './AppShell.vue'
import { navigationCatalog } from './navigation/navigationCatalog'

const shortcuts = vi.hoisted(() => ({
  value: {} as Record<string, () => unknown>,
}))

vi.mock('@nuxt/ui/composables', async (importOriginal) => {
  const original = await importOriginal<typeof import('@nuxt/ui/composables')>()
  return {
    ...original,
    defineShortcuts(definitions: Record<string, () => unknown>) {
      shortcuts.value = definitions
    },
  }
})

const ownerOnly = new Set<NavigationRouteName>([
  '/operations/server',
  '/operations/backups',
  '/operations/automation/schedules',
  '/operations/automation/rules',
  '/operations/configuration',
  '/operations/extensions/mods',
  '/operations/extensions/modules',
  '/operations/world',
  '/players/history/',
  '/players/map',
  '/community/chat/live',
  '/community/chat/history',
  '/community/chat/mutes',
  '/community/chat/settings',
  '/community/chat/appearance',
  '/community/teleport',
  '/community/votes',
  '/community/cities',
  '/economy/accounts',
  '/economy/transactions',
  '/economy/rewards/packages',
  '/economy/rewards/daily',
  '/economy/rewards/operations',
  '/economy/rewards/achievements',
  '/economy/commerce/shop',
  '/economy/commerce/redeem-codes',
  '/system/access',
  '/system/integrations/discord',
  '/system/integrations/geoip',
  '/system/audit',
  '/players/history/[crossplatformId]',
  '/players/profile/[crossplatformId]',
])

function routeMeta(routeName: NavigationRouteName) {
  if (ownerOnly.has(routeName))
    return { requiresAuth: true, roles: ['Owner'] }
  if (routeName === '/operations/console')
    return { requiresAuth: true, roles: ['Owner', 'Admin'] }
  return { requiresAuth: true }
}

function createRouteRecords() {
  const routeNames = [
    ...navigationCatalog.groups.flatMap(group => group.children.map(child => child.routeName)),
    ...navigationCatalog.routeParents.map(parent => parent.routeName),
  ]
  return routeNames.map((name, index) => ({
    name,
    path: name === '/' ? '/' : `/test-${index}`,
    component: { template: '<div />' },
    meta: routeMeta(name),
  }))
}

async function mountAppShell(initialRouteName: NavigationRouteName = '/players/') {
  localStorage.clear()
  sessionStorage.clear()
  const pinia = createPinia()
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      ...createRouteRecords(),
      { name: '/login', path: '/login', component: { template: '<div />' } },
    ] as never,
  })
  await router.push({ name: initialRouteName } as never)
  await router.isReady()

  const auth = useAuthStore(pinia)
  auth.token = '7dp_t_id.secret'
  auth.expiresAt = Date.now() + 60_000
  auth.username = 'server-owner'
  auth.role = 'Owner'

  const wrapper = mount(AppShell, {
    global: {
      plugins: [pinia, router],
      stubs: {
        AppBrand: true,
        AppearanceMenu: true,
        LocaleMenu: true,
        RouterView: true,
        UButton: {
          props: ['label'],
          emits: ['click'],
          template: '<button :aria-label="label" @click="$emit(\'click\')"><slot />{{ label }}</button>',
        },
        UDashboardGroup: { template: '<div><slot /></div>' },
        UDashboardSidebar: { template: '<aside><slot name="header" :collapsed="false" /><slot :collapsed="false" /><slot name="footer" :collapsed="false" /></aside>' },
        UDashboardSearchButton: {
          props: ['label'],
          emits: ['click'],
          template: '<button data-testid="search-button" @click="$emit(\'click\')">{{ label }}</button>',
        },
        UDashboardSearch: {
          props: ['groups', 'open'],
          template: '<div data-testid="dashboard-search" :data-open="open"><button v-for="item in groups[0]?.items" :key="item.label" @click="item.onSelect($event)">{{ item.label }}</button></div>',
        },
        UDropdownMenu: { template: '<div><slot /></div>' },
      },
    },
  })

  return { auth, router, wrapper }
}

describe('appShell', () => {
  it('renders the six task domains for Owner from the shared navigation projection', async () => {
    const { wrapper } = await mountAppShell()

    const primary = wrapper.get('[data-testid="primary-navigation"]')
    expect(primary.text()).toContain('概览')
    expect(primary.text()).toContain('服务器运维')
    expect(primary.text()).toContain('玩家')
    expect(primary.text()).toContain('社区')
    expect(primary.text()).toContain('经济与奖励')
    expect(primary.text()).toContain('系统管理')
  })

  it('updates visible domains and secondary entries from the authenticated role', async () => {
    const { auth, wrapper } = await mountAppShell()

    auth.role = 'Viewer'
    await nextTick()

    const primary = wrapper.get('[data-testid="primary-navigation"]')
    expect(primary.text()).toContain('概览')
    expect(primary.text()).toContain('玩家')
    expect(primary.text()).toContain('系统管理')
    expect(primary.text()).not.toContain('服务器运维')
    expect(primary.text()).not.toContain('社区')
    expect(primary.text()).not.toContain('经济与奖励')
    expect(wrapper.get('[data-testid="secondary-navigation"]').text()).not.toContain('玩家档案与证据')
  })

  it('uses the catalog parent chain to activate the deep-link task domain and breadcrumbs', async () => {
    const { wrapper } = await mountAppShell('/community/chat/history')

    expect(wrapper.get('[data-testid="primary-navigation"] [aria-current="page"]').text()).toContain('社区')
    expect(wrapper.get('[data-testid="app-breadcrumbs"]').text()).toContain('游戏聊天')
    expect(wrapper.get('[data-testid="app-breadcrumbs"]').text()).toContain('历史聊天')
  })

  it('localizes labels without changing the account identity or route projection', async () => {
    const { wrapper } = await mountAppShell()

    wrapper.vm.$i18n.locale = 'en'
    await nextTick()

    expect(wrapper.get('[data-testid="primary-navigation"]').text()).toContain('Overview')
    expect(wrapper.get('[data-testid="primary-navigation"]').text()).toContain('System management')
    expect(wrapper.text()).toContain('server-owner')
    expect(wrapper.get('[data-testid="account-menu-trigger"]').attributes('aria-label')).toBe('server-owner account')
  })

  it('derives search selection and shortcuts from the same accessible projection', async () => {
    const { router, wrapper } = await mountAppShell()

    const search = wrapper.findComponent({ name: 'DashboardSearch' })
    const groups = search.props('groups') as Array<{ items: Array<{ label: string, onSelect: (event: Event) => Promise<void> }> }>
    const destination = groups[0]?.items.find(item => item.label === '游戏资源')
    await destination?.onSelect(new Event('select'))
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('/players/resources')

    await shortcuts.value['g-p']?.()
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('/players/')
  })
})
