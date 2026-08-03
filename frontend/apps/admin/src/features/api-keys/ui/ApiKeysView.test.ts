import type { ApiKeyMetadata, CreatedApiKey } from '../api/apiKeys'
import type { ApiKeysController, ApiKeysFeedback, ApiKeysState } from '../model/useApiKeys'

import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { nextTick, readonly, shallowRef } from 'vue'

import ApiKeysView from './ApiKeysView.vue'

const { routerReplaceMock, useApiKeysMock } = vi.hoisted(() => ({
  routerReplaceMock: vi.fn(),
  useApiKeysMock: vi.fn(),
}))

vi.mock('../model/useApiKeys', () => ({
  useApiKeys: useApiKeysMock,
}))

vi.mock('vue-router', async importOriginal => ({
  ...await importOriginal<typeof import('vue-router')>(),
  useRouter: () => ({ replace: routerReplaceMock }),
}))

const apiKey: ApiKeyMetadata = {
  id: 's0m3K3y1d3nt1f13r00000',
  displayPrefix: '7dp_k_s0m3K3y1d3nt1f13r00000',
  name: 'Server backup automation',
  createdAtUtc: '2026-07-23T08:00:00.0000000+00:00',
  lastUsedAtUtc: null,
  expiresAtUtc: null,
  status: 'active',
}

const createdApiKey: CreatedApiKey = {
  id: apiKey.id,
  name: apiKey.name,
  apiKey: '7dp_k_s0m3K3y1d3nt1f13r00000_1234567890123456789012345678901234567890123',
  createdAtUtc: apiKey.createdAtUtc,
  expiresAtUtc: null,
}

interface ControllerValues {
  state?: ApiKeysState
  apiKeys?: readonly ApiKeyMetadata[]
  feedback?: ApiKeysFeedback | null
  createdApiKey?: CreatedApiKey | null
  isRefreshing?: boolean
  isCreating?: boolean
  revokingKeyId?: string | null
}

function mountApiKeysView(values: ControllerValues = {}) {
  const state = shallowRef<ApiKeysState>(values.state ?? 'fresh')
  const apiKeys = shallowRef<readonly ApiKeyMetadata[]>(values.apiKeys ?? [])
  const feedback = shallowRef<ApiKeysFeedback | null>(values.feedback ?? null)
  const created = shallowRef<CreatedApiKey | null>(values.createdApiKey ?? null)
  const isRefreshing = shallowRef(values.isRefreshing ?? false)
  const isCreating = shallowRef(values.isCreating ?? false)
  const revokingKeyId = shallowRef<string | null>(values.revokingKeyId ?? null)
  const refresh = vi.fn().mockResolvedValue(undefined)
  const create = vi.fn().mockResolvedValue(true)
  const revoke = vi.fn().mockResolvedValue(true)
  const clearFeedback = vi.fn(() => {
    feedback.value = null
  })
  const clearCreatedApiKey = vi.fn(() => {
    created.value = null
  })
  const controller: ApiKeysController = {
    state: readonly(state),
    apiKeys: readonly(apiKeys),
    feedback: readonly(feedback),
    createdApiKey: readonly(created),
    isRefreshing: readonly(isRefreshing),
    isCreating: readonly(isCreating),
    revokingKeyId: readonly(revokingKeyId),
    refresh,
    create,
    revoke,
    clearFeedback,
    clearCreatedApiKey,
    dispose: vi.fn(),
  }
  useApiKeysMock.mockReturnValue(controller)

  const wrapper = mount(ApiKeysView, {
    global: {
      stubs: {
        DashboardPanel: {
          template: '<section><slot name="header" /><slot name="body" /></section>',
        },
        DashboardNavbar: {
          template: '<header><slot name="leading" /><slot name="right" /></header>',
        },
        DashboardSidebarCollapse: true,
        UDashboardSidebarCollapse: true,
        UIcon: true,
        Tooltip: { template: '<span><slot /></span>' },
        UTooltip: { template: '<span><slot /></span>' },
        CreateApiKeyDialog: {
          props: ['open', 'isCreating', 'feedback'],
          emits: ['update:open', 'create'],
          template: `
            <section v-if="open" data-testid="create-dialog">
              <button data-testid="create-submit" @click="$emit('create', { name: 'nightly backup' })">创建</button>
            </section>
          `,
        },
        ApiKeyCreatedDialog: {
          props: ['open', 'createdApiKey'],
          emits: ['update:open'],
          template: `
            <section v-if="open" data-testid="created-dialog">
              <code data-testid="one-time-api-key">{{ createdApiKey?.apiKey }}</code>
              <button data-testid="close-created-dialog" @click="$emit('update:open', false)">关闭</button>
            </section>
          `,
        },
        RevokeApiKeyDialog: {
          props: ['open', 'apiKey', 'isSubmitting', 'feedback'],
          emits: ['update:open', 'confirm'],
          template: `
            <section v-if="open" data-testid="revoke-dialog">
              <span data-testid="revoke-key-name">{{ apiKey?.name }}</span>
              <button data-testid="revoke-confirm" @click="$emit('confirm')">撤销</button>
              <button data-testid="revoke-cancel" @click="$emit('update:open', false)">取消</button>
            </section>
          `,
        },
      },
    },
  })

  return {
    wrapper,
    apiKeys,
    created,
    refresh,
    create,
    revoke,
    clearCreatedApiKey,
    onSessionExpired: useApiKeysMock.mock.calls[0]?.[0]?.onSessionExpired as () => void,
  }
}

beforeEach(() => {
  routerReplaceMock.mockReset()
  useApiKeysMock.mockReset()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

it('renders an actionable empty state', () => {
  const { wrapper } = mountApiKeysView({ state: 'empty' })

  expect(wrapper.get('[data-testid="api-keys-empty"]').text()).toContain('尚未创建 API Key')
  expect(wrapper.get('[data-testid="create-api-key"]').text()).toContain('创建 API Key')
})

it('renders only safe metadata for each API Key', () => {
  const unsafeKey = {
    ...apiKey,
    apiKey: createdApiKey.apiKey,
    secretHash: 'must-not-render',
  } as unknown as ApiKeyMetadata
  const { wrapper } = mountApiKeysView({ apiKeys: [unsafeKey] })

  expect(wrapper.text()).toContain(apiKey.name)
  expect(wrapper.text()).toContain(apiKey.displayPrefix)
  expect(wrapper.text()).not.toContain(createdApiKey.apiKey)
  expect(wrapper.text()).not.toContain('must-not-render')
})

it('switches metadata labels to English without translating the Key identity', async () => {
  const { wrapper } = mountApiKeysView({ apiKeys: [apiKey] })

  wrapper.vm.$i18n.locale = 'en'
  await nextTick()

  expect(wrapper.text()).toContain('Active')
  expect(wrapper.text()).toContain('Created')
  expect(wrapper.text()).toContain(apiKey.name)
  expect(wrapper.text()).toContain(apiKey.displayPrefix)
})

it('submits creation through the controller and displays the one-time Key', async () => {
  const { wrapper, create, created } = mountApiKeysView({ state: 'empty' })

  await wrapper.get('[data-testid="create-api-key"]').trigger('click')
  await wrapper.get('[data-testid="create-submit"]').trigger('click')

  expect(create).toHaveBeenCalledWith({ name: 'nightly backup' })
  created.value = createdApiKey
  await wrapper.vm.$nextTick()

  expect(wrapper.get('[data-testid="one-time-api-key"]').text()).toBe(createdApiKey.apiKey)
})

it('clears the one-time Key when its dialog closes', async () => {
  const { wrapper, clearCreatedApiKey } = mountApiKeysView({ createdApiKey })

  await wrapper.get('[data-testid="close-created-dialog"]').trigger('click')

  expect(clearCreatedApiKey).toHaveBeenCalledOnce()
})

it('requires a fixed Key confirmation before revocation and leaves the row unchanged on failure', async () => {
  const { wrapper, revoke, apiKeys } = mountApiKeysView({
    apiKeys: [apiKey],
  })
  revoke.mockResolvedValueOnce(false)

  await wrapper.get(`[data-testid="revoke-${apiKey.id}"]`).trigger('click')
  expect(wrapper.get('[data-testid="revoke-key-name"]').text()).toBe(apiKey.name)
  await wrapper.get('[data-testid="revoke-confirm"]').trigger('click')
  await flushPromises()

  expect(revoke).toHaveBeenCalledWith(apiKey)
  expect(apiKeys.value).toEqual([apiKey])
})

it('redirects to login when the controller reports an expired session', () => {
  const { onSessionExpired } = mountApiKeysView({
    state: 'failed',
    feedback: { code: 'session-expired' },
  })
  onSessionExpired()

  expect(routerReplaceMock).toHaveBeenCalledWith({
    path: '/login',
    query: { redirect: '/system/api-keys' },
  })
})
