import { mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import BackupPoliciesPanel from './BackupPoliciesPanel.vue'

const drafts = [
  { kind: 'World', enabled: true, cronExpression: '0 4 * * *', timeZoneId: 'UTC', backupRootId: 'primary', retentionCount: 5, retentionDays: 14, compressionEnabled: true, rowVersion: 3 },
  { kind: 'PanelDatabase', enabled: true, cronExpression: '0 4 * * *', timeZoneId: 'UTC', backupRootId: 'primary', retentionCount: 5, retentionDays: 14, compressionEnabled: true, rowVersion: 3 },
  { kind: 'ServerConfiguration', enabled: true, cronExpression: '0 4 * * *', timeZoneId: 'UTC', backupRootId: 'primary', retentionCount: 5, retentionDays: 14, compressionEnabled: true, rowVersion: 3 },
] as const

it('renders one editable row for each fixed backup policy kind', () => {
  const wrapper = mount(BackupPoliciesPanel, {
    props: {
      controller: {
        state: readonly(shallowRef<'ready'>('ready')),
        policies: readonly(shallowRef(drafts)),
        drafts: readonly(shallowRef(drafts)),
        isSaving: readonly(shallowRef(false)),
        pendingKind: readonly(shallowRef(null)),
        errorCode: readonly(shallowRef(null)),
        saveError: readonly(shallowRef({ kind: 'World' as const, code: 'conflict' as const })),
        refresh: vi.fn(),
        updateDraft: vi.fn(),
        save: vi.fn(),
        dispose: vi.fn(),
      },
    },
    global: {
      stubs: {
        UAlert: { template: '<div><slot /></div>' },
        UButton: { template: '<button><slot /></button>' },
        Button: { template: '<button><slot /></button>' },
        UCard: { template: '<section><slot name="header" /><slot /></section>' },
        BackupPolicyForm: { template: '<div data-testid="backup-policy-row" />' },
      },
    },
  })

  expect(wrapper.findAll('[data-testid="backup-policy-row"]')).toHaveLength(3)
})
