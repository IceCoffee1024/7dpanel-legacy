import type { LocalePreferenceRepository } from '../app/i18n'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import { ADMIN_LOCALE_KEY, createAdminI18n } from '../app/i18n'
import LocaleMenu from './LocaleMenu.vue'

function mountMenu(initialLocale: 'en' | 'zh-CN', collapsed = false) {
  const repository: LocalePreferenceRepository = {
    restore: () => initialLocale,
    save: vi.fn(() => true),
    subscribe: () => () => {},
  }
  const runtime = createAdminI18n({
    repository,
    documentElement: { lang: '' },
  })
  const wrapper = mount(LocaleMenu, {
    props: { collapsed },
    attachTo: document.body,
    global: {
      plugins: [runtime.i18n],
      provide: { [ADMIN_LOCALE_KEY as symbol]: runtime },
    },
  })

  return { repository, runtime, wrapper }
}

describe('localeMenu', () => {
  it('shows both native language names and marks the current locale', async () => {
    const { runtime, wrapper } = mountMenu('en')

    expect(wrapper.get('[data-testid="locale-menu-trigger"]').text()).toBe('English')
    await wrapper.get('[data-testid="locale-menu-trigger"]').trigger('click')
    await flushPromises()
    const selectedItem = document.body.querySelector<HTMLElement>('[role="menuitemcheckbox"][aria-checked="true"]')
    expect(selectedItem?.textContent).toContain('English')
    expect(document.body.textContent).toContain('简体中文')
    runtime.dispose()
  })

  it('switches locale through the shared runtime', async () => {
    const { repository, runtime, wrapper } = mountMenu('en')

    await wrapper.get('[data-testid="locale-menu-trigger"]').trigger('click')
    await flushPromises()
    const simplifiedChinese = [...document.body.querySelectorAll<HTMLElement>('[role="menuitemcheckbox"]')]
      .find(item => item.textContent?.includes('简体中文'))
    expect(simplifiedChinese).toBeDefined()
    simplifiedChinese?.click()
    await flushPromises()

    expect(runtime.locale.value).toBe('zh-CN')
    expect(repository.save).toHaveBeenCalledExactlyOnceWith('zh-CN')
    expect(wrapper.get('[data-testid="locale-menu-trigger"]').text()).toBe('简体中文')
    runtime.dispose()
  })

  it('uses a localized accessible name when collapsed', () => {
    const { runtime, wrapper } = mountMenu('zh-CN', true)
    const trigger = wrapper.get('[data-testid="locale-menu-trigger"]')

    expect(trigger.text()).toBe('')
    expect(trigger.attributes('aria-label')).toBe('语言：简体中文')
    runtime.dispose()
  })
})
