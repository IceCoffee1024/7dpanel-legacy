import { shallowMount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '../../auth'
import GameResourceIcon from './GameResourceIcon.vue'

describe('gameResourceIcon', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it.each(['missing', 'invalid'] as const)('renders the same accessible placeholder for %s icons', (iconStatus) => {
    const pinia = createPinia()
    const wrapper = shallowMount(GameResourceIcon, {
      props: { alt: 'Stone', iconStatus, resourceId: 'resource-1' },
      global: { plugins: [pinia], stubs: { UIcon: true } },
    })

    expect(wrapper.get('[data-testid="game-resource-icon-placeholder"]').attributes('aria-label')).toBe('Stone')
    expect(wrapper.find('img').exists()).toBe(false)
  })

  it('never places the Bearer token in rendered markup or the image URL', () => {
    const pinia = createPinia()
    const auth = useAuthStore(pinia)
    auth.token = 'private-token'
    auth.expiresAt = Date.now() + 60_000
    auth.username = 'owner'
    auth.role = 'Owner'
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')))
    vi.stubGlobal('IntersectionObserver', class {
      disconnect() {}
      observe() {}
    })

    const wrapper = shallowMount(GameResourceIcon, {
      props: { alt: 'Stone', iconStatus: 'available', resourceId: 'resource-1' },
      global: { plugins: [pinia], stubs: { UIcon: true } },
    })

    expect(wrapper.html()).not.toContain('private-token')
    expect(wrapper.html()).not.toContain('Authorization')
  })
})
