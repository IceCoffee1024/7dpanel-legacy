import { expect, test } from '@playwright/test'

const authSessionStorageKey = '7dpanel.auth.session.v1'
const adminUrl = process.env.SEVENDPANEL_ADMIN_URL
const username = process.env.PANEL_USERNAME
const password = process.env.PANEL_PASSWORD
const hasOwnerSmokeEnvironment = [adminUrl, username, password]
  .every(value => value !== undefined && value.trim() !== '')
const missingOwnerSmokeReason = 'Requires SEVENDPANEL_ADMIN_URL, PANEL_USERNAME, and PANEL_PASSWORD for a running real OWIN Owner environment.'

test.skip(!hasOwnerSmokeEnvironment, missingOwnerSmokeReason)

async function loginOwner(
  page: import('@playwright/test').Page,
  rememberLogin = false,
) {
  await page.getByLabel('用户名').fill(username!)
  await page.getByLabel('密码').fill(password!)
  if (rememberLogin) {
    const checkbox = page.getByRole('checkbox', { name: '保持登录' })
    await expect(checkbox).toHaveAttribute('aria-checked', 'false')
    await checkbox.click()
    await expect(checkbox).toHaveAttribute('aria-checked', 'true')
  }
  await page.getByRole('button', { name: '登录' }).click()
  await expect(page).toHaveURL(/\/players$/)
}

async function expectSessionStorage(
  page: import('@playwright/test').Page,
  persistence: 'tab' | 'browser',
) {
  const contract = await page.evaluate(({ expectedUsername, persistence, storageKey }) => {
    const localValue = localStorage.getItem(storageKey)
    const sessionValue = sessionStorage.getItem(storageKey)
    const expectedValue = persistence === 'tab' ? sessionValue : localValue
    const unexpectedValue = persistence === 'tab' ? localValue : sessionValue
    let record: unknown = null

    try {
      record = expectedValue === null ? null : JSON.parse(expectedValue)
    }
    catch {
      record = null
    }

    const recordIsValid = typeof record === 'object'
      && record !== null
      && Object.keys(record).sort().join(',') === 'expiresAt,role,token,username,version'
      && record.version === 1
      && typeof record.token === 'string'
      && record.token.startsWith('7dp_t_')
      && typeof record.expiresAt === 'number'
      && Number.isSafeInteger(record.expiresAt)
      && record.expiresAt > Date.now()
      && record.username === expectedUsername
      && record.role === 'Owner'

    return {
      recordIsValid,
      unexpectedValueIsAbsent: unexpectedValue === null,
    }
  }, {
    expectedUsername: username!,
    persistence,
    storageKey: authSessionStorageKey,
  })

  expect(contract.recordIsValid).toBe(true)
  expect(contract.unexpectedValueIsAbsent).toBe(true)
}

async function expectAuthStorageAbsent(page: import('@playwright/test').Page) {
  const authStorageIsAbsent = await page.evaluate(storageKey => (
    localStorage.getItem(storageKey) === null
    && sessionStorage.getItem(storageKey) === null
  ), authSessionStorageKey)

  expect(authStorageIsAbsent).toBe(true)
}

test('redirects an anonymous players deep link to login', async ({ page }) => {
  await page.goto('/players')

  await expect(page).toHaveURL(url => (
    url.pathname === '/login' && url.searchParams.get('redirect') === '/players'
  ))
  await expect(page.getByRole('heading', { name: '登录管理面板' })).toBeVisible()
})

test('invalid browser session records return to login without sending an authorization header', async ({ page }) => {
  const authorizedProtectedRequests: string[] = []
  page.on('request', (request) => {
    if (
      request.url().includes('/api/v1/')
      && request.headers().authorization !== undefined
    ) {
      authorizedProtectedRequests.push(request.url())
    }
  })

  await page.goto('/login')
  await page.evaluate((storageKey) => {
    localStorage.setItem(storageKey, '{invalid-json')
    sessionStorage.setItem(storageKey, JSON.stringify({
      version: 1,
      token: '7dp_t_expired.secret',
      expiresAt: 1,
      username: 'expired-owner',
      role: 'Owner',
    }))
  }, authSessionStorageKey)

  await page.goto('/players')

  await expect(page).toHaveURL(url => (
    url.pathname === '/login' && url.searchParams.get('redirect') === '/players'
  ))
  await expectAuthStorageAbsent(page)
  expect(authorizedProtectedRequests).toEqual([])
})

test('owner login uses a Bearer header and confines authentication to the approved tab session record', async ({ page }) => {
  let consoleContainsSensitiveMaterial = false
  let consoleContainsCspViolation = false
  page.on('console', (message) => {
    const text = message.text()
    consoleContainsSensitiveMaterial ||= /bearer\s+\S+/i.test(text)
      || text.includes(username!)
      || text.includes(password!)
    consoleContainsCspViolation ||= /content security policy|refused to load/i.test(text)
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

  await expectSessionStorage(page, 'tab')
  await page.getByTestId('account-menu-trigger').click()
  await expect(page.getByText(username!, { exact: true })).toBeVisible()
  await expect(page.getByText('Owner', { exact: true })).toBeVisible()

  const browserPersistenceContainsSensitiveMaterial = await page.evaluate(
    ({ expectedPassword, storageKey }) => {
      const persistedText = [
        document.cookie,
        ...Object.entries(localStorage)
          .filter(([key]) => key !== storageKey)
          .flat(),
        ...Object.entries(sessionStorage)
          .filter(([key]) => key !== storageKey)
          .flat(),
      ].join('\n')

      const sessionRecord = sessionStorage.getItem(storageKey)
      return persistedText.includes(expectedPassword)
        || /7dp_[tk]_[\w.-]+/.test(persistedText)
        || /bearer\s+\S+/i.test(persistedText)
        || sessionRecord?.includes(expectedPassword) === true
    },
    { expectedPassword: password!, storageKey: authSessionStorageKey },
  )
  expect(browserPersistenceContainsSensitiveMaterial).toBe(false)
  expect(consoleContainsSensitiveMaterial).toBe(false)
  expect(consoleContainsCspViolation).toBe(false)
})

test('a tab-scoped session survives refresh but not replacement-tab navigation', async ({ context, page }) => {
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: '登录管理面板' })).toBeVisible()
  await page.reload()
  await expect(page).toHaveURL(/\/login$/)

  await page.goto('/players')
  await loginOwner(page)
  await page.reload()
  await expect(page).toHaveURL(/\/players$/)
  await expect(page.getByRole('heading', { name: '在线玩家', exact: true })).toBeVisible()
  await expectSessionStorage(page, 'tab')

  await page.close()
  const replacementPage = await context.newPage()
  await replacementPage.goto('/players')

  await expect(replacementPage).toHaveURL(url => (
    url.pathname === '/login' && url.searchParams.get('redirect') === '/players'
  ))
  await expectAuthStorageAbsent(replacementPage)
})

test('a remembered session restores after browser restart and logout clears authentication in every tab', async ({ browser, context, page }) => {
  await page.goto('/players')
  await loginOwner(page, true)
  await expectSessionStorage(page, 'browser')

  const adminOrigin = new URL(page.url()).origin
  const storageState = await context.storageState()
  const restartedContext = await browser.newContext({ storageState })
  try {
    const restartedPage = await restartedContext.newPage()
    await restartedPage.goto(`${adminOrigin}/players`)
    await expect(restartedPage).toHaveURL(/\/players$/)
    await expect(restartedPage.getByRole('heading', { name: '在线玩家', exact: true })).toBeVisible()
    await expectSessionStorage(restartedPage, 'browser')
  }
  finally {
    await restartedContext.close()
  }

  const secondPage = await context.newPage()
  await secondPage.goto('/players')
  await expect(secondPage).toHaveURL(/\/players$/)

  await page.getByTestId('account-menu-trigger').click()
  await page.getByRole('menuitem', { name: '退出登录' }).click()
  await expect(page).toHaveURL(/\/login$/)
  await expect(secondPage).toHaveURL(url => (
    url.pathname === '/login' && url.searchParams.get('redirect') === '/players'
  ))
  await expectAuthStorageAbsent(page)
  await expectAuthStorageAbsent(secondPage)
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
