import { config, enableAutoUnmount } from '@vue/test-utils'
import { afterEach, vi } from 'vitest'

import { ADMIN_LOCALE_KEY, createAdminI18n } from '../../app/i18n'

const testLocaleRuntime = createAdminI18n({
  repository: {
    restore: () => 'zh-CN',
    save: () => true,
    subscribe: () => () => {},
  },
  documentElement: { lang: '' },
})

config.global.plugins.push(testLocaleRuntime.i18n)
config.global.provide[ADMIN_LOCALE_KEY as unknown as string] = testLocaleRuntime

enableAutoUnmount(afterEach)

afterEach(() => {
  testLocaleRuntime.setLocale('zh-CN')
  vi.useRealTimers()
})
