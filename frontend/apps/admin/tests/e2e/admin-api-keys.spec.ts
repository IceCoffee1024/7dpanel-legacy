import { expect, test } from '@playwright/test'

import { setInitialAdminLocale } from './support/adminLocale'

const authSessionStorageKey = '7dpanel.auth.session.v1'
const adminUrl = process.env.SEVENDPANEL_ADMIN_URL
const username = process.env.PANEL_USERNAME
const password = process.env.PANEL_PASSWORD
const hasOwnerSmokeEnvironment = [adminUrl, username, password]
  .every(value => value !== undefined && value.trim() !== '')
const missingOwnerSmokeReason = 'Requires SEVENDPANEL_ADMIN_URL, PANEL_USERNAME, and PANEL_PASSWORD for a running real OWIN Owner environment.'

test.skip(!hasOwnerSmokeEnvironment, missingOwnerSmokeReason)

test.beforeEach(async ({ page }) => {
  await setInitialAdminLocale(page, 'zh-CN')
})

async function loginOwner(page: import('@playwright/test').Page, destination: '/system/api-keys' | '/players') {
  const tokenResponsePromise = page.waitForResponse(response => (
    response.request().method() === 'POST' && response.url().includes('/api/v1/auth/token')
  ))

  await page.getByLabel('用户名').fill(username!)
  await page.getByLabel('密码').fill(password!)
  await page.getByRole('button', { name: '登录' }).click()

  const tokenResponse = await tokenResponsePromise
  const tokenPayload = await tokenResponse.json() as { expires_in?: unknown }
  expect(tokenPayload.expires_in).toEqual(expect.any(Number))
  expect(tokenPayload.expires_in).toBeGreaterThanOrEqual(28_799)
  expect(tokenPayload.expires_in).toBeLessThanOrEqual(28_800)
  await expect(page).toHaveURL(new RegExp(`${destination}$`))
}

test('owner creates, uses, and revokes an API Key without recovering its one-time value', async ({ page }) => {
  const apiKeyName = `Playwright API Key ${Date.now()}`
  let createdApiKey: string | null = null
  let consoleContainsApiKey = false

  page.on('console', (message) => {
    consoleContainsApiKey ||= createdApiKey !== null && message.text().includes(createdApiKey)
  })

  await page.goto('/system/api-keys')
  await loginOwner(page, '/system/api-keys')
  await expect(page.getByRole('heading', { name: 'API Keys' })).toBeVisible()

  await page.getByTestId('create-api-key').click()
  const createDialog = page.getByRole('dialog', { name: '创建 API Key' })
  await createDialog.getByLabel('名称').fill(apiKeyName)
  await createDialog.getByTestId('create-api-key-submit').click()

  const createdDialog = page.getByRole('dialog', { name: 'API Key 已创建' })
  const createdApiKeyLocator = createdDialog.getByTestId('one-time-api-key')
  await expect(createdApiKeyLocator).toHaveText(/^7dp_k_[\w-]{22}_[\w-]{43}$/)
  const oneTimeApiKey = await createdApiKeyLocator.textContent()
  expect(oneTimeApiKey).not.toBeNull()
  createdApiKey = oneTimeApiKey

  await createdDialog.getByTestId('copy-api-key').click()
  await expect(createdDialog.getByRole('status')).toHaveText(/^(API Key 已复制|复制失败，请手动保存 API Key)$/)
  await createdDialog.getByTestId('close-created-api-key').click()
  await expect(page.getByTestId('one-time-api-key')).toHaveCount(0)

  const apiKeyUseStatus = await page.evaluate(async (credential) => {
    const response = await fetch('/api/v1/api-keys', {
      headers: { Authorization: `Bearer ${credential}` },
    })
    return response.status
  }, createdApiKey)
  expect(apiKeyUseStatus).toBe(200)

  const apiKeyRow = page.locator('article').filter({ hasText: apiKeyName })
  await apiKeyRow.getByRole('button', { name: '撤销 API Key' }).click()
  const revokeDialog = page.getByRole('dialog', { name: '撤销 API Key' })
  await expect(revokeDialog).toContainText(apiKeyName)
  await revokeDialog.getByTestId('confirm-revoke-api-key').click()
  await expect(revokeDialog).toHaveCount(0)

  const revokedApiKeyUseStatus = await page.evaluate(async (credential) => {
    const response = await fetch('/api/v1/api-keys', {
      headers: { Authorization: `Bearer ${credential}` },
    })
    return response.status
  }, createdApiKey)
  expect(revokedApiKeyUseStatus).toBe(401)
  expect(consoleContainsApiKey).toBe(false)
})

test('client-side session expiry redirects to login and permits owner relogin', async ({ page }) => {
  await page.clock.install({ time: new Date() })
  await page.goto('/system/api-keys')
  await loginOwner(page, '/system/api-keys')

  await page.clock.fastForward('08:00:01')

  await expect(page).toHaveURL(url => (
    url.pathname === '/login' && url.searchParams.get('redirect') === '/system/api-keys'
  ))
  const authStorageIsAbsent = await page.evaluate(storageKey => (
    localStorage.getItem(storageKey) === null
    && sessionStorage.getItem(storageKey) === null
  ), authSessionStorageKey)
  expect(authStorageIsAbsent).toBe(true)

  await loginOwner(page, '/system/api-keys')
  await expect(page.getByRole('heading', { name: 'API Keys', exact: true })).toBeVisible()
})
