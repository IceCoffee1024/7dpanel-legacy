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
  ],
}))

function createTestRouter() {
  const pinia = createPinia()
  const router = createAdminRouter(pinia, createMemoryHistory())
  return { pinia, router }
}

function authenticate(pinia: ReturnType<typeof createPinia>, expiresAt = Date.now() + 60_000) {
  const auth = useAuthStore(pinia)
  auth.token = 'test-token'
  auth.expiresAt = expiresAt
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
