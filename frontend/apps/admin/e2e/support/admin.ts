import type { ConsoleMessage, Page } from '@playwright/test'

export type AdminRole = 'Owner' | 'Admin' | 'Viewer'

export interface OwnerWave {
  readonly wave: 1 | 2 | 3 | 4 | 5 | 6
  readonly routes: readonly string[]
}

const authSessionStorageKey = '7dpanel.auth.session.v1'

export const ownerNavigationRoutes = [
  '/',
  '/players',
  '/players/history',
  '/players/resources',
  '/system/api-keys',
  '/community/chat/live',
  '/community/chat/history',
  '/community/chat/mutes',
  '/community/chat/settings',
  '/community/chat/appearance',
  '/system/audit',
  '/operations/server',
  '/operations/extensions/modules',
  '/operations/backups',
  '/operations/automation/schedules',
  '/operations/automation/rules',
  '/economy/accounts',
  '/economy/transactions',
  '/economy/rewards/packages',
  '/economy/rewards/daily',
  '/economy/rewards/operations',
  '/economy/commerce/shop',
  '/economy/commerce/redeem-codes',
  '/economy/rewards/achievements',
  '/community/teleport',
  '/community/cities',
  '/community/votes',
  '/system/integrations/discord',
  '/system/integrations/geoip',
  '/operations/configuration',
  '/players/access-lists',
  '/system/access',
  '/operations/extensions/mods',
  '/operations/console',
] as const

export const majorAdminRoutes = [
  ...ownerNavigationRoutes,
  '/players/map',
  '/players/history/EOS_browser_smoke',
  '/players/profile/EOS_browser_smoke',
] as const

export const ownerWaves: readonly OwnerWave[] = [
  {
    wave: 1,
    routes: [
      '/system/audit',
      '/community/chat/live',
      '/community/chat/history',
      '/community/chat/mutes',
      '/community/chat/settings',
      '/community/chat/appearance',
    ],
  },
  { wave: 2, routes: ['/operations/backups', '/operations/automation/schedules'] },
  { wave: 3, routes: ['/players/profile/EOS_browser_smoke'] },
  {
    wave: 4,
    routes: [
      '/economy/accounts',
      '/economy/transactions',
      '/economy/rewards/packages',
      '/economy/rewards/daily',
      '/economy/rewards/operations',
      '/economy/commerce/shop',
      '/economy/commerce/redeem-codes',
      '/economy/rewards/achievements',
      '/community/teleport',
      '/community/cities',
      '/community/votes',
    ],
  },
  {
    wave: 5,
    routes: ['/operations/automation/rules', '/system/integrations/discord', '/system/integrations/geoip'],
  },
  { wave: 6, routes: ['/operations/world', '/operations/extensions/modules'] },
] as const

export const ownerOnlyRoutes = [
  '/players/map',
  '/players/history',
  '/players/history/EOS_browser_smoke',
  '/players/profile/EOS_browser_smoke',
  '/community/chat/live',
  '/community/chat/history',
  '/community/chat/mutes',
  '/community/chat/settings',
  '/community/chat/appearance',
  '/system/audit',
  '/operations/world',
  '/operations/extensions/modules',
  '/operations/backups',
  '/operations/automation/schedules',
  '/operations/automation/rules',
  '/economy/accounts',
  '/economy/transactions',
  '/economy/rewards/packages',
  '/economy/rewards/daily',
  '/economy/rewards/operations',
  '/economy/commerce/shop',
  '/economy/commerce/redeem-codes',
  '/economy/rewards/achievements',
  '/community/teleport',
  '/community/cities',
  '/community/votes',
  '/system/integrations/discord',
  '/system/integrations/geoip',
  '/operations/configuration',
  '/system/access',
] as const

export const sharedAuthenticatedRoutes = [
  '/',
  '/players',
  '/players/resources',
  '/system/api-keys',
  '/players/access-lists',
  '/operations/extensions/mods',
] as const

export function useStoredSession(page: Page, role: AdminRole) {
  return page.addInitScript(({ expiresAt, role, storageKey }) => {
    sessionStorage.setItem(storageKey, JSON.stringify({
      version: 1,
      token: '7dp_t_browser-smoke.secret',
      expiresAt,
      username: `browser-${role.toLowerCase()}`,
      role,
    }))
  }, {
    expiresAt: Date.now() + 10 * 60_000,
    role,
    storageKey: authSessionStorageKey,
  })
}

export function gotoAdmin(page: Page, path: string) {
  return page.goto(path)
}

export async function mockAdminApi(page: Page) {
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request()
    if (request.method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: 'null',
      })
      return
    }

    await route.fulfill({ status: 204 })
  })
}

export interface BrowserErrorMonitor {
  readonly errors: string[]
  reset: () => void
  dispose: () => void
}

export function monitorBrowserErrors(page: Page): BrowserErrorMonitor {
  const errors: string[] = []
  const onConsole = (message: ConsoleMessage) => {
    if (message.type() === 'error')
      errors.push(`console.error: ${message.text()}`)
  }
  const onPageError = (error: Error) => errors.push(`pageerror: ${error.message}`)

  page.on('console', onConsole)
  page.on('pageerror', onPageError)

  return {
    errors,
    reset: () => errors.splice(0),
    dispose: () => {
      page.off('console', onConsole)
      page.off('pageerror', onPageError)
    },
  }
}
