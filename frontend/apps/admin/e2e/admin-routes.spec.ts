import { expect, test } from '@playwright/test'

import {
  majorAdminRoutes,
  gotoAdmin,
  mockAdminApi,
  monitorBrowserErrors,
  ownerNavigationRoutes,
  useStoredSession,
} from './support/admin'

test.beforeEach(async ({ page }) => {
  await useStoredSession(page, 'Owner')
  await mockAdminApi(page)
})

for (const route of majorAdminRoutes) {
  test(`${route} renders without browser errors or horizontal overflow`, async ({ page }) => {
    const browserErrors = monitorBrowserErrors(page)

    await gotoAdmin(page, route)
    await expect(page).toHaveURL((url) => url.pathname === route)
    await expect(page.locator('#app').first()).not.toBeEmpty()
    await expect(page.getByTestId('forbidden-page')).toHaveCount(0)
    await expect(page.locator('body')).not.toContainText('404')

    const hasHorizontalOverflow = await page.evaluate(() => (
      document.documentElement.scrollWidth > document.documentElement.clientWidth
    ))
    expect(hasHorizontalOverflow, `${route} overflows horizontally`).toBe(false)
    expect(browserErrors.errors, `${route} emitted browser errors`).toEqual([])
    browserErrors.dispose()
  })
}

for (const route of ownerNavigationRoutes) {
  test(`${route} has an owner navigation entry`, async ({ page }) => {
    await gotoAdmin(page, route)

    const href = route === '/' ? '/' : route
    await expect(page.locator(`a[href="${href}"]`).first()).toBeAttached()
  })
}

