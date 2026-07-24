import type { ApiKeyMetadata, CreatedApiKey } from '../api/apiKeys'
import type { ApiKeysController } from './useApiKeys'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent, isReadonly } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useApiKeys } from './useApiKeys'

interface Deferred<T> {
  promise: Promise<T>
  resolve: (value: T) => void
  reject: (reason: unknown) => void
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

const apiKey: ApiKeyMetadata = Object.freeze({
  id: 's0m3K3y1d3nt1f13r00000',
  displayPrefix: '7dp_k_s0m3K3y1d3nt1f13r00000',
  name: 'Server backup automation',
  createdAtUtc: '2026-07-23T08:00:00.0000000+00:00',
  lastUsedAtUtc: null,
  expiresAtUtc: null,
  status: 'active',
})

const createdApiKey: CreatedApiKey = Object.freeze({
  id: apiKey.id,
  name: apiKey.name,
  apiKey: '7dp_k_s0m3K3y1d3nt1f13r00000_1234567890123456789012345678901234567890123',
  createdAtUtc: apiKey.createdAtUtc,
  expiresAtUtc: apiKey.expiresAtUtc,
})

function mountComposable(options: Parameters<typeof useApiKeys>[0]) {
  let controller!: ApiKeysController
  const wrapper = mount(defineComponent({
    setup() {
      controller = useApiKeys(options)
      return () => null
    },
  }))
  return { controller, wrapper }
}

describe('useApiKeys', () => {
  it('loads metadata on mount and exposes readonly state', async () => {
    const fetchKeys = vi.fn().mockResolvedValue([apiKey])
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer website-token', expireSession: vi.fn() },
      fetchKeys,
    })
    await flushPromises()

    expect(fetchKeys).toHaveBeenCalledWith('Bearer website-token', expect.any(AbortSignal))
    expect(controller.state.value).toBe('fresh')
    expect(controller.apiKeys.value).toEqual([apiKey])
    expect(isReadonly(controller.apiKeys)).toBe(true)
    expect(isReadonly(controller.createdApiKey)).toBe(true)
    wrapper.unmount()
  })

  it('enters empty after loading an empty list', async () => {
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer website-token', expireSession: vi.fn() },
      fetchKeys: vi.fn().mockResolvedValue([]),
    })
    await flushPromises()

    expect(controller.state.value).toBe('empty')
    expect(controller.apiKeys.value).toEqual([])
    wrapper.unmount()
  })

  it('expires the website session and redirects once after a 401', async () => {
    const expireSession = vi.fn()
    const onSessionExpired = vi.fn()
    const fetchKeys = vi.fn().mockRejectedValue(new HttpError('http', 'sensitive detail', { status: 401 }))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer website-token', expireSession },
      fetchKeys,
      onSessionExpired,
    })
    await flushPromises()

    expect(expireSession).toHaveBeenCalledOnce()
    expect(onSessionExpired).toHaveBeenCalledOnce()
    expect(controller.state.value).toBe('failed')
    expect(controller.feedback.value).toEqual({ code: 'session-expired' })
    expect(JSON.stringify(controller.feedback.value)).not.toContain('sensitive detail')
    wrapper.unmount()
  })

  it('enters forbidden without clearing the session after a 403 list response', async () => {
    const expireSession = vi.fn()
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer website-token', expireSession },
      fetchKeys: vi.fn().mockRejectedValue(new HttpError('http', 'forbidden', { status: 403 })),
    })
    await flushPromises()

    expect(controller.state.value).toBe('forbidden')
    expect(controller.apiKeys.value).toEqual([])
    expect(expireSession).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('keeps the complete Key only in createdApiKey and clears it explicitly', async () => {
    const createKey = vi.fn().mockResolvedValue(createdApiKey)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer website-token', expireSession: vi.fn() },
      fetchKeys: vi.fn().mockResolvedValue([]),
      createKey,
    })
    await flushPromises()

    await expect(controller.create({ name: '  Server backup automation  ' })).resolves.toBe(true)

    expect(createKey).toHaveBeenCalledWith('Bearer website-token', {
      name: '  Server backup automation  ',
    }, expect.any(AbortSignal))
    expect(controller.createdApiKey.value).toEqual(createdApiKey)
    expect(JSON.stringify(controller.apiKeys.value)).not.toContain(createdApiKey.apiKey)
    expect(JSON.stringify(controller.feedback.value)).not.toContain(createdApiKey.apiKey)

    controller.clearCreatedApiKey()

    expect(controller.createdApiKey.value).toBeNull()
    wrapper.unmount()
  })

  it('does not optimistically mutate the list when revocation fails', async () => {
    const revokeKey = vi.fn().mockRejectedValue(new HttpError('http', 'sensitive revoke detail', {
      status: 500,
      problemCode: 'api_key_revoke_failed',
    }))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer website-token', expireSession: vi.fn() },
      fetchKeys: vi.fn().mockResolvedValue([apiKey]),
      revokeKey,
    })
    await flushPromises()

    await expect(controller.revoke(apiKey)).resolves.toBe(false)

    expect(revokeKey).toHaveBeenCalledWith('Bearer website-token', apiKey.id, expect.any(AbortSignal))
    expect(controller.apiKeys.value).toEqual([apiKey])
    expect(controller.revokingKeyId.value).toBeNull()
    expect(controller.feedback.value).toEqual({ code: 'revoke-failed' })
    expect(JSON.stringify(controller.feedback.value)).not.toContain('sensitive revoke detail')
    wrapper.unmount()
  })

  it('uses one in-flight create request and preserves the one-time value after completion', async () => {
    const pending = deferred<CreatedApiKey>()
    const createKey = vi.fn(() => pending.promise)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer website-token', expireSession: vi.fn() },
      fetchKeys: vi.fn().mockResolvedValue([]),
      createKey,
    })
    await flushPromises()

    const first = controller.create({ name: 'first' })
    const second = controller.create({ name: 'second' })

    expect(createKey).toHaveBeenCalledOnce()
    expect(controller.isCreating.value).toBe(true)
    pending.resolve(createdApiKey)
    await expect(Promise.all([first, second])).resolves.toEqual([true, true])
    expect(controller.isCreating.value).toBe(false)
    expect(controller.createdApiKey.value?.apiKey).toBe(createdApiKey.apiKey)
    wrapper.unmount()
  })
})
