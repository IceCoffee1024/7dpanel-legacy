import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import ConsoleWorkspace from './ConsoleWorkspace.vue'

interface ConsoleLogEntry {
  sequence: number
  formattedMessage: string | null
  message: string | null
  trace: string | null
  logType: string
}

const entries: ConsoleLogEntry[] = [
  {
    sequence: 1,
    formattedMessage: 'formatted line',
    message: 'fallback must not render',
    trace: 'trace line',
    logType: 'warning',
  },
  {
    sequence: 2,
    formattedMessage: '   ',
    message: 'fallback line',
    trace: null,
    logType: 'third-party-custom-type',
  },
]

function mountWorkspace(logs: ConsoleLogEntry[] = entries) {
  return mount(ConsoleWorkspace, {
    props: {
      entries: logs,
      snapshotLoading: false,
      connectionStatus: 'live',
      hasGap: false,
      unreadCount: 0,
      commandInput: '',
      commandCatalogUnavailable: false,
      commandSuggestions: [],
      selectedSuggestionIndex: 0,
      suggestionsOpen: false,
      isSubmitting: false,
    },
    global: {
      stubs: {
        UDashboardPanel: { template: '<section><slot name="header" /><slot /></section>' },
        UDashboardNavbar: { template: '<header><slot name="leading" /></header>' },
        UDashboardSidebarCollapse: true,
        UBadge: { template: '<span><slot /></span>' },
        UButton: {
          props: ['label', 'disabled'],
          emits: ['click'],
          template: '<button :disabled="disabled" @click="$emit(\'click\')">{{ label }}<slot /></button>',
        },
        ConsoleCommandBar: {
          template: '<div data-testid="command-bar" />',
        },
      },
    },
  })
}

describe('ConsoleWorkspace', () => {
  it('renders every log as escaped plain text with formatted fallback and subsequent trace', () => {
    const wrapper = mountWorkspace()
    const rows = wrapper.findAll('[data-testid="console-log-entry"]')

    expect(rows).toHaveLength(2)
    expect(rows[0]?.text()).toBe('formatted line\ntrace line')
    expect(rows[0]?.classes()).toContain('text-warning')
    expect(rows[1]?.text()).toBe('fallback line')
    expect(rows[1]?.classes()).toContain('text-default')
    expect(wrapper.text()).not.toContain('fallback must not render')
  })

  it('keeps unread logs while scrolled away and returns to the newest log on request', async () => {
    const wrapper = mountWorkspace([entries[0]!])
    const viewport = wrapper.get('[data-testid="console-log-viewport"]')
    Object.defineProperties(viewport.element, {
      clientHeight: { configurable: true, value: 100 },
      scrollHeight: { configurable: true, value: 500 },
    })
    viewport.element.scrollTop = 100
    await viewport.trigger('scroll')
    const leavingLatestEvents = wrapper.emitted('updateFollowingLatest') ?? []
    expect(leavingLatestEvents[leavingLatestEvents.length - 1]).toEqual([false])

    await wrapper.setProps({ entries, unreadCount: 1 })

    expect(wrapper.get('[data-testid="console-unread"]').text()).toContain('1')
    await wrapper.get('[data-testid="console-unread"]').trigger('click')
    expect(viewport.element.scrollTop).toBe(500)
    const returningLatestEvents = wrapper.emitted('updateFollowingLatest') ?? []
    expect(returningLatestEvents[returningLatestEvents.length - 1]).toEqual([true])
    await wrapper.setProps({ unreadCount: 0 })
    expect(wrapper.find('[data-testid="console-unread"]').exists()).toBe(false)
  })

  it('keeps command feedback and independent output outside the log viewport contract', () => {
    const wrapper = mountWorkspace()

    expect(wrapper.get('[data-testid="command-bar"]')).toBeDefined()
    expect(wrapper.find('[data-testid="command-output"]').exists()).toBe(false)
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })
})
