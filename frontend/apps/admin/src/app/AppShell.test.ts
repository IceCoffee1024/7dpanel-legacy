import ui from '@nuxt/ui/vue-plugin'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { useAuthStore } from '../features/auth'
import AppShell from './AppShell.vue'

async function mountAppShell() {
  localStorage.clear()
  sessionStorage.clear()
  const pinia = createPinia()
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/players', component: { template: '<div />' } },
      { path: '/api-keys', component: { template: '<div />' } },
      { path: '/console-logs', component: { template: '<div />' } },
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
        UDashboardSearch: true,
        UDashboardSearchButton: true,
        UDropdownMenu: {
          props: ['items'],
          template: '<div><slot /><span v-for="group in items" :key="group[0]?.label"><span v-for="item in group" :key="item.label">{{ item.label }}</span></span></div>',
        },
        UNavigationMenu: {
          props: ['items'],
          template: '<nav><span v-for="item in items" :key="item.label">{{ item.label }}</span></nav>',
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
})
