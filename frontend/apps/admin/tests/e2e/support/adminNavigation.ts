import type { Page } from '@playwright/test'

import process from 'node:process'
import { expect } from '@playwright/test'

import { setInitialAdminLocale } from './adminLocale'

const authSessionStorageKey = '7dpanel.auth.session.v1'
const adminUrl = process.env.SEVENDPANEL_ADMIN_URL
const username = process.env.PANEL_USERNAME
const password = process.env.PANEL_PASSWORD
const hasOwnerSmokeEnvironment = [adminUrl, username, password]
  .every(value => value !== undefined && value.trim() !== '')

export const hasRealOwinNavigationEnvironment = hasOwnerSmokeEnvironment
export const missingRealOwinNavigationEnvironmentReason = 'Requires SEVENDPANEL_ADMIN_URL, PANEL_USERNAME, and PANEL_PASSWORD for a running real OWIN Owner environment.'

async function openSidebar(page: Page) {
  if ((page.viewportSize()?.width ?? 1280) >= 1024)
    return

  let lastError: unknown
  for (let attempt = 0; attempt < 5; attempt++) {
    const openSidebarButton = page.getByRole('button', { name: '打开侧边栏', exact: true })
    try {
      await expect(openSidebarButton).toBeVisible()
      await openSidebarButton.click({ timeout: 2_000 })
      lastError = undefined
      break
    }
    catch (error) {
      lastError = error
      await page.waitForTimeout(100)
    }
  }
  if (lastError !== undefined)
    throw lastError

  await expect(page.getByTestId('secondary-navigation').last()).toBeVisible()
}

async function configureApi(page: Page, mockApi: boolean) {
  if (!mockApi)
    return

  await page.route('**/api/v1/**', async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({ code: 'unavailable' }),
    })
  })
}

export async function runOwnerAdminNavigationScenario(page: Page, options: { mockApi: boolean }) {
  await setInitialAdminLocale(page, 'zh-CN')
  const pageErrors: string[] = []
  page.on('pageerror', error => pageErrors.push(error.message))
  await configureApi(page, options.mockApi)

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
  await openSidebar(page)

  const secondaryNavigation = page.getByTestId('secondary-navigation')

  await secondaryNavigation.getByRole('link', { name: '玩家档案与证据', exact: true }).click()
  await expect(page).toHaveURL(/\/players\/history$/)
  await expect(page.getByTestId('history-search')).toBeVisible()
  await openSidebar(page)
  await page.getByTestId('primary-navigation').getByRole('button', { name: '系统管理', exact: true }).click()
  await secondaryNavigation.getByRole('link', { name: 'API Keys', exact: true }).click()
  await expect(page).toHaveURL(/\/system\/api-keys$/)
  await openSidebar(page)
  await page.getByTestId('primary-navigation').getByRole('button', { name: '系统管理', exact: true }).click()
  await secondaryNavigation.getByRole('link', { name: '审计与事件', exact: true }).click()
  await expect(page).toHaveURL(/\/system\/audit$/)
  await openSidebar(page)
  await page.getByTestId('primary-navigation').getByRole('button', { name: '服务器运维', exact: true }).click()
  await secondaryNavigation.getByRole('link', { name: '网页控制台', exact: true }).click()
  await expect(page).toHaveURL(/\/operations\/console$/)

  await openSidebar(page)
  await page.getByTestId('primary-navigation').getByRole('button', { name: '社区', exact: true }).click()
  await secondaryNavigation.getByRole('link', { name: '游戏聊天', exact: true }).click()
  await expect(page).toHaveURL(/\/community\/chat\/live$/)
  const sectionTabs = page.getByTestId('section-tabs')
  await sectionTabs.getByRole('link', { name: '历史聊天', exact: true }).click()
  await expect(page).toHaveURL(/\/community\/chat\/history$/)
  await page.goBack()
  await expect(page).toHaveURL(/\/community\/chat\/live$/)
  await page.goForward()
  await expect(page).toHaveURL(/\/community\/chat\/history$/)
  await page.reload()
  await expect(sectionTabs.getByRole('link', { name: '历史聊天', exact: true })).toHaveAttribute('aria-current', 'page')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)

  expect(pageErrors).toEqual([])
}

export async function runViewerAdminNavigationScenario(page: Page, options: { mockApi: boolean }) {
  await setInitialAdminLocale(page, 'zh-CN')
  await configureApi(page, options.mockApi)

  await page.goto('/login')
  await page.evaluate(({ expiresAt, storageKey }) => {
    sessionStorage.setItem(storageKey, JSON.stringify({
      version: 1,
      token: '7dp_t_navigation-viewer.secret',
      expiresAt,
      username: 'navigation-viewer',
      role: 'Viewer',
    }))
  }, {
    expiresAt: Date.now() + 60_000,
    storageKey: authSessionStorageKey,
  })
  await page.goto('/players')
  await openSidebar(page)

  const secondaryNavigation = page.getByTestId('secondary-navigation')
  await expect(secondaryNavigation.getByRole('link', { name: '玩家', exact: true })).toBeVisible()
  await expect(secondaryNavigation.getByRole('link', { name: '访问名单', exact: true })).toBeVisible()
  await expect(secondaryNavigation.getByRole('link', { name: '玩家档案与证据', exact: true })).toHaveCount(0)
  await expect(secondaryNavigation.getByRole('link', { name: '地图', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '社区', exact: true })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '经济与奖励', exact: true })).toHaveCount(0)
}
