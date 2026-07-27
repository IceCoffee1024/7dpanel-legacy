import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import type { WorldOperationRecord } from '../api/worldTools'

import { createInitialWorldOperationForm, createWorldOperationReview } from '../model/worldOperationForm'
import WorldOperationConfirmDialog from './WorldOperationConfirmDialog.vue'
import WorldOperationHistory from './WorldOperationHistory.vue'
import WorldOperationPanel from './WorldOperationPanel.vue'
import WorldReadDetails from './WorldReadDetails.vue'

const summary = {
  sourceState: 'Success' as const,
  worldId: 'world-1',
  worldVersion: 'world-v7',
  seed: null,
  width: 8192,
  height: 8192,
  gameVersion: '3.0.1-b4',
  mapResourceVersion: 'map-v3',
  availableExtent: null,
  observedAtUtc: '2026-07-26T10:00:00.000Z',
}

const stubs = {
  UAlert: { props: ['title', 'description'], template: '<div role="alert">{{ title }} {{ description }}<slot/></div>' },
  UBadge: { template: '<span><slot/></span>' },
  UButton: { props: ['label', 'disabled'], emits: ['click'], template: '<button :disabled="disabled" @click="$emit(\'click\')">{{ label }}<slot/></button>' },
  UFormField: { props: ['label', 'description'], template: '<label>{{ label }} {{ description }}<slot/></label>' },
  UInput: { props: ['modelValue'], emits: ['update:modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
  UInputNumber: true,
  UModal: { template: '<section><slot name="body"/><slot name="footer"/></section>' },
  USelect: true,
  UTable: { template: '<table><slot name="empty"/></table>' },
}

const nuxtUiStubs = {
  ...stubs,
  Alert: stubs.UAlert,
  Badge: stubs.UBadge,
  Button: stubs.UButton,
  FormField: stubs.UFormField,
  Input: stubs.UInput,
  InputNumber: stubs.UInputNumber,
  Modal: stubs.UModal,
  Select: stubs.USelect,
  Table: stubs.UTable,
}

function worldOperation(status: WorldOperationRecord['status']): WorldOperationRecord {
  return {
    operationId: 'operation-1',
    jobId: '7257ce31-623a-48d7-a5b8-406a181fb5db',
    kind: 'RenderFullMap',
    worldId: 'world-1',
    worldVersion: 'world-v7',
    mapResourceVersion: 'map-v3',
    correlationId: 'correlation-1',
    confirmationSummary: 'Render full map for world-1',
    isReversible: false,
    changeSetId: null,
    status,
    progress: null,
    errorCode: status === 'RollbackFailed' ? 'rollback_failed' : 'result_unknown',
    createdAtUtc: '2026-07-26T10:00:00.000Z',
    startedAtUtc: '2026-07-26T10:00:01.000Z',
    completedAtUtc: '2026-07-26T10:00:02.000Z',
  }
}

describe('WorldOperationConfirmDialog', () => {
  it('keeps the complete strong confirmation visible and requires CONFIRM', async () => {
    const form = createInitialWorldOperationForm()
    form.type = 'renderFullMap'
    const review = createWorldOperationReview(form, summary)
    const wrapper = mount(WorldOperationConfirmDialog, {
      props: { open: true, review, submitting: false },
      global: { stubs: nuxtUiStubs },
    })

    expect(wrapper.text()).toContain('Complete map tiles')
    expect(wrapper.text()).toContain('world-1')
    expect(wrapper.text()).toContain('Entire available map')
    expect(wrapper.text()).toContain('world-v7')
    expect(wrapper.text()).toContain('map-v3')
    expect(wrapper.text()).toContain('substantial server resources')

    const confirmButton = wrapper.find('[data-testid="confirm-world-operation"]')
    expect(confirmButton.attributes('disabled')).toBeDefined()
    await wrapper.find('input').setValue('CONFIRM')
    await confirmButton.trigger('click')
    expect(wrapper.emitted('confirm')).toHaveLength(1)
  })
})

describe('WorldOperationPanel', () => {
  it('does not render world mutation controls for non-Owners', () => {
    const wrapper = mount(WorldOperationPanel, {
      props: { summary, canMutate: false, submitting: false },
      global: { stubs: nuxtUiStubs },
    })

    expect(wrapper.text()).toContain('Owner')
    expect(wrapper.find('[data-testid="world-operation-form"]').exists()).toBe(false)
  })
})

describe('WorldOperationHistory', () => {
  it.each([
    ['ResultUnknown'],
    ['RollbackFailed'],
  ] as const)('renders a persistent high-severity alert for %s', (status) => {
    const wrapper = mount(WorldOperationHistory, {
      props: { operation: worldOperation(status), receipt: null, state: 'terminal', errorCode: null },
      global: { stubs: nuxtUiStubs },
    })

    expect(wrapper.text()).toContain(status)
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })
})

describe('WorldReadDetails', () => {
  it('shows all source labels, observation time, and nullable values honestly', () => {
    const resource = <T,>(sourceState: 'Success' | 'Partial' | 'Stale' | 'Unavailable', data: T | null) => ({
      phase: data === null ? 'failed' as const : 'ready' as const,
      sourceState,
      data,
      errorCode: data === null ? 'unavailable' as const : null,
    })
    const empty = (sourceState: 'Success' | 'Partial' | 'Stale') => resource(sourceState, {
      sourceState,
      observedAtUtc: '2026-07-26T10:00:00.000Z',
      items: [],
    })
    const catalog = resource('Success', {
      sourceState: 'Success' as const,
      catalogVersion: 'catalog-4',
      observedAtUtc: '2026-07-26T10:00:00.000Z',
      items: [],
    })
    const wrapper = mount(WorldReadDetails, {
      props: {
        summary: resource('Success', summary),
        landClaims: empty('Partial'),
        vehicles: empty('Stale'),
        drones: resource('Unavailable', null),
        containers: empty('Success'),
        blockCatalog: catalog,
        prefabCatalog: catalog,
        entityTypeCatalog: catalog,
      },
      global: { stubs: { ...nuxtUiStubs, Tabs: { template: '<div><slot/></div>' }, UTabs: { template: '<div><slot/></div>' } } },
    })

    expect(wrapper.text()).toContain('Success')
    expect(wrapper.text()).toContain('Partial')
    expect(wrapper.text()).toContain('Stale')
    expect(wrapper.text()).toContain('Unavailable')
    expect(wrapper.text()).toContain('2026')
    expect(wrapper.text()).toContain('—')
  })
})
