import type { RouteRecordRaw } from 'vue-router'
import { describe, expect, it } from 'vitest'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import { navigationRedirects } from './navigationRedirects'

const redirectMatrix = [
  ['/operations', '/operations/server'],
  ['/community', '/community/chat/live'],
  ['/economy', '/economy/accounts'],
  ['/system', '/system/api-keys'],
  ['/backups', '/operations/backups'],
  ['/schedules', '/operations/automation/schedules'],
  ['/automation', '/operations/automation/rules'],
  ['/server-configuration', '/operations/configuration'],
  ['/mods', '/operations/extensions/mods'],
  ['/modules', '/operations/extensions/modules'],
  ['/world-tools', '/operations/world'],
  ['/console-logs', '/operations/console'],
  ['/game-resources', '/players/resources'],
  ['/access-lists', '/players/access-lists'],
  ['/game-chat/live', '/community/chat/live'],
  ['/game-chat/history', '/community/chat/history'],
  ['/game-chat/mutes', '/community/chat/mutes'],
  ['/game-chat/settings', '/community/chat/settings'],
  ['/game-chat/colored', '/community/chat/appearance'],
  ['/economy/reward-packages', '/economy/rewards/packages'],
  ['/economy/daily-reward', '/economy/rewards/daily'],
  ['/economy/reward-operations', '/economy/rewards/operations'],
  ['/economy/achievement-online-rewards', '/economy/rewards/achievements'],
  ['/economy/shop', '/economy/commerce/shop'],
  ['/economy/redeem-codes', '/economy/commerce/redeem-codes'],
  ['/permissions', '/system/access'],
  ['/api-keys', '/system/api-keys'],
  ['/integrations/discord', '/system/integrations/discord'],
  ['/integrations/geoip', '/system/integrations/geoip'],
  ['/audit', '/system/audit'],
] as const

const Page = defineComponent({ render: () => null })

function createRedirectRouter(extraRoutes: readonly RouteRecordRaw[] = []) {
  const canonicalRoutes = [...new Set(redirectMatrix.map(([, destination]) => destination))]
    .map(path => ({ path, component: Page }))

  return createRouter({
    history: createMemoryHistory(),
    routes: [...navigationRedirects, ...canonicalRoutes, ...extraRoutes],
  })
}

describe('navigationRedirects', () => {
  it.each(redirectMatrix)('redirects %s directly to %s', async (legacyPath, canonicalPath) => {
    const router = createRedirectRouter()

    await router.push(legacyPath)

    expect(router.currentRoute.value.path).toBe(canonicalPath)
    expect(router.currentRoute.value.matched).toHaveLength(1)
  })

  it('preserves query and hash values on a compatibility redirect', async () => {
    const router = createRedirectRouter()

    await router.push({
      path: '/backups',
      query: { page: '2', search: 'steel', tab: 'policies', operationId: 'op-42' },
      hash: '#restore-result',
    })

    expect(router.currentRoute.value).toMatchObject({
      path: '/operations/backups',
      query: { page: '2', search: 'steel', tab: 'policies', operationId: 'op-42' },
      hash: '#restore-result',
    })
  })

  it('does not shadow canonical dynamic player detail routes', async () => {
    const router = createRedirectRouter([
      { path: '/players/history/:crossplatformId', component: Page },
      { path: '/players/profile/:crossplatformId', component: Page },
    ])

    await router.push('/players/history/EOS_ada?tab=activity#events')
    expect(router.currentRoute.value).toMatchObject({
      path: '/players/history/EOS_ada',
      params: { crossplatformId: 'EOS_ada' },
      query: { tab: 'activity' },
      hash: '#events',
    })

    await router.push('/players/profile/EOS_ada?section=inventory#items')
    expect(router.currentRoute.value).toMatchObject({
      path: '/players/profile/EOS_ada',
      params: { crossplatformId: 'EOS_ada' },
      query: { section: 'inventory' },
      hash: '#items',
    })
  })

  it('has no redirect targets that are themselves redirect sources', () => {
    const redirectPaths = new Set(navigationRedirects.map(record => record.path))

    for (const [, canonicalPath] of redirectMatrix)
      expect(redirectPaths.has(canonicalPath)).toBe(false)
  })
})
