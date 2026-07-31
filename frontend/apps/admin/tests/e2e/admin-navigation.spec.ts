import { expect, test } from '@playwright/test'

import { setInitialAdminLocale } from './support/adminLocale'

const authSessionStorageKey = '7dpanel.auth.session.v1'

test('owner can navigate across admin pages without Vue render errors', async ({ page }) => {
  await setInitialAdminLocale(page, 'zh-CN')
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

  const navigation = page.getByRole('navigation')

  await navigation.getByRole('link', { name: '玩家档案与证据', exact: true }).click()
  await expect(page).toHaveURL(/\/players\/history$/)
  await expect(page.getByTestId('history-search')).toBeVisible()
  await navigation.getByRole('link', { name: 'API Keys', exact: true }).click()
  await expect(page).toHaveURL(/\/api-keys$/)
  await navigation.getByRole('link', { name: '审计与事件', exact: true }).click()
  await expect(page).toHaveURL(/\/audit$/)
  await navigation.getByRole('link', { name: '网页控制台', exact: true }).click()
  await expect(page).toHaveURL(/\/console-logs$/)

  expect(pageErrors).toEqual([])
})
