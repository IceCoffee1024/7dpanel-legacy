import { expect, test } from '@playwright/test'

import {
  gotoAdmin,
  majorAdminRoutes,
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
    await expect(page).toHaveURL(url => url.pathname === route)
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

test('server operation mock recovery keeps the persisted ID at 390x844 despite an SSE disconnect', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  let statusReads = 0
  await page.route('**/api/v1/server-operations/operation-browser-1', async (route) => {
    statusReads++
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        operationId: 'operation-browser-1',
        kind: 'restart_script',
        status: statusReads === 1 ? 'running' : 'succeeded',
        requestedAtUtc: '2026-08-03T01:02:03Z',
        startedAtUtc: '2026-08-03T01:02:04Z',
        completedAtUtc: statusReads === 1 ? null : '2026-08-03T01:02:05Z',
        completionDeadlineUtc: '2026-08-03T01:07:04Z',
        failureCode: null,
        auditStatus: 'recorded',
      }),
    })
  })
  await page.route('**/api/v1/events/**', route => route.abort())

  await gotoAdmin(page, '/operations/server?operationId=operation-browser-1&operationKind=restart_script')
  await expect.poll(() => statusReads).toBeGreaterThan(0)
  await page.reload()
  await expect.poll(() => statusReads).toBeGreaterThan(1)

  const hasHorizontalOverflow = await page.evaluate(() => (
    document.documentElement.scrollWidth > document.documentElement.clientWidth
  ))
  expect(hasHorizontalOverflow).toBe(false)
})
