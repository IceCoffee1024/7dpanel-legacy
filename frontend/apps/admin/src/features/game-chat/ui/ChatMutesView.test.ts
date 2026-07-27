import type { ChatMutesController } from '../model/useChatMutes'
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import { readonly, shallowRef } from 'vue'
import ChatMutesView from './ChatMutesView.vue'

function controller(mutating = false): ChatMutesController {
  return {
    state: readonly(shallowRef('ready')),
    mutes: readonly(shallowRef(Object.freeze([Object.freeze({
      crossplatformId: 'EOS_technical_identity',
      displayName: null,
      reason: 'spam',
      mutedUntilUtc: null,
      createdBy: 'owner',
      createdAtUtc: '2026-07-26T08:00:00Z',
      updatedBy: 'owner',
      updatedAtUtc: '2026-07-26T08:00:00Z',
    })]))),
    nextCursor: readonly(shallowRef(null)),
    pageNumber: readonly(shallowRef(1)),
    isMutating: readonly(shallowRef(mutating)),
    create: vi.fn(),
    update: vi.fn(),
    release: vi.fn(),
    goToPage: vi.fn(),
    refresh: vi.fn(),
    retry: vi.fn(),
    dispose: vi.fn(),
  }
}

describe('chatMutesView', () => {
  it('renders permanent mutes without translating the technical identity', () => {
    const wrapper = mount(ChatMutesView, {
      props: { controller: controller() },
      global: {
        stubs: {
          DashboardPanel: { template: '<main><slot name="header" /><slot name="body" /></main>' },
          UDashboardPanel: { template: '<main><slot name="header" /><slot name="body" /></main>' },
          DashboardNavbar: { template: '<header><slot name="leading" /><slot name="right" /></header>' },
          UDashboardNavbar: { template: '<header><slot name="leading" /><slot name="right" /></header>' },
          DashboardSidebarCollapse: true,
          UDashboardSidebarCollapse: true,
          Table: true,
          UTable: true,
          Modal: true,
          UModal: true,
          Form: true,
          UForm: true,
          FormField: true,
          UFormField: true,
          Input: true,
          UInput: true,
          Textarea: true,
          UTextarea: true,
          Checkbox: true,
          UCheckbox: true,
          Button: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
          UButton: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
        },
      },
    })

    expect(wrapper.text()).toContain('EOS_technical_identity')
    expect(wrapper.text()).toContain('永久')
  })

  it('disables mutation entry points while a mutation is in flight', () => {
    const wrapper = mount(ChatMutesView, {
      props: { controller: controller(true) },
      global: {
        stubs: {
          DashboardPanel: { template: '<main><slot name="header" /><slot name="body" /></main>' },
          UDashboardPanel: { template: '<main><slot name="header" /><slot name="body" /></main>' },
          DashboardNavbar: { template: '<header><slot name="right" /></header>' },
          UDashboardNavbar: { template: '<header><slot name="right" /></header>' },
          DashboardSidebarCollapse: true,
          UDashboardSidebarCollapse: true,
          Table: true,
          UTable: true,
          Modal: true,
          UModal: true,
          Form: true,
          UForm: true,
          FormField: true,
          UFormField: true,
          Input: true,
          UInput: true,
          Textarea: true,
          UTextarea: true,
          Checkbox: true,
          UCheckbox: true,
          Button: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
          UButton: { props: ['disabled'], template: '<button :disabled="disabled"><slot /></button>' },
        },
      },
    })

    expect(wrapper.get('[data-testid="create-mute-button"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="edit-mute-EOS_technical_identity"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="release-mute-EOS_technical_identity"]').attributes('disabled')).toBeDefined()
  })
})
