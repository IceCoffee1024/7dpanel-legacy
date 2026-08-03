import { expect, test } from '@playwright/test'

import {
  gotoAdmin,
  mockAdminApi,
  monitorBrowserErrors,
  ownerWaves,
  useStoredSession,
} from './support/admin'

test.beforeEach(async ({ page }) => {
  await useStoredSession(page, 'Owner')
  await mockAdminApi(page)
})

for (const ownerWave of ownerWaves) {
  test(`wave ${ownerWave.wave} Owner pages render across the browser matrix`, async ({ page }) => {
    test.setTimeout(90_000)
    const browserErrors = monitorBrowserErrors(page)

    for (const route of ownerWave.routes) {
      browserErrors.reset()
      await gotoAdmin(page, route)

      await expect(page, `wave ${ownerWave.wave}: ${route}`).toHaveURL(url => url.pathname === route)
      await expect(page.locator('#app').first()).not.toBeEmpty()
      await expect(page.getByTestId('forbidden-page')).toHaveCount(0)
      await expect(page.locator('body')).not.toContainText('404')

      const overflow = await page.evaluate(() => ({
        clientWidth: document.documentElement.clientWidth,
        scrollWidth: document.documentElement.scrollWidth,
      }))
      expect(overflow.scrollWidth, `${route} overflows horizontally at ${overflow.clientWidth}px`)
        .toBeLessThanOrEqual(overflow.clientWidth)
      expect(browserErrors.errors, `${route} emitted browser errors`).toEqual([])
    }

    browserErrors.dispose()
  })
}

test('wave 1 mute loading and empty states remain reachable at 390x844', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'Chromium - mock 390x844')

  let finishLoading: (() => void) | undefined
  await page.route('**/api/v1/chat/mutes?**', async (route) => {
    await new Promise<void>((resolve) => {
      finishLoading = resolve
    })
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        mutes: [],
        nextCursorUpdatedAtUtc: null,
        nextCursorCrossplatformId: null,
      }),
    })
  })

  await gotoAdmin(page, '/community/chat/mutes')
  const loadingState = page.getByRole('status', { name: /Loading active mutes|正在加载禁言列表/u })
  await expect(loadingState).toBeVisible()
  finishLoading?.()
  await expect(loadingState).toHaveCount(0)
  await expect(page.getByText(/No active chat mutes|当前没有生效中的禁言/u)).toBeVisible()
})
