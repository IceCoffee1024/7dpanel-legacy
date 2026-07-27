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
    { path: '/game-resources', component: { template: '<div />' }, meta: { requiresAuth: true } },
    { path: '/players/map', component: { template: '<div />' }, meta: { requiresAuth: true } },
    { path: '/players/history', component: { template: '<div />' }, meta: { requiresAuth: true } },
    { path: '/players/history/:crossplatformId', component: { template: '<div />' }, meta: { requiresAuth: true } },
    { path: '/players/profile/:crossplatformId', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/api-keys', component: { template: '<div />' }, meta: { requiresAuth: true } },
    { path: '/console-logs', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner', 'Admin'] } },
    { path: '/game-chat/live', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/game-chat/history', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/game-chat/settings', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/game-chat/colored', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/game-chat/mutes', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/audit', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/world-tools', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/modules', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/backups', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/schedules', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/automation', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/economy/accounts', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/economy/transactions', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/economy/reward-packages', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/economy/reward-operations', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/economy/shop', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/economy/redeem-codes', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/economy/achievement-online-rewards', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/community/teleport', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/community/cities', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/community/votes', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/integrations/discord', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
    { path: '/integrations/geoip', component: { template: '<div />' }, meta: { requiresAuth: true, roles: ['Owner'] } },
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

function authenticateAs(pinia: ReturnType<typeof createPinia>, role: 'Owner' | 'Admin' | 'Viewer') {
  const auth = authenticate(pinia)
  auth.role = role
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

  it.each(['Owner', 'Admin', 'Viewer'] as const)('allows %s to open the game resources route', async (role) => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, role)

    await router.push('/game-resources?kind=block&page=2')

    expect(router.currentRoute.value.fullPath).toBe('/game-resources?kind=block&page=2')
  })

  it('preserves an anonymous game resources deep link in the login redirect', async () => {
    const { router } = createTestRouter()

    await router.push('/game-resources?search=steel')

    expect(router.currentRoute.value.query.redirect).toBe('/game-resources?search=steel')
  })

  it.each(['Owner', 'Admin'] as const)('allows %s to open the console deep link', async (role) => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, role)

    await router.push('/console-logs')

    expect(router.currentRoute.value.fullPath).toBe('/console-logs')
  })

  it('sends a Viewer console deep link to Forbidden', async () => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, 'Viewer')

    await router.push('/console-logs')

    expect(router.currentRoute.value.fullPath).toBe('/forbidden?from=/console-logs')
  })

  it.each(['/game-chat/live', '/game-chat/history', '/game-chat/settings', '/game-chat/colored'])('allows Owner to open %s', async (path) => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, 'Owner')

    await router.push(path)

    expect(router.currentRoute.value.fullPath).toBe(path)
  })

  it.each(['Admin', 'Viewer'] as const)('sends %s game chat deep links to Forbidden', async (role) => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, role)

    await router.push('/game-chat/live')

    expect(router.currentRoute.value.fullPath).toBe('/forbidden?from=/game-chat/live')
  })

  it('preserves an anonymous game chat deep link in the login redirect', async () => {
    const { router } = createTestRouter()

    await router.push('/game-chat/colored')

    expect(router.currentRoute.value.fullPath).toBe('/login?redirect=/game-chat/colored')
  })

  it.each(['/audit', '/game-chat/mutes'])('allows Owner to open %s', async (path) => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, 'Owner')

    await router.push(path)

    expect(router.currentRoute.value.fullPath).toBe(path)
  })

  it.each(['Admin', 'Viewer'] as const)('sends %s audit and mute deep links to Forbidden', async (role) => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, role)

    await router.push('/audit?tab=game-events')
    expect(router.currentRoute.value.fullPath).toBe('/forbidden?from=/audit?tab=game-events')

    await router.push('/game-chat/mutes')
    expect(router.currentRoute.value.fullPath).toBe('/forbidden?from=/game-chat/mutes')
  })

  it('preserves an anonymous audit deep link for login instead of treating it as missing', async () => {
    const { router } = createTestRouter()

    await router.push('/audit?tab=game-events')

    expect(router.currentRoute.value.fullPath).toBe('/login?redirect=/audit?tab=game-events')
  })

  it.each([
    '/world-tools',
    '/modules',
    '/automation',
    '/economy/accounts',
    '/economy/achievement-online-rewards',
    '/community/teleport',
    '/community/votes',
    '/integrations/discord',
    '/integrations/geoip',
  ])('keeps parity management route %s Owner-only', async (path) => {
    const owner = createTestRouter()
    authenticateAs(owner.pinia, 'Owner')
    await owner.router.push(path)
    expect(owner.router.currentRoute.value.fullPath).toBe(path)

    const admin = createTestRouter()
    authenticateAs(admin.pinia, 'Admin')
    await admin.router.push(path)
    expect(admin.router.currentRoute.value.fullPath).toBe(`/forbidden?from=${path}`)
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

  it('protects history list and deep links while preserving the full target', async () => {
    const { router } = createTestRouter()
    await router.push('/players/history/EOS_0002d12af0fe4add9c7de0fbc238d431')

    expect(router.currentRoute.value.fullPath)
      .toBe('/login?redirect=/players/history/EOS_0002d12af0fe4add9c7de0fbc238d431')
  })

  it('allows authenticated navigation to the history list', async () => {
    const { pinia, router } = createTestRouter()
    authenticate(pinia)
    await router.push('/players/history')

    expect(router.currentRoute.value.fullPath).toBe('/players/history')
  })

  it('allows Owner to open a player profile deep link', async () => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, 'Owner')

    await router.push('/players/profile/EOS_ada')

    expect(router.currentRoute.value.fullPath).toBe('/players/profile/EOS_ada')
  })

  it.each(['Admin', 'Viewer'] as const)('sends %s player profile deep links to Forbidden', async (role) => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, role)

    await router.push('/players/profile/EOS_ada')

    expect(router.currentRoute.value.fullPath).toBe('/forbidden?from=/players/profile/EOS_ada')
  })

  it('preserves an anonymous player profile deep link for login', async () => {
    const { router } = createTestRouter()

    await router.push('/players/profile/EOS_ada?section=inventory')

    expect(router.currentRoute.value.fullPath)
      .toBe('/login?redirect=/players/profile/EOS_ada?section=inventory')
  })

  it('protects the player map while preserving its restored filters', async () => {
    const { router } = createTestRouter()

    await router.push('/players/map?player=EOS_ada&observation=41')

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query.redirect)
      .toBe('/players/map?player=EOS_ada&observation=41')
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
