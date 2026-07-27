import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { readonly, shallowRef } from 'vue'

import AutomationView from './AutomationView.vue'

const selectedRule = Object.freeze({
  id: 'welcome', version: 2, name: 'Welcome', isEnabled: true, trigger: { type: 'PlayerJoined' as const },
  condition: { nodeId: 'root', kind: 'All' as const, children: [
    { nodeId: 'group', kind: 'Predicate' as const, predicate: { fieldKey: 'actor.group', operator: 'Equals' as const, scalarValue: 'member' } },
    { nodeId: 'permission', kind: 'Predicate' as const, predicate: { fieldKey: 'actor.permission', operator: 'Permission' as const, scalarValue: '1' } },
  ] },
  actions: [
    { id: 'first', type: 'PrivateMessage' as const, target: { kind: 'TriggerPlayer' as const }, privateMessage: { message: 'First' } },
    { id: 'second', type: 'BroadcastMessage' as const, target: { kind: 'Global' as const }, broadcastMessage: { message: 'Second' } },
  ],
  cooldownSeconds: 30, cooldownScope: 'RulePlayer' as const, concurrencyPolicy: 'SkipIfRunning' as const, failurePolicy: 'Continue' as const,
  createdAtUtc: '2026-07-27T00:00:00Z', updatedAtUtc: '2026-07-27T00:00:00Z',
})

function controller() {
  return {
    state: readonly(shallowRef('ready')),
    rules: readonly(shallowRef(Object.freeze([selectedRule]))),
    selected: readonly(shallowRef(selectedRule)),
    executions: readonly(shallowRef(Object.freeze([{ executionId: 'execution-1', ruleId: 'welcome', triggerId: 'trigger-1', status: 'Failed', correlationId: 'corr-1', startedAtUtc: '2026-07-27T00:00:00Z', completedAtUtc: '2026-07-27T00:00:01Z', errorCode: 'action_failed', conditions: [], actions: [{ ordinal: 0, actionType: 'PrivateMessage', status: 'Failed', errorCode: 'target_unavailable', startedAtUtc: '2026-07-27T00:00:00Z', completedAtUtc: '2026-07-27T00:00:01Z' }] }]))),
    selectedExecution: readonly(shallowRef(null)),
    executionState: readonly(shallowRef('available')),
    executionDetailState: readonly(shallowRef('ready')),
    isMutating: readonly(shallowRef(false)), errorCode: readonly(shallowRef(null)), validation: readonly(shallowRef(null)),
    dryRunResult: readonly(shallowRef({ validation: { isValid: true, issues: [] }, evaluation: { truth: 'Matched', trace: [{ nodeId: 'group', fieldKey: 'actor.group', truth: 'Matched', isValueKnown: true }] }, plannedActions: [{ ordinal: 0, actionId: 'first', actionType: 'PrivateMessage', dependency: { status: 'Available' }, target: { isResolved: true }, wouldExecute: true }] })),
    select: vi.fn(), refresh: vi.fn(), save: vi.fn().mockResolvedValue(true), remove: vi.fn(), validate: vi.fn(), dryRun: vi.fn(), loadExecution: vi.fn(), dispose: vi.fn(),
  }
}

const stubs = {
  DashboardPanel: { template: '<main><slot name="header"/><slot name="body"/></main>' }, UDashboardPanel: { template: '<main><slot name="header"/><slot name="body"/></main>' },
  DashboardNavbar: { template: '<header><slot name="leading"/><slot name="right"/></header>' }, UDashboardNavbar: { template: '<header><slot name="leading"/><slot name="right"/></header>' }, DashboardSidebarCollapse: true, UDashboardSidebarCollapse: true,
  Container: { template: '<div><slot/></div>' }, UContainer: { template: '<div><slot/></div>' }, Card: { template: '<section><slot name="header"/><slot/></section>' }, UCard: { template: '<section><slot name="header"/><slot/></section>' },
  Alert: { props: ['title', 'description'], template: '<div role="alert">{{ title }} {{ description }}<slot name="description"/><slot name="actions"/></div>' }, UAlert: { props: ['title', 'description'], template: '<div role="alert">{{ title }} {{ description }}<slot name="description"/><slot name="actions"/></div>' },
  Badge: { props: ['label'], template: '<span>{{ label }}<slot/></span>' }, UBadge: { props: ['label'], template: '<span>{{ label }}<slot/></span>' },
  Button: { props: ['label', 'disabled', 'type'], emits: ['click'], template: '<button :disabled="disabled" :type="type" @click="$emit(\'click\')">{{ label }}<slot/></button>' }, UButton: { props: ['label', 'disabled', 'type'], emits: ['click'], template: '<button :disabled="disabled" :type="type" @click="$emit(\'click\')">{{ label }}<slot/></button>' },
  Form: { template: '<form @submit.prevent="$emit(\'submit\')"><slot/></form>' }, UForm: { template: '<form @submit.prevent="$emit(\'submit\')"><slot/></form>' }, FormField: { props: ['label'], template: '<label>{{ label }}<slot/></label>' }, UFormField: { props: ['label'], template: '<label>{{ label }}<slot/></label>' },
  Input: { props: ['modelValue', 'type'], emits: ['update:modelValue'], template: '<input :type="type" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' }, UInput: { props: ['modelValue', 'type'], emits: ['update:modelValue'], template: '<input :type="type" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
  Textarea: { props: ['modelValue'], emits: ['update:modelValue'], template: '<textarea :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' }, UTextarea: { props: ['modelValue'], emits: ['update:modelValue'], template: '<textarea :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
  Select: true, USelect: true, InputNumber: true, UInputNumber: true, Switch: true, USwitch: true, Skeleton: true, USkeleton: true, Table: { template: '<table><slot name="empty"/></table>' }, UTable: { template: '<table><slot name="empty"/></table>' },
}

describe('AutomationView', () => {
  it('warns before replacing a composite condition tree and keeps dry-run output visible', async () => {
    const value = controller()
    const wrapper = mount(AutomationView, { props: { controller: value as never }, global: { stubs } })

    expect(wrapper.text()).toContain('Welcome')
    expect(wrapper.text()).toContain('复合条件树')
    expect(wrapper.text()).toContain('Matched')
    expect(wrapper.text()).toContain('计划动作 1 个')
    expect(value.save).not.toHaveBeenCalled()
  })
})
