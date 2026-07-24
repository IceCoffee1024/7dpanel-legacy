import type { SupportedLocale } from './locale'
import type { LocalePreferenceRepository } from './localePreference'

import { getGlobalConfig } from 'valibot'
import { describe, expect, it, vi } from 'vitest'

import { createAdminI18n } from './index'

function createRepository(initialLocale: SupportedLocale) {
  let listener: ((locale: SupportedLocale) => void) | null = null
  const repository: LocalePreferenceRepository = {
    restore: vi.fn(() => initialLocale),
    save: vi.fn(() => true),
    subscribe: vi.fn((nextListener) => {
      listener = nextListener
      return vi.fn(() => {
        listener = null
      })
    }),
  }

  return {
    emit(locale: SupportedLocale) {
      listener?.(locale)
    },
    repository,
  }
}

describe('createAdminI18n', () => {
  it('synchronizes the restored locale across all localization providers', () => {
    const { repository } = createRepository('zh-CN')
    const documentElement = { lang: '' }

    const runtime = createAdminI18n({ repository, documentElement })

    expect(runtime.locale.value).toBe('zh-CN')
    expect(runtime.i18n.global.locale.value).toBe('zh-CN')
    expect(runtime.nuxtLocale.value.code).toBe('zh-CN')
    expect(getGlobalConfig().lang).toBe('zh-CN')
    expect(documentElement.lang).toBe('zh-CN')
    runtime.dispose()
  })

  it('persists explicit changes and applies external changes without writing back', () => {
    const { emit, repository } = createRepository('en')
    const documentElement = { lang: '' }
    const runtime = createAdminI18n({ repository, documentElement })

    runtime.setLocale('zh-CN')

    expect(runtime.locale.value).toBe('zh-CN')
    expect(repository.save).toHaveBeenCalledExactlyOnceWith('zh-CN')

    emit('en')

    expect(runtime.locale.value).toBe('en')
    expect(runtime.nuxtLocale.value.code).toBe('en')
    expect(getGlobalConfig().lang).toBe('en')
    expect(documentElement.lang).toBe('en')
    expect(repository.save).toHaveBeenCalledTimes(1)
    runtime.dispose()
  })

  it('unsubscribes when disposed', () => {
    const { emit, repository } = createRepository('en')
    const runtime = createAdminI18n({ repository, documentElement: { lang: '' } })

    runtime.dispose()
    emit('zh-CN')

    expect(runtime.locale.value).toBe('en')
  })
})
