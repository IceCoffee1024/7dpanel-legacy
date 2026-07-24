import type { SupportedLocale } from './locale'

import { negotiateLocale } from './locale'

export const LOCALE_PREFERENCE_STORAGE_KEY = '7dpanel.locale.v1'

export interface LocalePreferenceRepository {
  restore: () => SupportedLocale
  save: (locale: SupportedLocale) => boolean
  subscribe: (listener: (locale: SupportedLocale) => void) => () => void
}

export interface BrowserLocalePreferenceRepositoryOptions {
  getStorage: () => Storage
  eventTarget: Pick<Window, 'addEventListener' | 'removeEventListener'>
  browserLanguages: () => readonly string[]
}

function isSupportedLocale(value: unknown): value is SupportedLocale {
  return value === 'en' || value === 'zh-CN'
}

export function parseLocalePreference(value: string | null): SupportedLocale | null {
  if (value === null)
    return null

  try {
    const record: unknown = JSON.parse(value)
    if (typeof record !== 'object' || record === null || Array.isArray(record))
      return null

    const entries = record as Record<string, unknown>
    if (Object.keys(entries).sort().join(',') !== 'locale,version')
      return null

    return entries.version === 1 && isSupportedLocale(entries.locale)
      ? entries.locale
      : null
  }
  catch {
    return null
  }
}

export function serializeLocalePreference(locale: SupportedLocale): string {
  return JSON.stringify({ version: 1, locale })
}

function getStorageSafely(getStorage: () => Storage): Storage | null {
  try {
    return getStorage()
  }
  catch {
    return null
  }
}

function readStoredValue(getStorage: () => Storage): string | null {
  const storage = getStorageSafely(getStorage)
  if (storage === null)
    return null

  try {
    return storage.getItem(LOCALE_PREFERENCE_STORAGE_KEY)
  }
  catch {
    return null
  }
}

function removeStoredValue(getStorage: () => Storage) {
  const storage = getStorageSafely(getStorage)
  if (storage === null)
    return

  try {
    storage.removeItem(LOCALE_PREFERENCE_STORAGE_KEY)
  }
  catch {}
}

export function createBrowserLocalePreferenceRepository(
  options: BrowserLocalePreferenceRepositoryOptions,
): LocalePreferenceRepository {
  function negotiateBrowserLocale() {
    return negotiateLocale(options.browserLanguages())
  }

  return {
    restore() {
      const storedValue = readStoredValue(options.getStorage)
      const locale = parseLocalePreference(storedValue)
      if (locale !== null)
        return locale

      if (storedValue !== null)
        removeStoredValue(options.getStorage)

      return negotiateBrowserLocale()
    },
    save(locale) {
      const storage = getStorageSafely(options.getStorage)
      if (storage === null)
        return false

      try {
        storage.setItem(LOCALE_PREFERENCE_STORAGE_KEY, serializeLocalePreference(locale))
        return true
      }
      catch {
        return false
      }
    },
    subscribe(listener) {
      function onStorage(event: StorageEvent) {
        if (event.key !== LOCALE_PREFERENCE_STORAGE_KEY)
          return

        const storage = getStorageSafely(options.getStorage)
        if (storage !== null && event.storageArea !== storage)
          return

        listener(parseLocalePreference(event.newValue) ?? negotiateBrowserLocale())
      }

      options.eventTarget.addEventListener('storage', onStorage)
      return () => options.eventTarget.removeEventListener('storage', onStorage)
    },
  }
}
