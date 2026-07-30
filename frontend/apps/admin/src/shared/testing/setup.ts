import { icons as lucideIcons } from '@iconify-json/lucide'
import { config, enableAutoUnmount } from '@vue/test-utils'
import { afterEach, beforeEach, vi } from 'vitest'

import { ADMIN_LOCALE_KEY, createAdminI18n } from '../../app/i18n'

const testLocaleRuntime = createAdminI18n({
  repository: {
    restore: () => 'zh-CN',
    save: () => true,
    subscribe: () => () => {},
  },
  documentElement: { lang: '' },
})

const unmockedRequests: string[] = []
const blockedFetch = vi.fn<typeof fetch>((input) => {
  const target = input instanceof Request ? input.url : String(input)
  const url = new URL(target, location.origin)
  if (
    ['api.iconify.design', 'api.simplesvg.com', 'api.unisvg.com'].includes(url.hostname)
    && url.pathname === '/lucide.json'
  ) {
    return Promise.resolve(Response.json(lucideIcons))
  }
  unmockedRequests.push(target)
  return Promise.reject(new Error(`Unmocked network request: ${target}`))
})

config.global.plugins.push(testLocaleRuntime.i18n)
config.global.provide[ADMIN_LOCALE_KEY as unknown as string] = testLocaleRuntime

vi.stubGlobal('fetch', blockedFetch)

enableAutoUnmount(afterEach)

beforeEach(() => {
  vi.stubGlobal('fetch', blockedFetch)
})

afterEach(() => {
  testLocaleRuntime.setLocale('zh-CN')
  vi.useRealTimers()

  const requests = unmockedRequests.splice(0)
  blockedFetch.mockClear()
  if (requests.length > 0)
    throw new Error(`Unit test attempted unmocked network access:\n${requests.join('\n')}`)
})
