import { expect, test } from '@playwright/test'

const authSessionStorageKey = '7dpanel.auth.session.v1'

test('owner can navigate across admin pages without Vue render errors', async ({ page }) => {
  const pageErrors: string[] = []
  page.on('pageerror', error => pageErrors.push(error.message))
  await page.route('**/api/v1/**', async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({ code: 'unavailable' }),
    })
  })

  await page.goto('/login')
  await page.evaluate(({ expiresAt, storageKey }) => {
    sessionStorage.setItem(storageKey, JSON.stringify({
      version: 1,
      token: '7dp_t_navigation-test.secret',
      expiresAt,
      username: 'navigation-owner',
      role: 'Owner',
    }))
  }, {
    expiresAt: Date.now() + 60_000,
    storageKey: authSessionStorageKey,
  })
  await page.goto('/players')

  await page.locator('a[href="/players/history"]').click()
  await expect(page).toHaveURL(/\/players\/history$/)
  await expect(page.getByTestId('history-search')).toBeVisible()
  await page.locator('a[href="/api-keys"]').click()
  await expect(page).toHaveURL(/\/api-keys$/)
  await page.locator('a[href="/audit"]').click()
  await expect(page).toHaveURL(/\/audit$/)
  await page.locator('a[href="/console-logs"]').click()
  await expect(page).toHaveURL(/\/console-logs$/)

  expect(pageErrors).toEqual([])
})
