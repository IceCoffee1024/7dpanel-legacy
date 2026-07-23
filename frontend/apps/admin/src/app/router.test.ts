import { flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory } from 'vue-router'

import { useAuthStore } from '../features/auth'
import { createAdminRouter } from './router'

vi.mock('vue-router/auto-routes', () => ({
  routes: [
    { path: '/', component: { template: '<div />' }, meta: { requiresAuth: true } },
    { path: '/login', component: { template: '<div />' }, meta: { public: true } },
    { path: '/players', component: { template: '<div />' }, meta: { requiresAuth: true } },
    { path: '/api-keys', component: { template: '<div />' }, meta: { requiresAuth: true } },
  ],
}))

function createTestRouter() {
  const pinia = createPinia()
  const router = createAdminRouter(pinia, createMemoryHistory())
  return { pinia, router }
}

function authenticate(pinia: ReturnType<typeof createPinia>, expiresAt = Date.now() + 60_000) {
  const auth = useAuthStore(pinia)
  auth.token = '7dp_t_test.secret'
  auth.expiresAt = expiresAt
  auth.username = 'server-owner'
  auth.role = 'Owner'
  return auth
}

describe('createAdminRouter', () => {
  it('redirects an anonymous protected navigation to login with the full target', async () => {
    const { router } = createTestRouter()

    await router.push('/?from=players')

    expect(router.currentRoute.value.fullPath).toBe('/login?redirect=/?from=players')
  })

  it('allows an authenticated navigation to a protected route', async () => {
    const { pinia, router } = createTestRouter()
    authenticate(pinia)

    await router.push('/players')

    expect(router.currentRoute.value.fullPath).toBe('/players')
  })

  it('returns to login when an active protected session is cleared', async () => {
    const { pinia, router } = createTestRouter()
    const auth = authenticate(pinia)
    await router.push('/players?tab=online')

    auth.token = null
    auth.expiresAt = null
    auth.username = null
    auth.role = null
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/login?redirect=/players?tab=online')
  })

  it('redirects an anonymous API Key navigation to login', async () => {
    const { router } = createTestRouter()

    await router.push('/api-keys')

    expect(router.currentRoute.value.fullPath).toBe('/login?redirect=/api-keys')
  })

  it('redirects an authenticated login navigation to players', async () => {
    const { pinia, router } = createTestRouter()
    authenticate(pinia)

    await router.push('/login')

    expect(router.currentRoute.value.fullPath).toBe('/players')
  })

  it('redirects an authenticated login navigation to a safe internal target', async () => {
    const { pinia, router } = createTestRouter()
    authenticate(pinia)

    await router.push({ path: '/login', query: { redirect: '/?from=players' } })

    expect(router.currentRoute.value.fullPath).toBe('/?from=players')
  })

  it('treats an expired token as anonymous and clears it', async () => {
    const { pinia, router } = createTestRouter()
    const auth = authenticate(pinia, Date.now() - 1)

    await router.push('/players')

    expect(router.currentRoute.value.fullPath).toBe('/login?redirect=/players')
    expect(auth.token).toBeNull()
    expect(auth.expiresAt).toBeNull()
  })

  it('preserves a valid protected full path in the login redirect', async () => {
    const { router } = createTestRouter()

    await router.push('/players?tab=online')

    expect(router.currentRoute.value.query.redirect).toBe('/players?tab=online')
  })

  it('ignores a malicious login redirect for an authenticated session', async () => {
    const { pinia, router } = createTestRouter()
    authenticate(pinia)

    await router.push({ path: '/login', query: { redirect: '//evil' } })

    expect(router.currentRoute.value.fullPath).toBe('/players')
  })

  it('falls back to players when an authenticated login redirect targets login', async () => {
    const { pinia, router } = createTestRouter()
    authenticate(pinia)

    await router.push({ path: '/login', query: { redirect: '/login' } })

    expect(router.currentRoute.value.fullPath).toBe('/players')
  })
})
