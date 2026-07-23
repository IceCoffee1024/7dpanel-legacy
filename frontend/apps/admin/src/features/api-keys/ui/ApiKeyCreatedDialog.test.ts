import type { CreatedApiKey } from '../api/apiKeys'

import { mount } from '@vue/test-utils'
import { afterEach, expect, it, vi } from 'vitest'

import ApiKeyCreatedDialog from './ApiKeyCreatedDialog.vue'

const createdApiKey: CreatedApiKey = {
  id: 's0m3K3y1d3nt1f13r00000',
  name: 'Server backup automation',
  apiKey: '7dp_k_s0m3K3y1d3nt1f13r00000_1234567890123456789012345678901234567890123',
  createdAtUtc: '2026-07-23T08:00:00.0000000+00:00',
  expiresAtUtc: null,
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
    </section>
  `,
}

function mountDialog() {
  return mount(ApiKeyCreatedDialog, {
    props: {
      'open': true,
      createdApiKey,
      'onUpdate:open': () => {},
    },
    global: {
      stubs: {
        Modal: modalStub,
        UModal: modalStub,
        Button: {
          emits: ['click'],
          template: '<button @click="$emit(\'click\')"><slot /></button>',
        },
        UButton: {
          emits: ['click'],
          template: '<button @click="$emit(\'click\')"><slot /></button>',
        },
      },
    },
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

it('shows the complete API Key only in its one-time result area', () => {
  const wrapper = mountDialog()

  expect(wrapper.get('[data-testid="one-time-api-key"]').text()).toBe(createdApiKey.apiKey)
  expect(wrapper.text()).toContain('关闭此窗口后将无法再次查看')
})

it('copies the complete API Key and keeps it out of feedback', async () => {
  const writeText = vi.fn().mockResolvedValue(undefined)
  vi.stubGlobal('navigator', { clipboard: { writeText } })
  const wrapper = mountDialog()

  await wrapper.get('[data-testid="copy-api-key"]').trigger('click')
  await vi.waitFor(() => expect(writeText).toHaveBeenCalledWith(createdApiKey.apiKey))

  expect(wrapper.get('[role="status"]').text()).toBe('API Key 已复制')
  expect(wrapper.get('[role="status"]').text()).not.toContain(createdApiKey.apiKey)
})

it('reports clipboard failure without exposing the complete API Key', async () => {
  vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockRejectedValue(new Error('clipboard unavailable')) } })
  const wrapper = mountDialog()

  await wrapper.get('[data-testid="copy-api-key"]').trigger('click')
  await vi.waitFor(() => expect(wrapper.get('[role="status"]').text()).toBe('复制失败，请手动保存 API Key'))

  expect(wrapper.get('[role="status"]').text()).not.toContain(createdApiKey.apiKey)
})

it('emits closure only from the explicit completion action', async () => {
  const wrapper = mountDialog()

  await wrapper.get('[data-testid="close-created-api-key"]').trigger('click')

  expect(wrapper.emitted('update:open')).toEqual([[false]])
})
