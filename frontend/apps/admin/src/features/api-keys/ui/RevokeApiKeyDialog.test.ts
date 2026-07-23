import type { ApiKeyMetadata } from '../api/apiKeys'
import type { ApiKeysFeedback } from '../model/useApiKeys'

import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'

import RevokeApiKeyDialog from './RevokeApiKeyDialog.vue'

const apiKey: ApiKeyMetadata = {
  id: 's0m3K3y1d3nt1f13r00000',
  displayPrefix: '7dp_k_s0m3K3y1d3nt1f13r00000',
  name: 'Server backup automation',
  createdAtUtc: '2026-07-23T08:00:00.0000000+00:00',
  lastUsedAtUtc: null,
  expiresAtUtc: null,
  status: 'active',
}

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

function mountDialog(options: {
  isSubmitting?: boolean
  feedback?: ApiKeysFeedback | null
} = {}) {
  return mount(RevokeApiKeyDialog, {
    props: {
      'open': true,
      apiKey,
      'isSubmitting': options.isSubmitting ?? false,
      'feedback': options.feedback ?? null,
      'onUpdate:open': () => {},
    },
    global: {
      stubs: {
        Modal: modalStub,
        UModal: modalStub,
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

it('shows only the fixed safe Key identity before revocation', () => {
  const unsafeApiKey = {
    ...apiKey,
    apiKey: '7dp_k_complete-secret-must-not-render',
    secretHash: 'must-not-render',
  } as unknown as ApiKeyMetadata
  const wrapper = mount(RevokeApiKeyDialog, {
    props: { 'open': true, 'apiKey': unsafeApiKey, 'isSubmitting': false, 'feedback': null, 'onUpdate:open': () => {} },
    global: {
      stubs: {
        Modal: modalStub,
        UModal: modalStub,
        Button: { template: '<button><slot /></button>' },
        UButton: { template: '<button><slot /></button>' },
      },
    },
  })

  expect(wrapper.text()).toContain(apiKey.name)
  expect(wrapper.text()).toContain(apiKey.displayPrefix)
  expect(wrapper.text()).not.toContain('complete-secret')
  expect(wrapper.text()).not.toContain('must-not-render')
})

it('emits confirmation once and locks cancellation while submitting', async () => {
  const wrapper = mountDialog({ isSubmitting: true })

  expect(wrapper.get('[data-testid="confirm-revoke-api-key"]').attributes()).toHaveProperty('disabled')
  expect(wrapper.get('[data-testid="cancel-revoke-api-key"]').attributes()).toHaveProperty('disabled')
  await wrapper.get('[data-testid="modal-dismiss"]').trigger('click')

  expect(wrapper.emitted('update:open')).toBeUndefined()
  expect(wrapper.emitted('confirm')).toBeUndefined()
})

it('emits a destructive confirmation and keeps stable failure feedback visible', async () => {
  const wrapper = mountDialog({
    feedback: { code: 'revoke-failed', message: '撤销 API Key 失败，请稍后重试' },
  })

  await wrapper.get('[data-testid="confirm-revoke-api-key"]').trigger('click')

  expect(wrapper.emitted('confirm')).toEqual([[]])
  expect(wrapper.get('[role="status"]').text()).toBe('撤销 API Key 失败，请稍后重试')
})
