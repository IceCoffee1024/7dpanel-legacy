import type { GameEventsController } from '../../game-events/model/useGameEvents'
import type { AuditWorkspaceController } from '../model/useAuditWorkspace'
import { mount } from '@vue/test-utils'

import { describe, expect, it, vi } from 'vitest'
import { defineComponent, h, readonly, shallowRef } from 'vue'
import { createEmptyAuditFilters } from '../model/audit'
import AuditWorkspace from './AuditWorkspace.vue'

const TabsStub = defineComponent({
  props: {
    items: { type: Array, required: true },
    modelValue: { type: String, required: true },
  },
  emits: ['update:modelValue'],
  setup(props, { emit, slots }) {
    return () => h('div', [
      ...(props.items as Array<{ label: string, value: string }>).map(item => h('button', {
        'data-testid': `tab-${item.value}`,
        'onClick': () => emit('update:modelValue', item.value),
      }, item.label)),
      slots[props.modelValue]?.(),
    ])
  },
})

function auditController(): AuditWorkspaceController {
  return {
    state: readonly(shallowRef('ready')),
    entries: readonly(shallowRef(Object.freeze([]))),
    sourceGaps: readonly(shallowRef(Object.freeze([]))),
    filters: readonly(shallowRef(Object.freeze(createEmptyAuditFilters()))),
    nextCursor: readonly(shallowRef('audit-cursor')),
    pageNumber: readonly(shallowRef(1)),
    applyFilters: vi.fn(),
    goToPage: vi.fn(),
    refresh: vi.fn(),
    retry: vi.fn(),
    dispose: vi.fn(),
  }
}

function eventsController(): GameEventsController {
  return {
    state: readonly(shallowRef('stale')),
    events: readonly(shallowRef(Object.freeze([]))),
    gaps: readonly(shallowRef(Object.freeze([]))),
    filters: readonly(shallowRef(Object.freeze({ fromUtc: '', toUtc: '', eventType: '', crossplatformId: '' }))),
    nextCursor: readonly(shallowRef('event-cursor')),
    pageNumber: readonly(shallowRef(3)),
    applyFilters: vi.fn(),
    goToPage: vi.fn(),
    refresh: vi.fn(),
    retry: vi.fn(),
    dispose: vi.fn(),
  }
}

describe('auditWorkspace', () => {
  it('switches independent audit and game-event panels without sharing their state', async () => {
    const wrapper = mount(AuditWorkspace, {
      props: { audit: auditController(), gameEvents: eventsController() },
      global: {
        stubs: {
          Tabs: TabsStub,
          UTabs: TabsStub,
          DashboardPanel: { template: '<main><slot name="header" /><slot name="body" /></main>' },
          UDashboardPanel: { template: '<main><slot name="header" /><slot name="body" /></main>' },
          DashboardNavbar: { template: '<header><slot name="leading" /><slot name="right" /></header>' },
          UDashboardNavbar: { template: '<header><slot name="leading" /><slot name="right" /></header>' },
          DashboardSidebarCollapse: true,
          UDashboardSidebarCollapse: true,
          Button: true,
          UButton: true,
          AuditEntriesTable: { props: ['controller'], template: '<div data-testid="audit-panel">{{ controller.nextCursor.value }}</div>' },
          GameEventsTable: { props: ['controller'], template: '<div data-testid="events-panel">{{ controller.nextCursor.value }}:{{ controller.pageNumber.value }}</div>' },
        },
      },
    })

    expect(wrapper.get('[data-testid="audit-panel"]').text()).toBe('audit-cursor')
    expect(wrapper.find('[data-testid="events-panel"]').exists()).toBe(false)

    await wrapper.get('[data-testid="tab-game-events"]').trigger('click')

    expect(wrapper.get('[data-testid="events-panel"]').text()).toBe('event-cursor:3')
    expect(wrapper.find('[data-testid="audit-panel"]').exists()).toBe(false)
  })
})
