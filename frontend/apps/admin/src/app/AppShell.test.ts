import ui from '@nuxt/ui/vue-plugin'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { useAuthStore } from '../features/auth'
import AppShell from './AppShell.vue'

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

async function mountAppShell() {
  localStorage.clear()
  sessionStorage.clear()
  const pinia = createPinia()
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/players', component: { template: '<div />' } },
      { path: '/game-resources', component: { template: '<div />' } },
      { path: '/api-keys', component: { template: '<div />' } },
      { path: '/console-logs', component: { template: '<div />' } },
      { path: '/game-chat/live', component: { template: '<div />' } },
      { path: '/game-chat/history', component: { template: '<div />' } },
      { path: '/game-chat/settings', component: { template: '<div />' } },
      { path: '/game-chat/colored', component: { template: '<div />' } },
      { path: '/server-configuration', component: { template: '<div />' } },
      { path: '/access-lists', component: { template: '<div />' } },
      { path: '/permissions', component: { template: '<div />' } },
      { path: '/mods', component: { template: '<div />' } },
      { path: '/login', component: { template: '<div />' } },
    ],
  })
  await router.push('/players')
  await router.isReady()
  const auth = useAuthStore(pinia)
  auth.token = '7dp_t_id.secret'
  auth.expiresAt = Date.now() + 60_000
  auth.username = 'server-owner'
  auth.role = 'Owner'

  const wrapper = mount(AppShell, {
    global: {
      plugins: [pinia, router, ui],
      stubs: {
        AppBrand: true,
        AppearanceMenu: true,
        RouterView: true,
        UDashboardGroup: { template: '<div><slot /></div>' },
        UDashboardSidebar: { template: '<aside><slot name="header" :collapsed="false" /><slot :collapsed="false" /><slot name="footer" :collapsed="false" /></aside>' },
        UDashboardSearch: {
          props: ['groups'],
          template: '<div data-testid="dashboard-search"><span v-for="group in groups" :key="group.id"><span v-for="item in group.items" :key="item.label">{{ item.label }}</span></span></div>',
        },
        UDashboardSearchButton: true,
        UDropdownMenu: {
          props: ['items'],
          template: '<div><slot /><span v-for="group in items" :key="group[0]?.label"><span v-for="item in group" :key="item.label">{{ item.label }}</span></span></div>',
        },
        UNavigationMenu: {
          props: ['items'],
          template: '<nav><section v-for="item in items" :key="item.label" :data-nav-label="item.label">{{ item.label }}<span v-for="child in item.children" :key="child.label">{{ child.label }}</span></section></nav>',
        },
      },
    },
  })

  return { auth, router, wrapper }
}

describe('appShell', () => {
  it('displays the server-confirmed account identity in the account menu', async () => {
    const { wrapper } = await mountAppShell()

    const trigger = wrapper.get('[data-testid="account-menu-trigger"]')
    expect(trigger.text()).toContain('server-owner')
    expect(trigger.attributes('aria-label')).toBe('server-owner 账号')
    expect(wrapper.text()).toContain('server-owner')
    await trigger.trigger('click')
    expect(document.body.textContent).toContain('Owner')
  })

  it('logs out from the account menu and returns to login', async () => {
    const { auth, router, wrapper } = await mountAppShell()

    await wrapper.get('[data-testid="account-menu-trigger"]').trigger('click')
    const logout = [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')]
      .find(item => item.textContent?.includes('退出登录'))
    expect(logout).toBeDefined()
    logout?.click()
    await flushPromises()

    expect(auth.isAuthenticated).toBe(false)
    expect(router.currentRoute.value.fullPath).toBe('/login')
  })

  it('switches navigation and account commands to English without translating identity', async () => {
    const { wrapper } = await mountAppShell()

    wrapper.vm.$i18n.locale = 'en'
    await nextTick()

    expect(wrapper.text()).toContain('Overview')
    expect(wrapper.text()).toContain('Players')
    expect(wrapper.get('[data-testid="account-menu-trigger"]').attributes('aria-label'))
      .toBe('server-owner account')

    await wrapper.get('[data-testid="account-menu-trigger"]').trigger('click')
    await nextTick()

    expect(document.body.textContent).toContain('Sign out')
    expect(document.body.textContent).toContain('Owner')
  })

  it('shows the console navigation only to Owner and Admin roles', async () => {
    const { auth, wrapper } = await mountAppShell()

    expect(wrapper.text()).toContain('网页控制台')
    auth.role = 'Admin'
    await nextTick()
    expect(wrapper.text()).toContain('网页控制台')
    auth.role = 'Viewer'
    await nextTick()
    expect(wrapper.text()).not.toContain('网页控制台')
  })

  it('shows game chat and all four destinations only to Owner', async () => {
    const { auth, wrapper } = await mountAppShell()

    expect(wrapper.text()).toContain('游戏聊天')

    auth.role = 'Admin'
    await nextTick()
    expect(wrapper.text()).not.toContain('游戏聊天')
    expect(wrapper.text()).not.toContain('实时聊天')

    auth.role = 'Viewer'
    await nextTick()
    expect(wrapper.text()).not.toContain('游戏聊天')
  })

  it.each(['Owner', 'Admin', 'Viewer'] as const)('groups Players and Game resources for %s and flattens both into search', async (role) => {
    const { auth, wrapper } = await mountAppShell()
    auth.role = role
    await nextTick()

    const navigation = wrapper.findComponent({ name: 'NavigationMenu' })
    const items = navigation.props('items') as Array<{ label: string, children?: Array<{ label: string }> }>
    const group = items.find(item => item.label === '玩家与世界')
    expect(group?.children?.map(item => item.label)).toEqual(['玩家', '游戏资源'])
    const search = wrapper.findComponent({ name: 'DashboardSearch' })
    const groups = search.props('groups') as Array<{ items: Array<{ label: string }> }>
    expect(groups[0]?.items.map(item => item.label)).toEqual(expect.arrayContaining(['玩家', '游戏资源']))
  })

  it('keeps g-p and adds g-r navigation shortcuts', async () => {
    const { router } = await mountAppShell()

    await shortcuts.value['g-r']?.()
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/game-resources')

    await shortcuts.value['g-p']?.()
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/players')
  })
})
