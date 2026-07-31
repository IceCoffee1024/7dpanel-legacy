import type { Page } from '@playwright/test'

export type AdminLocale = 'en' | 'zh-CN'

const localePreferenceStorageKey = '7dpanel.locale.v1'

export async function setInitialAdminLocale(page: Page, locale: AdminLocale) {
  await page.addInitScript(({ storageKey, value }) => {
    localStorage.setItem(storageKey, JSON.stringify({ version: 1, locale: value }))
  }, {
    storageKey: localePreferenceStorageKey,
    value: locale,
  })
}
