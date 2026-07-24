import type { ComputedRef, InjectionKey, Ref } from 'vue'
import type { SupportedLocale } from './locale'
import type { LocalePreferenceRepository } from './localePreference'

import { en, zh_cn as zhCN } from '@nuxt/ui/locale'

import { setGlobalConfig } from 'valibot'
import { computed, inject, readonly } from 'vue'
import { createI18n } from 'vue-i18n'
import { createBrowserLocalePreferenceRepository } from './localePreference'

import enMessages from './locales/en.json'
import zhCNMessages from './locales/zh-CN.json'
import '@valibot/i18n/zh-CN'

export {
  DEFAULT_LOCALE,
  matchSupportedLocale,
  negotiateLocale,
  type SupportedLocale,
} from './locale'
export {
  createBrowserLocalePreferenceRepository,
  LOCALE_PREFERENCE_STORAGE_KEY,
  type LocalePreferenceRepository,
  parseLocalePreference,
  serializeLocalePreference,
} from './localePreference'

function createConfiguredI18n(initialLocale: SupportedLocale) {
  return createI18n({
    legacy: false,
    locale: initialLocale,
    fallbackLocale: 'en',
    messages: {
      'en': enMessages,
      'zh-CN': zhCNMessages,
    },
    datetimeFormats: {
      'en': {
        medium: { dateStyle: 'medium', timeStyle: 'short' },
        playerObservation: { dateStyle: 'short', timeStyle: 'medium' },
      },
      'zh-CN': {
        medium: { dateStyle: 'medium', timeStyle: 'short' },
        playerObservation: { dateStyle: 'short', timeStyle: 'medium' },
      },
    },
  })
}

export interface AdminLocaleRuntime {
  i18n: ReturnType<typeof createConfiguredI18n>
  locale: Readonly<Ref<SupportedLocale>>
  nuxtLocale: ComputedRef<typeof en | typeof zhCN>
  setLocale: (locale: SupportedLocale) => void
  dispose: () => void
}

export interface CreateAdminI18nOptions {
  repository?: LocalePreferenceRepository
  documentElement?: Pick<HTMLElement, 'lang'>
}

export const ADMIN_LOCALE_KEY: InjectionKey<AdminLocaleRuntime> = Symbol('admin-locale')

function browserLanguages(): readonly string[] {
  if (navigator.languages.length > 0)
    return navigator.languages
  return navigator.language === '' ? [] : [navigator.language]
}

function createDefaultRepository(): LocalePreferenceRepository {
  return createBrowserLocalePreferenceRepository({
    getStorage: () => window.localStorage,
    eventTarget: window,
    browserLanguages,
  })
}

export function createAdminI18n(options: CreateAdminI18nOptions = {}): AdminLocaleRuntime {
  const repository = options.repository ?? createDefaultRepository()
  const documentElement = options.documentElement ?? document.documentElement
  const initialLocale = repository.restore()
  const i18n = createConfiguredI18n(initialLocale)

  const locale = i18n.global.locale as Ref<SupportedLocale>
  const nuxtLocale = computed(() => locale.value === 'zh-CN' ? zhCN : en)

  function applyLocale(nextLocale: SupportedLocale) {
    locale.value = nextLocale
    setGlobalConfig({ lang: nextLocale })
    documentElement.lang = nextLocale
  }

  function setLocale(nextLocale: SupportedLocale) {
    applyLocale(nextLocale)
    repository.save(nextLocale)
  }

  applyLocale(initialLocale)
  const unsubscribe = repository.subscribe(applyLocale)

  return {
    i18n,
    locale: readonly(locale),
    nuxtLocale,
    setLocale,
    dispose: unsubscribe,
  }
}

export function useAdminLocale(): AdminLocaleRuntime {
  const runtime = inject(ADMIN_LOCALE_KEY)
  if (runtime === undefined)
    throw new Error('Admin locale runtime is not available')
  return runtime
}
