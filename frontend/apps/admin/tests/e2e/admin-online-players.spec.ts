import { expect, test } from '@playwright/test'

const adminUrl = process.env.SEVENDPANEL_ADMIN_URL
const username = process.env.PANEL_USERNAME
const password = process.env.PANEL_PASSWORD
const hasOwnerSmokeEnvironment = [adminUrl, username, password]
  .every(value => value !== undefined && value.trim() !== '')
const missingOwnerSmokeReason = 'Requires SEVENDPANEL_ADMIN_URL, PANEL_USERNAME, and PANEL_PASSWORD for a running real OWIN Owner environment.'

test.skip(!hasOwnerSmokeEnvironment, missingOwnerSmokeReason)

async function loginOwner(page: import('@playwright/test').Page) {
  await page.getByLabel('用户名').fill(username!)
  await page.getByLabel('密码').fill(password!)
  await page.getByRole('button', { name: '登录' }).click()
  await expect(page).toHaveURL(/\/players$/)
}

test('redirects an anonymous players deep link to login', async ({ page }) => {
  await page.goto('/players')

  await expect(page).toHaveURL(url => (
    url.pathname === '/login' && url.searchParams.get('redirect') === '/players'
  ))
  await expect(page.getByRole('heading', { name: '登录管理面板' })).toBeVisible()
})

test('owner login shows players without leaking authentication into browser persistence', async ({ page }) => {
  let consoleContainsSensitiveMaterial = false
  page.on('console', (message) => {
    const text = message.text()
    consoleContainsSensitiveMaterial ||= /bearer\s+\S+/i.test(text)
      || text.includes(username!)
      || text.includes(password!)
  })
  const playersRequestPromise = page.waitForRequest(request => (
    request.method() === 'GET' && request.url().includes('/api/v1/players/online')
  ))

  await page.goto('/players')
  await loginOwner(page)

  await expect(page.getByRole('heading', { name: '在线玩家', exact: true })).toBeVisible()
  await expect(page.getByText(/在线 \d+ 人/)).toBeVisible()
  const playersRequest = await playersRequestPromise
  const authorizationHeaderIsBearer = playersRequest.headers().authorization?.startsWith('Bearer ') === true
  expect(authorizationHeaderIsBearer).toBe(true)
  expect(playersRequest.url().toLowerCase()).not.toContain('access_token')

  const browserPersistenceContainsSensitiveMaterial = await page.evaluate(
    ({ expectedUsername, expectedPassword }) => {
      const persistedText = [
        document.cookie,
        ...Object.entries(localStorage).flat(),
        ...Object.entries(sessionStorage).flat(),
      ].join('\n')

      return persistedText.includes(expectedUsername)
        || persistedText.includes(expectedPassword)
        || /7dp_[tk]_[\w.-]+/.test(persistedText)
        || /bearer\s+\S+/i.test(persistedText)
    },
    { expectedUsername: username!, expectedPassword: password! },
  )
  expect(browserPersistenceContainsSensitiveMaterial).toBe(false)
  expect(consoleContainsSensitiveMaterial).toBe(false)
})

test('login and players deep links survive refresh with the expected memory-session boundary', async ({ page }) => {
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: '登录管理面板' })).toBeVisible()
  await page.reload()
  await expect(page).toHaveURL(/\/login$/)

  await page.goto('/players')
  await loginOwner(page)
  await page.reload()

  await expect(page).toHaveURL(url => (
    url.pathname === '/login' && url.searchParams.get('redirect') === '/players'
  ))
})

test('players layout has no horizontal overflow at 390 by 844', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/players')
  await loginOwner(page)

  const hasHorizontalOverflow = await page.evaluate(() => (
    document.documentElement.scrollWidth > document.documentElement.clientWidth
  ))
  expect(hasHorizontalOverflow).toBe(false)
})
