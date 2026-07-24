import ui from '@nuxt/ui/vue-plugin'
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { nextTick, readonly, shallowRef } from 'vue'

import OverviewPage from './index.vue'

const sampleTime = new Date(2026, 6, 23, 14, 5).getTime()
const health = {
  state: readonly(shallowRef<'fresh'>('fresh')),
  data: readonly(shallowRef({ status: 'ok' as const, product: '7DPanel', version: '3.0.1-b4' })),
  error: readonly(shallowRef(null)),
  lastSuccessfulAt: readonly(shallowRef(sampleTime)),
  refresh: vi.fn(),
  dispose: vi.fn(),
}

vi.mock('../composables/useServerHealth', () => ({
  useServerHealth: () => health,
}))

function formatSample(locale: 'en' | 'zh-CN') {
  return new Intl.DateTimeFormat(locale, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(sampleTime)
}

describe('overview page', () => {
  it('reactively translates status while preserving technical values and formatting the same sample', async () => {
    const wrapper = mount(OverviewPage, {
      global: {
        plugins: [ui],
        stubs: {
          UDashboardPanel: { template: '<main><slot name="header" /><slot name="body" /></main>' },
          UDashboardNavbar: { props: ['title'], template: '<header>{{ title }}<slot name="leading" /></header>' },
          UDashboardSidebarCollapse: true,
        },
      },
    })

    expect(wrapper.text()).toContain('服务器运行正常')
    expect(wrapper.text()).toContain(formatSample('zh-CN'))
    expect(wrapper.text()).toContain('7DPanel')
    expect(wrapper.text()).toContain('3.0.1-b4')

    wrapper.vm.$i18n.locale = 'en'
    await nextTick()

    expect(wrapper.text()).toContain('The server is running normally')
    expect(wrapper.text()).toContain(formatSample('en'))
    expect(wrapper.text()).toContain('7DPanel')
    expect(wrapper.text()).toContain('3.0.1-b4')
  })
})
