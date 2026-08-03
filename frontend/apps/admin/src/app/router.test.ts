import { flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory } from 'vue-router'

import { useAuthStore } from '../features/auth'
import { createAdminRouter } from './router'

const routeMock = vi.hoisted(() => {
  const ownerOnly = new Set([
    '/operations/server',
    '/operations/backups',
    '/operations/automation/schedules',
    '/operations/automation/rules',
    '/operations/configuration',
    '/operations/extensions/mods',
    '/operations/extensions/modules',
    '/operations/world',
    '/players/history',
    '/players/history/:crossplatformId',
    '/players/profile/:crossplatformId',
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
  ])
  const canonicalPaths = [
    '/',
    '/login',
    '/operations/server',
    '/operations/backups',
    '/operations/automation/schedules',
    '/operations/automation/rules',
    '/operations/configuration',
    '/operations/extensions/mods',
    '/operations/extensions/modules',
    '/operations/world',
    '/operations/console',
    '/players',
    '/players/history',
    '/players/history/:crossplatformId',
    '/players/profile/:crossplatformId',
    '/players/map',
    '/players/access-lists',
    '/players/resources',
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
    '/system/api-keys',
    '/system/integrations/discord',
    '/system/integrations/geoip',
    '/system/audit',
  ]
  return {
    routes: canonicalPaths.map(path => ({
      path,
      component: { template: '<div />' },
      meta: path === '/login'
        ? {}
        : path === '/operations/console'
          ? { requiresAuth: true, roles: ['Owner', 'Admin'] }
          : ownerOnly.has(path)
            ? { requiresAuth: true, roles: ['Owner'] }
            : { requiresAuth: true },
    })),
    redirectPaths: [
      '/operations',
      '/community',
      '/economy',
      '/system',
      '/backups',
      '/schedules',
      '/automation',
      '/server-configuration',
      '/mods',
      '/modules',
      '/world-tools',
      '/console-logs',
      '/game-resources',
      '/access-lists',
      '/game-chat/live',
      '/game-chat/history',
      '/game-chat/mutes',
      '/game-chat/settings',
      '/game-chat/colored',
      '/economy/reward-packages',
      '/economy/daily-reward',
      '/economy/reward-operations',
      '/economy/achievement-online-rewards',
      '/economy/shop',
      '/economy/redeem-codes',
      '/permissions',
      '/api-keys',
      '/integrations/discord',
      '/integrations/geoip',
      '/audit',
    ],
  }
})

vi.mock('vue-router/auto-routes', () => ({ routes: routeMock.routes }))

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
  it('redirects anonymous protected navigation to login with the canonical target', async () => {
    const { router } = createTestRouter()

    await router.push('/game-resources?search=steel#results')

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query.redirect).toBe('/players/resources?search=steel#results')
  })

  it('applies the target route guard after a legacy redirect', async () => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, 'Viewer')

    await router.push('/console-logs')

    expect(router.currentRoute.value.fullPath).toBe('/forbidden?from=/operations/console')
  })

  it.each(routeMock.redirectPaths)('keeps the legacy route address out of the final history entry for %s', async (path) => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, 'Owner')

    await router.push(`${path}?tab=overview#top`)

    expect(router.currentRoute.value.path).not.toBe(path)
    expect(router.currentRoute.value.query.tab).toBe('overview')
    expect(router.currentRoute.value.hash).toBe('#top')
  })

  it('preserves query and hash when a legacy route is redirected', async () => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, 'Owner')

    await router.push('/game-chat/live?channel=global#latest')

    expect(router.currentRoute.value.fullPath).toBe('/community/chat/live?channel=global#latest')
  })

  it.each(['Owner', 'Admin', 'Viewer'] as const)('allows %s to open API Keys', async (role) => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, role)

    await router.push('/api-keys')

    expect(router.currentRoute.value.fullPath).toBe('/system/api-keys')
  })

  it('allows an authenticated navigation to a canonical route', async () => {
    const { pinia, router } = createTestRouter()
    authenticate(pinia)

    await router.push('/players/resources?kind=block&page=2')

    expect(router.currentRoute.value.fullPath).toBe('/players/resources?kind=block&page=2')
  })

  it.each(['Admin', 'Viewer'] as const)('sends %s Owner-only deep links to Forbidden', async (role) => {
    const { pinia, router } = createTestRouter()
    authenticateAs(pinia, role)

    await router.push('/players/history/EOS_ada')

    expect(router.currentRoute.value.fullPath).toBe('/forbidden?from=/players/history/EOS_ada')
  })

  it('redirects an authenticated login navigation to a safe internal target', async () => {
    const { pinia, router } = createTestRouter()
    authenticate(pinia)

    await router.push({ path: '/login', query: { redirect: '/players/resources?search=steel' } })

    expect(router.currentRoute.value.fullPath).toBe('/players/resources?search=steel')
  })

  it('falls back to players when an authenticated login redirect is unsafe', async () => {
    const { pinia, router } = createTestRouter()
    authenticate(pinia)

    await router.push({ path: '/login', query: { redirect: '//evil' } })

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

  it('treats an expired token as anonymous and clears it', async () => {
    const { pinia, router } = createTestRouter()
    const auth = authenticate(pinia, Date.now() - 1)

    await router.push('/players')

    expect(router.currentRoute.value.fullPath).toBe('/login?redirect=/players')
    expect(auth.token).toBeNull()
    expect(auth.expiresAt).toBeNull()
  })
})
