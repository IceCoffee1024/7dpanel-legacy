import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import BackupsView from './BackupsView.vue'

function controller() {
  return {
    state: readonly(shallowRef('ready')),
    backups: readonly(shallowRef(Object.freeze([]))),
    activeJob: readonly(shallowRef(null)),
    isMutating: readonly(shallowRef(false)),
    errorCode: readonly(shallowRef(null)),
    create: vi.fn().mockResolvedValue(true),
    download: vi.fn().mockResolvedValue(true),
    remove: vi.fn().mockResolvedValue(true),
    restore: vi.fn().mockResolvedValue(true),
    refresh: vi.fn(),
    dispose: vi.fn(),
  }
}

function policyController() {
  return {
    isSaving: readonly(shallowRef(false)),
    refresh: vi.fn(),
  }
}

describe('backupsView', () => {
  it('submits each fixed backup kind through the controller', async () => {
    const value = controller()
    const wrapper = mount(BackupsView, {
      props: { controller: value as never, policyController: policyController() as never },
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
          FormField: { template: '<label><slot/></label>' },
          UFormField: { template: '<label><slot/></label>' },
          Input: { template: '<input />' },
          UInput: { template: '<input />' },
          Button: { props: ['disabled', 'label'], template: '<button :disabled="disabled">{{ label }}<slot/></button>' },
          UButton: { props: ['disabled', 'label'], template: '<button :disabled="disabled">{{ label }}<slot/></button>' },
          Table: { template: '<div><slot name="empty"/></div>' },
          UTable: { template: '<div><slot name="empty"/></div>' },
          Alert: true,
          UAlert: true,
          BackupPoliciesPanel: true,
          Modal: true,
          UModal: true,
        },
      },
    })

    await wrapper.get('[data-testid="create-world-backup"]').trigger('click')
    await wrapper.get('[data-testid="create-panel-database-backup"]').trigger('click')
    await wrapper.get('[data-testid="create-server-configuration-backup"]').trigger('click')

    expect(value.create).toHaveBeenNthCalledWith(1, 'World', '')
    expect(value.create).toHaveBeenNthCalledWith(2, 'PanelDatabase', '')
    expect(value.create).toHaveBeenNthCalledWith(3, 'ServerConfiguration', '')
  })
})
