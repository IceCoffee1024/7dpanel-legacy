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

type OnlinePlayerStage = 'initial' | 'updated' | 'missing' | 'reappeared'

function onlinePlayersResponse(stage: OnlinePlayerStage) {
  const primaryName = stage === 'updated'
    ? 'Player Updated'
    : stage === 'reappeared'
      ? 'Player Reappeared'
      : 'Player'
  const primaryPlayer = {
    entityId: 7,
    name: primaryName,
    platformIdentity: {
      combinedId: 'Steam_76561198000000000',
      platform: 'Steam',
    },
    crossplatformIdentity: {
      combinedId: 'EOS_12345678901234567',
      platform: 'EOS',
    },
    deviceType: 'windows',
    ip: '192.0.2.10',
    ping: 42,
    compatibilityVersion: 'V 3.0.1',
    discordUserId: '18446744073709551615',
    permissionLevel: 1000,
    position: { x: 100.5, y: 51, z: 200.25 },
    isDead: false,
    health: 93,
    maxHealth: 100,
    level: 18,
    playGroup: null,
    lastLoginUtc: null,
    gameStage: null,
    expToNextLevel: null,
    skillPoints: null,
    bedroll: null,
    score: stage === 'updated' ? 900 : 827,
    zombieKills: 317,
    playerKills: 2,
    deaths: 4,
    totalTimePlayedMinutes: 4823.5,
    distanceWalkedMeters: 127540.75,
    totalItemsCrafted: 2360,
    longestLifeMinutes: 920.25,
    currentLifeMinutes: 134.5,
    observedAtUtc: stage === 'updated'
      ? '2026-07-23T08:00:10Z'
      : '2026-07-23T07:59:00Z',
  }
  const nullablePlayer = {
    entityId: 42,
    name: 'Nullable Player',
    platformIdentity: {
      combinedId: 'Steam_76561198000000000_with_a_deliberately_long_identity_value',
      platform: 'Steam',
    },
    crossplatformIdentity: null,
    deviceType: 'unknown',
    ip: null,
    ping: 1,
    compatibilityVersion: null,
    discordUserId: null,
    permissionLevel: 1000,
    position: { x: -100.5, y: 51, z: -200.25 },
    isDead: true,
    health: 0,
    maxHealth: 100,
    level: 1,
    playGroup: null,
    lastLoginUtc: null,
    gameStage: null,
    expToNextLevel: null,
    skillPoints: null,
    bedroll: null,
    score: 0,
    zombieKills: 0,
    playerKills: 0,
    deaths: 1,
    totalTimePlayedMinutes: 0,
    distanceWalkedMeters: 9_876_543.21,
    totalItemsCrafted: 0,
    longestLifeMinutes: 0,
    currentLifeMinutes: 0,
    observedAtUtc: '2026-07-23T07:59:00Z',
  }

  return {
    players: stage === 'missing'
      ? [nullablePlayer]
      : [primaryPlayer, nullablePlayer],
  }
}

async function interceptOnlinePlayers(
  page: import('@playwright/test').Page,
  getStage: () => OnlinePlayerStage,
) {
  await page.route('**/api/v1/players/online', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify(onlinePlayersResponse(getStage())),
    })
  })
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
  const accountMenu = page.getByRole('menu')
  await expect(accountMenu.getByText(username!, { exact: true })).toBeVisible()
  await expect(accountMenu.getByText('Owner', { exact: true })).toBeVisible()

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
  await expect(page).toHaveURL(url => (
    url.pathname === '/login' && url.searchParams.get('redirect') === '/players'
  ))
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

test('synthetic complete observations render in the details slideover and lock unavailable targets', async ({ page }) => {
  let stage: OnlinePlayerStage = 'initial'
  await interceptOnlinePlayers(page, () => stage)
  await page.goto('/players')
  await loginOwner(page)

  const primaryDetailsButton = page.getByRole('button', { name: '查看玩家详情：Player' })
  await primaryDetailsButton.click()
  const dialog = page.getByRole('dialog')

  await expect(dialog.getByRole('heading', { name: '身份' })).toBeVisible()
  await expect(dialog.getByRole('heading', { name: '连接' })).toBeVisible()
  await expect(dialog.getByRole('heading', { name: '当前状态' })).toBeVisible()
  await expect(dialog.getByRole('heading', { name: '累计统计' })).toBeVisible()
  await expect(dialog.getByText('101, 51, 200')).toBeVisible()
  await expect(dialog.getByText('127,541')).toBeVisible()
  await expect(dialog.getByText('3 天 8 小时 24 分钟')).toBeVisible()
  await expect(dialog.getByRole('button', { name: '复制平台身份' })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '复制跨平台身份' })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '复制 Discord 用户 ID' })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '复制 IP 地址' })).toBeVisible()

  stage = 'updated'
  await page.getByRole('button', { name: '刷新在线玩家' }).click()
  await expect(dialog.getByText('Player Updated', { exact: true })).toBeVisible()

  stage = 'missing'
  await page.getByRole('button', { name: '刷新在线玩家' }).click()
  await expect(dialog.getByRole('alert')).toContainText('该玩家观察已不可用')
  await expect(dialog.getByText('Player Updated', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('button', { name: '踢出玩家' })).toHaveCount(0)

  stage = 'reappeared'
  await page.getByRole('button', { name: '刷新在线玩家' }).click()
  await expect(dialog.getByRole('alert')).toBeVisible()
  await expect(dialog.getByText('Player Updated', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: '取消' }).click()
  const reappearedDetailsButton = page.getByRole('button', { name: '查看玩家详情：Player Reappeared' })
  await reappearedDetailsButton.click()
  await expect(page.getByRole('dialog').getByRole('alert')).toHaveCount(0)
  await expect(page.getByRole('dialog').getByRole('button', { name: '踢出玩家' })).toBeVisible()
})

for (const viewport of [
  { name: 'desktop', width: 1280, height: 900 },
  { name: 'mobile-390', width: 390, height: 844 },
  { name: 'mobile-320', width: 320, height: 844 },
]) {
  test(`details remain accessible without horizontal overflow at ${viewport.name}`, async ({ page }) => {
    const stage: OnlinePlayerStage = 'initial'
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await interceptOnlinePlayers(page, () => stage)
    await page.goto('/players')
    await loginOwner(page)

    const detailsButton = page.getByRole('button', { name: '查看玩家详情：Nullable Player' })
    await detailsButton.click()
    const dialog = page.getByRole('dialog')

    await expect(dialog.getByText('未知', { exact: true })).toHaveCount(9)
    await expect(dialog.getByText('-100, 51, -200')).toBeVisible()
    await expect(dialog.getByText('9,876,543')).toBeVisible()
    await expect(dialog.getByRole('button', { name: '复制跨平台身份' })).toHaveCount(0)
    await expect(dialog.getByRole('button', { name: '复制 Discord 用户 ID' })).toHaveCount(0)
    await expect(dialog.getByRole('button', { name: '复制 IP 地址' })).toHaveCount(0)
    await expect(dialog.locator('button').filter({ hasText: '踢出玩家' })).toBeVisible()

    const bodyHasScrollableOverflow = await dialog.evaluate((element) => {
      return Array.from(element.querySelectorAll<HTMLElement>('*')).some((child) => {
        const style = getComputedStyle(child)
        return (style.overflowY === 'auto' || style.overflowY === 'scroll')
          && child.scrollHeight > child.clientHeight
      })
    })
    const hasHorizontalOverflow = await page.evaluate(() => (
      document.documentElement.scrollWidth > document.documentElement.clientWidth
    ))
    expect(bodyHasScrollableOverflow).toBe(true)
    expect(hasHorizontalOverflow).toBe(false)

    await page.keyboard.press('Escape')
    await expect(detailsButton).toBeFocused()
  })
}
