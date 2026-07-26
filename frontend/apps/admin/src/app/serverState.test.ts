import type { MutationCache, QueryCache } from '@pinia/colada'
import { PiniaColada } from '@pinia/colada'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent } from 'vue'

import { useAuthStore } from '../features/auth'
import { clearServerStateCache, connectServerState } from './serverState'

describe('server state cache', () => {
  it('cancels and removes protected query and mutation entries', () => {
    const queryEntries = [{ id: 'overview' }, { id: 'players' }]
    const mutationEntries = [{ id: 'restart' }]
    const queryCache = {
      cancelQueries: vi.fn(),
      getEntries: vi.fn(() => queryEntries),
      remove: vi.fn(),
    } as unknown as QueryCache
    const mutationCache = {
      getEntries: vi.fn(() => mutationEntries),
      remove: vi.fn(),
    } as unknown as MutationCache

    clearServerStateCache(queryCache, mutationCache)

    expect(queryCache.cancelQueries).toHaveBeenCalledOnce()
    expect(queryCache.remove).toHaveBeenCalledTimes(2)
    expect(mutationCache.remove).toHaveBeenCalledOnce()
  })

  it('starts one stream for the active session and resets it on replacement or logout', () => {
    const pinia = createPinia()
    const app = createApp(defineComponent({ render: () => null }))
    app.use(pinia)
    app.use(PiniaColada)
    const auth = useAuthStore(pinia)
    auth.$patch({
      expiresAt: Date.now() + 60_000,
      role: 'Owner',
      token: 'first-token',
      username: 'admin',
    })
    const serverEvents = {
      start: vi.fn(),
      stop: vi.fn(),
    }

    const disconnect = connectServerState(pinia, serverEvents)
    expect(serverEvents.start).toHaveBeenCalledWith('Bearer first-token')

    auth.$patch({ token: 'second-token' })
    expect(serverEvents.stop).toHaveBeenCalledWith({ clearCursor: true })
    expect(serverEvents.start).toHaveBeenLastCalledWith('Bearer second-token')

    auth.logout()
    expect(serverEvents.stop).toHaveBeenCalledTimes(2)

    disconnect()
  })
})
