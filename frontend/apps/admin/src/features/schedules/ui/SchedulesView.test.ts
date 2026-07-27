import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import SchedulesView from './SchedulesView.vue'

function controller() {
  return {
    state: readonly(shallowRef('ready')),
    schedules: readonly(shallowRef(Object.freeze([]))),
    isMutating: readonly(shallowRef(false)),
    errorCode: readonly(shallowRef(null)),
    announce: vi.fn().mockResolvedValue(true),
    save: vi.fn().mockResolvedValue(true),
    setEnabled: vi.fn().mockResolvedValue(true),
    remove: vi.fn().mockResolvedValue(true),
    refresh: vi.fn(),
    dispose: vi.fn(),
  }
}

describe('schedulesView', () => {
  it('submits an immediate announcement as plain text', async () => {
    const value = controller()
    const wrapper = mount(SchedulesView, {
      props: { controller: value as never },
      global: {
        stubs: {
          DashboardPanel: { template: '<main><slot name="header"/><slot name="body"/></main>' },
          UDashboardPanel: { template: '<main><slot name="header"/><slot name="body"/></main>' },
          DashboardNavbar: { template: '<header><slot name="leading"/><slot name="right"/></header>' },
          UDashboardNavbar: { template: '<header><slot name="leading"/><slot name="right"/></header>' },
          DashboardSidebarCollapse: true,
          UDashboardSidebarCollapse: true,
          Container: { template: '<div><slot/></div>' },
          UContainer: { template: '<div><slot/></div>' },
          Card: { template: '<section><slot name="header"/><slot/></section>' },
          UCard: { template: '<section><slot name="header"/><slot/></section>' },
          Form: { template: '<form @submit.prevent="$emit(\'submit\')"><slot/></form>' },
          UForm: { template: '<form @submit.prevent="$emit(\'submit\')"><slot/></form>' },
          FormField: { template: '<label><slot/></label>' },
          UFormField: { template: '<label><slot/></label>' },
          Textarea: {
            props: ['modelValue'],
            emits: ['update:modelValue'],
            template: '<textarea :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
          },
          UTextarea: {
            props: ['modelValue'],
            emits: ['update:modelValue'],
            template: '<textarea :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
          },
          Button: { props: ['disabled', 'label', 'type'], template: '<button :disabled="disabled" :type="type">{{ label }}<slot/></button>' },
          UButton: { props: ['disabled', 'label', 'type'], template: '<button :disabled="disabled" :type="type">{{ label }}<slot/></button>' },
          Table: { template: '<div><slot name="empty"/></div>' },
          UTable: { template: '<div><slot name="empty"/></div>' },
          Alert: true,
          UAlert: true,
          Modal: true,
          UModal: true,
        },
      },
    })

    await wrapper.get('textarea').setValue('Server restart in 10 minutes')
    await wrapper.get('form').trigger('submit')

    expect(value.announce).toHaveBeenCalledWith('Server restart in 10 minutes')
  })
})
