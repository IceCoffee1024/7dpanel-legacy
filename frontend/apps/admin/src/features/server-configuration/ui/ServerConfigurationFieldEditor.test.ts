import type { ServerConfigurationField } from '../api/serverConfiguration'

import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import ServerConfigurationFieldEditor from './ServerConfigurationFieldEditor.vue'

const baseField: ServerConfigurationField = {
  key: 'Example',
  value: '',
  group: 'Advanced',
  valueType: 'text',
  editable: true,
  advanced: false,
  sensitive: false,
  isSet: false,
  restartRequired: true,
  allowedValues: [],
  minimum: null,
  maximum: null,
}

const stubs = {
  UCheckbox: { props: ['modelValue'], template: '<button data-testid="boolean-editor" />' },
  USelect: { props: ['modelValue', 'items'], template: '<select data-testid="enum-editor" />' },
  UInput: { props: ['modelValue', 'type', 'min', 'max', 'step'], template: '<input data-testid="scalar-editor" :type="type" :min="min" :max="max" :step="step" />' },
}

function mountEditor(field: ServerConfigurationField) {
  return mount(ServerConfigurationFieldEditor, {
    props: { field, modelValue: field.value },
    global: { stubs },
  })
}

describe('serverConfigurationFieldEditor', () => {
  it('uses dedicated boolean and enum controls', () => {
    expect(mountEditor({ ...baseField, valueType: 'boolean', value: 'true' }).find('[data-testid="boolean-editor"]').exists()).toBe(true)
    expect(mountEditor({ ...baseField, valueType: 'enum', allowedValues: ['A', 'B'] }).find('[data-testid="enum-editor"]').exists()).toBe(true)
  })

  it('uses constrained numeric and plain text controls', () => {
    const integer = mountEditor({ ...baseField, valueType: 'integer', minimum: 1, maximum: 64 })
    expect(integer.find('[data-testid="scalar-editor"]').attributes()).toMatchObject({ type: 'number', min: '1', max: '64', step: '1' })
    expect(mountEditor({ ...baseField, valueType: 'decimal' }).find('[data-testid="scalar-editor"]').attributes('step')).toBe('any')
    expect(mountEditor(baseField).find('[data-testid="scalar-editor"]').attributes('type')).toBe('text')
  })
})
