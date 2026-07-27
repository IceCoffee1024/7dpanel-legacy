import type { App } from 'vue'

import type { ChatMutePage, ChatMuteWriteInput, CreateChatMuteInput } from '../api/chatMutes'
import { flushPromises } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { createApp } from 'vue'
import { useChatMutes } from './useChatMutes'

const mute = Object.freeze({
  crossplatformId: 'EOS_player',
  displayName: 'Player',
  reason: 'spam',
  mutedUntilUtc: null,
  createdBy: 'owner',
  createdAtUtc: '2026-07-26T08:00:00Z',
  updatedBy: 'owner',
  updatedAtUtc: '2026-07-26T08:00:00Z',
})

function page(): ChatMutePage {
  return Object.freeze({
    mutes: Object.freeze([mute]),
    nextCursor: null,
  })
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

function mountComposable(overrides: Record<string, unknown>) {
  let result!: ReturnType<typeof useChatMutes>
  const app = createApp({
    setup() {
      result = useChatMutes({
        auth: { authorizationHeader: 'Bearer owner', expireSession: vi.fn() },
        ...overrides,
      })
      return () => null
    },
  })
  app.mount(document.createElement('div'))
  return { app, result }
}

describe('useChatMutes', () => {
  const apps: App[] = []

  afterEach(() => {
    while (apps.length > 0)
      apps.pop()!.unmount()
  })

  it('locks concurrent mutations and refreshes after a successful create', async () => {
    const pending = deferred<typeof mute>()
    const load = vi.fn().mockResolvedValue(page())
    const create = vi.fn((_authorization: string, _input: CreateChatMuteInput) => pending.promise)
    const mounted = mountComposable({ load, create })
    apps.push(mounted.app)
    await flushPromises()

    const input: CreateChatMuteInput = {
      crossplatformId: 'EOS_new',
      displayName: null,
      reason: 'spam',
      mutedUntilUtc: null,
      correlationId: null,
    }
    const first = mounted.result.create(input)
    const duplicate = await mounted.result.create(input)

    expect(duplicate).toBe(false)
    expect(create).toHaveBeenCalledOnce()
    expect(mounted.result.isMutating.value).toBe(true)

    pending.resolve(mute)
    await first

    expect(mounted.result.isMutating.value).toBe(false)
    expect(load).toHaveBeenCalledTimes(2)
  })

  it('keeps the last successful list stale when refresh fails', async () => {
    const load = vi.fn()
      .mockResolvedValueOnce(page())
      .mockRejectedValueOnce(new Error('offline'))
    const mounted = mountComposable({ load })
    apps.push(mounted.app)
    await flushPromises()

    await mounted.result.refresh()

    expect(mounted.result.state.value).toBe('stale')
    expect(mounted.result.mutes.value[0]?.crossplatformId).toBe('EOS_player')
  })

  it('serializes update and release through the same mutation lock', async () => {
    const pending = deferred<typeof mute>()
    const update = vi.fn((_header: string, _id: string, _input: ChatMuteWriteInput) => pending.promise)
    const release = vi.fn().mockResolvedValue(undefined)
    const mounted = mountComposable({ load: vi.fn().mockResolvedValue(page()), update, release })
    apps.push(mounted.app)
    await flushPromises()

    const updating = mounted.result.update('EOS_player', {
      displayName: 'Player',
      reason: 'updated',
      mutedUntilUtc: null,
      correlationId: null,
    })
    expect(await mounted.result.release('EOS_player', null)).toBe(false)
    expect(release).not.toHaveBeenCalled()

    pending.resolve(mute)
    await updating
    expect(update).toHaveBeenCalledOnce()
  })
})
