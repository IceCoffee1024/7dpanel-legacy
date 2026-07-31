import type { BrowserContext, Page } from '@playwright/test'

import { expect, test } from '@playwright/test'

const localePreferenceStorageKey = '7dpanel.locale.v1'
const adminUrl = process.env.SEVENDPANEL_ADMIN_URL
const username = process.env.PANEL_USERNAME
const password = process.env.PANEL_PASSWORD
const hasOwnerSmokeEnvironment = [adminUrl, username, password]
  .every(value => value !== undefined && value.trim() !== '')
const missingOwnerSmokeReason = 'Requires SEVENDPANEL_ADMIN_URL, PANEL_USERNAME, and PANEL_PASSWORD for a running real OWIN Owner environment.'

test.skip(!hasOwnerSmokeEnvironment, missingOwnerSmokeReason)

async function setBrowserLanguages(context: BrowserContext, languages: readonly string[]) {
  await context.addInitScript((preferredLanguages) => {
    Object.defineProperty(navigator, 'languages', {
      configurable: true,
      get: () => preferredLanguages,
    })
    Object.defineProperty(navigator, 'language', {
      configurable: true,
      get: () => preferredLanguages[0] ?? '',
    })
  }, languages)
}

async function selectLocale(page: Page, locale: 'en' | 'zh-CN') {
  await page.getByTestId('locale-menu-trigger').click()
  const nativeName = locale === 'en' ? 'English' : '简体中文'
  await page.getByRole('menuitemcheckbox', { name: nativeName }).click()
  await expect(page.getByRole('menu')).toBeHidden()
  await expect(page.locator('html')).toHaveAttribute('lang', locale)
}

async function expectStoredLocale(page: Page, locale: 'en' | 'zh-CN') {
  const record = await page.evaluate(storageKey => localStorage.getItem(storageKey), localePreferenceStorageKey)
  expect(record).toBe(JSON.stringify({ version: 1, locale }))
}

async function loginOwner(page: Page, locale: 'en' | 'zh-CN') {
  const labels = locale === 'en'
    ? { username: 'Username', password: 'Password', submit: 'Sign in' }
    : { username: '用户名', password: '密码', submit: '登录' }

  await page.getByLabel(labels.username).fill(username!)
  await page.getByLabel(labels.password).fill(password!)
  await page.getByRole('button', { name: labels.submit, exact: true }).click()
  await expect(page).toHaveURL(/\/players$/)
}

test('negotiates supported browser languages and falls back to English', async ({ browser }) => {
  const cases = [
    { languages: ['zh-Hans-CN'], locale: 'zh-CN', heading: '登录管理面板' },
    { languages: ['en-US'], locale: 'en', heading: 'Sign in to the admin panel' },
    { languages: ['fr-FR'], locale: 'en', heading: 'Sign in to the admin panel' },
    { languages: ['zh-TW', 'en-US'], locale: 'en', heading: 'Sign in to the admin panel' },
  ] as const

  for (const scenario of cases) {
    const context = await browser.newContext()
    try {
      await setBrowserLanguages(context, scenario.languages)
      const page = await context.newPage()
      await page.goto('/login')

      await expect(page.locator('html')).toHaveAttribute('lang', scenario.locale)
      await expect(page.getByRole('heading', { name: scenario.heading })).toBeVisible()
      expect(await page.evaluate(storageKey => localStorage.getItem(storageKey), localePreferenceStorageKey)).toBeNull()
    }
    finally {
      await context.close()
    }
  }
})

test('switches language before login without clearing credentials and persists across refresh', async ({ context, page }) => {
  await setBrowserLanguages(context, ['en-US'])
  await page.goto('/login')
  await page.getByLabel('Username').fill(username!)
  await page.getByLabel('Password').fill(password!)
  await page.getByRole('checkbox', { name: 'Keep me signed in' }).click()

  await selectLocale(page, 'zh-CN')

  await expect(page.getByLabel('用户名')).toHaveValue(username!)
  await expect(page.getByLabel('密码')).toHaveValue(password!)
  await expect(page.getByRole('checkbox', { name: '保持登录' })).toHaveAttribute('aria-checked', 'true')
  await expectStoredLocale(page, 'zh-CN')

  await page.reload()

  await expect(page.locator('html')).toHaveAttribute('lang', 'zh-CN')
  await expect(page.getByRole('heading', { name: '登录管理面板' })).toBeVisible()
  await expectStoredLocale(page, 'zh-CN')
})

test('keeps the selected locale and technical identity through login and logout', async ({ context, page }) => {
  await setBrowserLanguages(context, ['zh-CN'])
  await page.goto('/players')
  await selectLocale(page, 'en')
  await loginOwner(page, 'en')

  await expect(page.getByRole('heading', { name: 'Online players', exact: true })).toBeVisible()
  await page.getByTestId('account-menu-trigger').click()
  const accountMenu = page.getByRole('menu')
  await expect(accountMenu.getByText(username!, { exact: true })).toBeVisible()
  await expect(accountMenu.getByText('Owner', { exact: true })).toBeVisible()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()

  await expect(page).toHaveURL(url => (
    url.pathname === '/login' && url.searchParams.get('redirect') === '/players'
  ))
  await expect(page.locator('html')).toHaveAttribute('lang', 'en')
  await expect(page.getByRole('heading', { name: 'Sign in to the admin panel' })).toBeVisible()
  await expectStoredLocale(page, 'en')
})

test('English players layout has no horizontal overflow at 390 by 844', async ({ context, page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await setBrowserLanguages(context, ['en-US'])
  await page.goto('/players')
  await loginOwner(page, 'en')

  const hasHorizontalOverflow = await page.evaluate(() => (
    document.documentElement.scrollWidth > document.documentElement.clientWidth
  ))
  expect(hasHorizontalOverflow).toBe(false)
})
