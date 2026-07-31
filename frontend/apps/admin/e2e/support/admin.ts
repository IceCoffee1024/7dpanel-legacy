import type { ConsoleMessage, Page } from '@playwright/test'

export type AdminRole = 'Owner' | 'Admin' | 'Viewer'

const authSessionStorageKey = '7dpanel.auth.session.v1'

export const ownerNavigationRoutes = [
  '/',
  '/players',
  '/players/history',
  '/game-resources',
  '/api-keys',
  '/game-chat/live',
  '/game-chat/history',
  '/game-chat/mutes',
  '/game-chat/settings',
  '/game-chat/colored',
  '/audit',
  '/world-tools',
  '/modules',
  '/backups',
  '/schedules',
  '/automation',
  '/economy/accounts',
  '/economy/transactions',
  '/economy/reward-packages',
  '/economy/daily-reward',
  '/economy/reward-operations',
  '/economy/shop',
  '/economy/redeem-codes',
  '/economy/achievement-online-rewards',
  '/community/teleport',
  '/community/cities',
  '/community/votes',
  '/integrations/discord',
  '/integrations/geoip',
  '/server-configuration',
  '/access-lists',
  '/permissions',
  '/mods',
  '/console-logs',
] as const

export const majorAdminRoutes = [
  ...ownerNavigationRoutes,
  '/players/map',
  '/players/history/EOS_browser_smoke',
  '/players/profile/EOS_browser_smoke',
] as const

export const ownerOnlyRoutes = [
  '/players/map',
  '/players/profile/EOS_browser_smoke',
  '/game-chat/live',
  '/game-chat/history',
  '/game-chat/mutes',
  '/game-chat/settings',
  '/game-chat/colored',
  '/audit',
  '/world-tools',
  '/modules',
  '/backups',
  '/schedules',
  '/automation',
  '/economy/accounts',
  '/economy/transactions',
  '/economy/reward-packages',
  '/economy/daily-reward',
  '/economy/reward-operations',
  '/economy/shop',
  '/economy/redeem-codes',
  '/economy/achievement-online-rewards',
  '/community/teleport',
  '/community/cities',
  '/community/votes',
  '/integrations/discord',
  '/integrations/geoip',
  '/server-configuration',
  '/permissions',
] as const

export const sharedAuthenticatedRoutes = [
  '/',
  '/players',
  '/players/history',
  '/players/history/EOS_browser_smoke',
  '/game-resources',
  '/api-keys',
  '/access-lists',
  '/mods',
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
