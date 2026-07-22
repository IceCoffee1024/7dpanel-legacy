import ui from '@nuxt/ui/vue-plugin'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { AuthError, useAuthStore } from '../index'
import LoginForm from './LoginForm.vue'

const loginRequest = vi.hoisted(() => vi.fn())

vi.mock('../api/auth', async importOriginal => ({
  ...await importOriginal<typeof import('../api/auth')>(),
  loginWithPassword: loginRequest,
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

async function mountLoginForm(redirect?: string) {
  const pinia = createPinia()
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/login', component: { template: '<div />' } },
      { path: '/players', component: { template: '<div />' } },
      { path: '/', component: { template: '<div />' } },
    ],
  })
  await router.push({ path: '/login', query: redirect === undefined ? {} : { redirect } })
  await router.isReady()

  const wrapper = mount(LoginForm, {
    global: {
      plugins: [pinia, router, ui],
    },
  })

  return { wrapper, pinia, router }
}

async function fillCredentials(wrapper: Awaited<ReturnType<typeof mountLoginForm>>['wrapper']) {
  await wrapper.get('input[autocomplete="username"]').setValue('Owner')
  await wrapper.get('input[autocomplete="current-password"]').setValue('top-secret-password')
}

describe('loginForm', () => {
  beforeEach(() => {
    loginRequest.mockReset()
  })

  it('labels the username and password fields with correct autocomplete values', async () => {
    const { wrapper } = await mountLoginForm()

    expect(wrapper.get('label[for="username"]').text()).toBe('用户名')
    expect(wrapper.get('#username').attributes('autocomplete')).toBe('username')
    expect(wrapper.get('label[for="password"]').text()).toBe('密码')
    expect(wrapper.get('#password').attributes('autocomplete')).toBe('current-password')
  })

  it('submits once and disables repeated submission while pending', async () => {
    const pending = deferred<{ token: string, expiresAt: number }>()
    loginRequest.mockReturnValue(pending.promise)
    const { wrapper } = await mountLoginForm('/players')
    await fillCredentials(wrapper)

    await wrapper.get('form').trigger('submit')
    await wrapper.get('form').trigger('submit')

    expect(loginRequest).toHaveBeenCalledOnce()
    expect(wrapper.get('button[type="submit"]').attributes()).toHaveProperty('disabled')
    pending.resolve({ token: 'token', expiresAt: Date.now() + 60_000 })
    await flushPromises()
  })

  it('replaces the route with a safe target after login', async () => {
    loginRequest.mockResolvedValue({ token: 'token', expiresAt: Date.now() + 60_000 })
    const { wrapper, router } = await mountLoginForm('/?from=players')
    await fillCredentials(wrapper)

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/?from=players')
    expect(wrapper.get('input[type="password"]').element).toHaveProperty('value', '')
  })

  it.each(['//evil', '/missing'])('falls back from unsafe target %s after login', async (redirect) => {
    loginRequest.mockResolvedValue({ token: 'token', expiresAt: Date.now() + 60_000 })
    const { wrapper, router } = await mountLoginForm(redirect)
    await fillCredentials(wrapper)

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/players')
  })

  it('keeps the username, clears the password, and shows one message on failure', async () => {
    loginRequest.mockRejectedValue(new AuthError('invalid-credentials'))
    const { wrapper, pinia, router } = await mountLoginForm('/players')
    await fillCredentials(wrapper)

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('input[autocomplete="username"]').element).toHaveProperty('value', 'Owner')
    expect(wrapper.get('input[type="password"]').element).toHaveProperty('value', '')
    expect(wrapper.text()).toContain('用户名或密码错误')
    expect(wrapper.html()).not.toContain('top-secret-password')
    expect(JSON.stringify(useAuthStore(pinia).$state)).not.toContain('top-secret-password')
    expect(router.currentRoute.value.fullPath).not.toContain('top-secret-password')
  })

  it('shows the exact rate limit message', async () => {
    loginRequest.mockRejectedValue(new AuthError('rate-limited'))
    const { wrapper } = await mountLoginForm()
    await fillCredentials(wrapper)

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请求过于频繁，请稍后重试')
  })
})
