import { expect, test } from '@playwright/test'

import {
  gotoAdmin,
  majorAdminRoutes,
  mockAdminApi,
  ownerOnlyRoutes,
  sharedAuthenticatedRoutes,
  useStoredSession,
} from './support/admin'

test('anonymous users are redirected from every protected route with the full target preserved', async ({ page }) => {
  test.setTimeout(120_000)
  await mockAdminApi(page)

  for (const route of majorAdminRoutes) {
    const target = `${route}?browser=anonymous`
    await gotoAdmin(page, target)

    await expect(page, `anonymous access to ${route}`).toHaveURL(url => (
      url.pathname === '/login' && url.searchParams.get('redirect') === target
    ))
  }
})

for (const role of ['Admin', 'Viewer'] as const) {
  test(`${role} is denied every Owner-only route`, async ({ page }) => {
    test.setTimeout(120_000)
    await useStoredSession(page, role)
    await mockAdminApi(page)

    for (const route of ownerOnlyRoutes) {
      await gotoAdmin(page, route)

      await expect(page, `${role} access to ${route}`).toHaveURL(url => (
        url.pathname === '/forbidden' && url.searchParams.get('from') === route
      ))
      await expect(page.getByTestId('forbidden-page')).toBeVisible()
    }
  })
}

for (const role of ['Owner', 'Admin', 'Viewer'] as const) {
  test(`${role} can open shared authenticated routes`, async ({ page }) => {
    await useStoredSession(page, role)
    await mockAdminApi(page)

    for (const route of sharedAuthenticatedRoutes) {
      await gotoAdmin(page, route)
      await expect(page, `${role} access to ${route}`).toHaveURL(url => url.pathname === route)
    }
  })
}

test('Admin can use the console while Viewer is forbidden', async ({ browser }) => {
  for (const role of ['Admin', 'Viewer'] as const) {
    const context = await browser.newContext()
    const page = await context.newPage()
    await useStoredSession(page, role)
    await mockAdminApi(page)
    await gotoAdmin(page, '/operations/console')

    await expect(page).toHaveURL(url => url.pathname === (role === 'Admin' ? '/operations/console' : '/forbidden'))
    await context.close()
  }
})

test('an authenticated login redirect accepts safe internal targets and rejects external targets', async ({ page }) => {
  await useStoredSession(page, 'Owner')
  await mockAdminApi(page)

  await gotoAdmin(page, '/login?redirect=%2Fcommunity%2Fchat%2Fmutes')
  await expect(page).toHaveURL(url => url.pathname === '/community/chat/mutes')

  await gotoAdmin(page, '/login?redirect=%2F%2Fevil.example')
  await expect(page).toHaveURL(url => url.pathname === '/players')
})
