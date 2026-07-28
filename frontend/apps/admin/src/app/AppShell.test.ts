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

async function mountAppShell(initialPath = '/players') {
  localStorage.clear()
  sessionStorage.clear()
  const pinia = createPinia()
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/players', component: { template: '<div />' } },
      { path: '/players/history', component: { template: '<div />' } },
      { path: '/game-resources', component: { template: '<div />' } },
      { path: '/api-keys', component: { template: '<div />' } },
      { path: '/console-logs', component: { template: '<div />' } },
      { path: '/game-chat/live', component: { template: '<div />' } },
      { path: '/game-chat/history', component: { template: '<div />' } },
      { path: '/game-chat/settings', component: { template: '<div />' } },
      { path: '/game-chat/colored', component: { template: '<div />' } },
      { path: '/game-chat/mutes', component: { template: '<div />' } },
      { path: '/audit', component: { template: '<div />' } },
      { path: '/world-tools', component: { template: '<div />' } },
      { path: '/modules', component: { template: '<div />' } },
      { path: '/backups', component: { template: '<div />' } },
      { path: '/schedules', component: { template: '<div />' } },
      { path: '/automation', component: { template: '<div />' } },
      { path: '/economy/accounts', component: { template: '<div />' } },
      { path: '/economy/transactions', component: { template: '<div />' } },
      { path: '/economy/reward-packages', component: { template: '<div />' } },
      { path: '/economy/reward-operations', component: { template: '<div />' } },
      { path: '/economy/shop', component: { template: '<div />' } },
      { path: '/economy/redeem-codes', component: { template: '<div />' } },
      { path: '/economy/achievement-online-rewards', component: { template: '<div />' } },
      { path: '/community/teleport', component: { template: '<div />' } },
      { path: '/community/cities', component: { template: '<div />' } },
      { path: '/community/votes', component: { template: '<div />' } },
      { path: '/integrations/discord', component: { template: '<div />' } },
      { path: '/integrations/geoip', component: { template: '<div />' } },
      { path: '/server-configuration', component: { template: '<div />' } },
      { path: '/access-lists', component: { template: '<div />' } },
      { path: '/permissions', component: { template: '<div />' } },
      { path: '/mods', component: { template: '<div />' } },
      { path: '/login', component: { template: '<div />' } },
    ],
  })
  await router.push(initialPath)
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

  it('shows audit, game events, and mute management only to Owner', async () => {
    const { auth, wrapper } = await mountAppShell()

    expect(wrapper.text()).toContain('审计与事件')
    let navigation = wrapper.findComponent({ name: 'NavigationMenu' })
    let items = navigation.props('items') as Array<{ label: string, children?: Array<{ label: string }> }>
    expect(items.find(item => item.label === '游戏聊天')?.children?.map(item => item.label)).toContain('禁言管理')

    auth.role = 'Admin'
    await nextTick()
    expect(wrapper.text()).not.toContain('审计与事件')
    navigation = wrapper.findComponent({ name: 'NavigationMenu' })
    items = navigation.props('items') as Array<{ label: string, children?: Array<{ label: string }> }>
    expect(items.some(item => item.label === '游戏聊天')).toBe(false)

    auth.role = 'Viewer'
    await nextTick()
    expect(wrapper.text()).not.toContain('审计与事件')
    navigation = wrapper.findComponent({ name: 'NavigationMenu' })
    items = navigation.props('items') as Array<{ label: string, children?: Array<{ label: string }> }>
    expect(items.some(item => item.label === '游戏聊天')).toBe(false)
  })

  it('shows parity operation groups only to Owner and exposes their destinations to search', async () => {
    const { auth, wrapper } = await mountAppShell()
    const navigation = wrapper.findComponent({ name: 'NavigationMenu' })
    const ownerItems = navigation.props('items') as Array<{ label: string, children?: Array<{ label: string }> }>

    expect(ownerItems.find(item => item.label === '服务器运维')?.children?.map(item => item.label))
      .toEqual(expect.arrayContaining(['世界工具', '功能模块', '事件自动化']))
    expect(ownerItems.find(item => item.label === '经济与奖励')?.children).toHaveLength(8)
    expect(ownerItems.find(item => item.label === '传送与投票')?.children).toHaveLength(3)
    expect(ownerItems.find(item => item.label === '集成与访问策略')?.children).toHaveLength(2)

    const search = wrapper.findComponent({ name: 'DashboardSearch' })
    const groups = search.props('groups') as Array<{ items: Array<{ label: string }> }>
    expect(groups[0]?.items.map(item => item.label))
      .toEqual(expect.arrayContaining(['经济账户', '传送设置', 'Discord 集成', 'GeoIP 访问策略']))

    auth.role = 'Admin'
    await nextTick()
    expect(wrapper.text()).not.toContain('服务器运维')
    expect(wrapper.text()).not.toContain('经济与奖励')
  })

  it.each(['Owner', 'Admin', 'Viewer'] as const)('groups player destinations for %s and flattens accessible entries into search', async (role) => {
    const { auth, wrapper } = await mountAppShell()
    auth.role = role
    await nextTick()

    const navigation = wrapper.findComponent({ name: 'NavigationMenu' })
    const items = navigation.props('items') as Array<{ label: string, children?: Array<{ label: string }> }>
    const group = items.find(item => item.label === '玩家与世界')
    const expected = role === 'Owner' ? ['玩家', '玩家档案与证据', '游戏资源'] : ['玩家', '游戏资源']
    expect(group?.children?.map(item => item.label)).toEqual(expected)
    const search = wrapper.findComponent({ name: 'DashboardSearch' })
    const groups = search.props('groups') as Array<{ items: Array<{ label: string }> }>
    expect(groups[0]?.items.map(item => item.label)).toEqual(expect.arrayContaining(expected))
  })

  it('matches the players navigation item exactly so history only highlights its own item', async () => {
    const { wrapper } = await mountAppShell()
    const navigation = wrapper.findComponent({ name: 'NavigationMenu' })
    const items = navigation.props('items') as Array<{
      label: string
      children?: Array<{ label: string, to?: string, exact?: boolean }>
    }>
    const playersItem = items
      .find(item => item.label === '玩家与世界')
      ?.children
      ?.find(item => item.to === '/players')

    expect(playersItem?.exact).toBe(true)
  })

  it('closes the command palette after navigating to the selected page', async () => {
    const { router, wrapper } = await mountAppShell()
    const search = wrapper.findComponent({ name: 'DashboardSearch' })
    const searchButton = wrapper.findAll('button').find(button => button.text().includes('搜索'))
    let finishNavigation: (() => void) | undefined
    router.beforeEach((to) => {
      if (to.path !== '/game-resources')
        return true

      return new Promise<boolean>((resolve) => {
        finishNavigation = () => resolve(true)
      })
    })

    expect(searchButton).toBeDefined()
    await searchButton?.trigger('click')
    await nextTick()
    expect(search.props('open')).toBe(true)

    const destination = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')]
      .find(item => item.textContent?.includes('游戏资源'))
    expect(destination).toBeDefined()
    destination?.click()
    await vi.waitFor(() => expect(finishNavigation).toBeDefined())

    expect(router.currentRoute.value.fullPath).toBe('/players')
    expect(search.props('open')).toBe(true)

    finishNavigation?.()
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/game-resources')
    expect(search.props('open')).toBe(false)
  })

  it.each([
    ['/players/history', '玩家与世界'],
    ['/game-chat/history', '游戏聊天'],
    ['/backups', '服务器运维'],
    ['/economy/accounts', '经济与奖励'],
    ['/community/cities', '传送与投票'],
    ['/integrations/geoip', '集成与访问策略'],
  ])('opens the owning navigation group for a deep link to %s', async (path, groupLabel) => {
    const { wrapper } = await mountAppShell(path)
    const group = wrapper.findAll('button').find(button => button.text().includes(groupLabel))

    expect(group).toBeDefined()
    expect(group?.attributes('aria-expanded')).toBe('true')
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
