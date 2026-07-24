import type { ApiKeysFeedback } from '../model/useApiKeys'

import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'
import { nextTick } from 'vue'

import CreateApiKeyDialog from './CreateApiKeyDialog.vue'

const modalStub = {
  props: ['open', 'title', 'description'],
  emits: ['update:open'],
  template: `
    <section v-if="open" role="dialog">
      <h2>{{ title }}</h2>
      <p>{{ description }}</p>
      <slot name="body" />
      <slot name="footer" />
      <button data-testid="modal-dismiss" @click="$emit('update:open', false)">关闭</button>
    </section>
  `,
}

const inputStub = {
  props: ['modelValue', 'disabled'],
  emits: ['update:modelValue'],
  template: '<input :value="modelValue" :disabled="disabled" @input="$emit(\'update:modelValue\', $event.target.value)">',
}

function mountDialog(options: {
  isCreating?: boolean
  feedback?: ApiKeysFeedback | null
} = {}) {
  return mount(CreateApiKeyDialog, {
    props: {
      'open': true,
      'isCreating': options.isCreating ?? false,
      'feedback': options.feedback ?? null,
      'onUpdate:open': () => {},
    },
    global: {
      stubs: {
        Modal: modalStub,
        UModal: modalStub,
        Input: inputStub,
        UInput: inputStub,
        FormField: { template: '<div><slot /></div>' },
        UFormField: { template: '<div><slot /></div>' },
        Button: {
          props: ['disabled', 'loading'],
          emits: ['click'],
          template: '<button :disabled="disabled" @click="$emit(\'click\')"><slot /></button>',
        },
        UButton: {
          props: ['disabled', 'loading'],
          emits: ['click'],
          template: '<button :disabled="disabled" @click="$emit(\'click\')"><slot /></button>',
        },
      },
    },
  })
}

it('trims and emits the approved creation input', async () => {
  const wrapper = mountDialog()
  const inputs = wrapper.findAll('input')
  await inputs[0]!.setValue('  nightly backup  ')
  await inputs[1]!.setValue('2026-08-23T08:00:00.0000000+00:00')

  await wrapper.get('[data-testid="create-api-key-submit"]').trigger('click')

  expect(wrapper.emitted('create')).toEqual([[
    { name: 'nightly backup', expiresAtUtc: '2026-08-23T08:00:00.0000000+00:00' },
  ]])
})

it.each([
  ['an empty trimmed name', '   '],
  ['a name longer than 80 Unicode characters', '原'.repeat(81)],
] as const)('does not submit %s', async (_, name) => {
  const wrapper = mountDialog()

  await wrapper.find('input').setValue(name)

  expect(wrapper.get('[data-testid="create-api-key-submit"]').attributes()).toHaveProperty('disabled')
  expect(wrapper.emitted('create')).toBeUndefined()
})

it('locks all controls while creating and shows only stable feedback', async () => {
  const wrapper = mountDialog({
    isCreating: true,
    feedback: { code: 'create-failed' },
  })

  expect(wrapper.findAll('input').every(input => input.attributes('disabled') !== undefined)).toBe(true)
  expect(wrapper.get('[data-testid="create-api-key-submit"]').attributes()).toHaveProperty('disabled')
  await wrapper.get('[data-testid="modal-dismiss"]').trigger('click')

  expect(wrapper.emitted('update:open')).toBeUndefined()
  expect(wrapper.get('[role="status"]').text()).toBe('创建 API Key 失败，请稍后重试')
})

it('switches to English without clearing the name or expiration', async () => {
  const wrapper = mountDialog({
    feedback: { code: 'create-failed' },
  })
  const inputs = wrapper.findAll('input')
  await inputs[0]!.setValue('nightly backup')
  await inputs[1]!.setValue('2026-08-23T08:00:00Z')

  wrapper.vm.$i18n.locale = 'en'
  await nextTick()

  expect(wrapper.text()).toContain('Create API Key')
  expect(wrapper.text()).toContain('Failed to create API Key')
  expect(inputs[0]!.element.value).toBe('nightly backup')
  expect(inputs[1]!.element.value).toBe('2026-08-23T08:00:00Z')
})
