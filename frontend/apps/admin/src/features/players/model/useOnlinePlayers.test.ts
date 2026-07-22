import type { OnlinePlayersSnapshot } from '../api/onlinePlayers'
import type { OnlinePlayersController, VisibilitySource } from './useOnlinePlayers'

import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useOnlinePlayers } from './useOnlinePlayers'

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

function snapshot(capturedAtUtc: string): OnlinePlayersSnapshot {
  return Object.freeze({ capturedAtUtc, players: Object.freeze([]) })
}

function createVisibility(initiallyVisible = true) {
  let visible = initiallyVisible
  let listener: (() => void) | null = null
  const unsubscribe = vi.fn(() => {
    listener = null
  })
  const source: VisibilitySource = {
    isVisible: () => visible,
    subscribe: vi.fn((nextListener) => {
      listener = nextListener
      return unsubscribe
    }),
  }
  return {
    source,
    unsubscribe,
    setVisible(value: boolean) {
      visible = value
      listener?.()
    },
  }
}

function mountComposable(options: Parameters<typeof useOnlinePlayers>[0]) {
  let controller!: OnlinePlayersController
  const wrapper = mount(defineComponent({
    setup() {
      controller = useOnlinePlayers(options)
      return () => null
    },
  }))
  return { controller, wrapper }
}

describe('useOnlinePlayers', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('loads immediately on mount and publishes the latest successful snapshot', async () => {
    const first = deferred<OnlinePlayersSnapshot>()
    const fetchPlayers = vi.fn(() => first.promise)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
      visibility: createVisibility().source,
    })

    expect(fetchPlayers).toHaveBeenCalledOnce()
    expect(controller.state.value).toBe('loading')
    expect(controller.isRefreshing.value).toBe(true)
    first.resolve(snapshot('2026-07-22T08:00:00Z'))
    await flushPromises()

    expect(controller.state.value).toBe('fresh')
    expect(controller.snapshot.value?.capturedAtUtc).toBe('2026-07-22T08:00:00Z')
    expect(controller.errorCode.value).toBeNull()
    expect(controller.isRefreshing.value).toBe(false)
    wrapper.unmount()
  })

  it('refreshes every ten seconds without replacing old data while pending', async () => {
    const second = deferred<OnlinePlayersSnapshot>()
    const fetchPlayers = vi.fn()
      .mockResolvedValueOnce(snapshot('2026-07-22T08:00:00Z'))
      .mockImplementationOnce(() => second.promise)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
      visibility: createVisibility().source,
    })
    await flushPromises()

    await vi.advanceTimersByTimeAsync(10_000)
    expect(fetchPlayers).toHaveBeenCalledTimes(2)
    expect(controller.snapshot.value?.capturedAtUtc).toBe('2026-07-22T08:00:00Z')
    expect(controller.isRefreshing.value).toBe(true)

    second.resolve(snapshot('2026-07-22T08:00:10Z'))
    await flushPromises()
    expect(controller.snapshot.value?.capturedAtUtc).toBe('2026-07-22T08:00:10Z')
    wrapper.unmount()
  })

  it('uses one in-flight request for automatic and manual refreshes', () => {
    const pending = deferred<OnlinePlayersSnapshot>()
    const fetchPlayers = vi.fn(() => pending.promise)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
      visibility: createVisibility().source,
    })

    const first = controller.refresh()
    vi.advanceTimersByTime(10_000)
    const second = controller.refresh()

    expect(first).toBe(second)
    expect(fetchPlayers).toHaveBeenCalledOnce()
    pending.resolve(snapshot('2026-07-22T08:00:00Z'))
    wrapper.unmount()
  })

  it.each([
    [new HttpError('http', 'busy', { status: 503, problemCode: 'online_player_query_busy' }), 'busy'],
    [new HttpError('http', 'not ready', { status: 503, problemCode: 'game_not_ready' }), 'game-not-ready'],
    [new HttpError('http', 'timed out', { status: 503, problemCode: 'game_thread_timeout' }), 'timeout'],
    [new HttpError('http', 'unavailable', { status: 503, problemCode: 'online_player_snapshot_unavailable' }), 'unavailable'],
    [new HttpError('http', 'server error', { status: 500 }), 'unavailable'],
    [new HttpError('timeout', 'timed out'), 'timeout'],
    [new HttpError('network', 'offline'), 'network'],
  ] as const)('maps a first-load failure to offline with %s', async (error, expectedCode) => {
    const fetchPlayers = vi.fn().mockRejectedValue(error)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
      visibility: createVisibility().source,
    })
    await flushPromises()

    expect(controller.state.value).toBe('offline')
    expect(controller.errorCode.value).toBe(expectedCode)
    expect(controller.snapshot.value).toBeNull()
    wrapper.unmount()
  })

  it('keeps the previous snapshot and marks a refresh failure stale', async () => {
    const fetchPlayers = vi.fn()
      .mockResolvedValueOnce(snapshot('2026-07-22T08:00:00Z'))
      .mockRejectedValueOnce(new HttpError('http', 'busy', { status: 503, problemCode: 'online_player_query_busy' }))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
      visibility: createVisibility().source,
    })
    await flushPromises()
    await controller.refresh()

    expect(controller.state.value).toBe('stale')
    expect(controller.errorCode.value).toBe('busy')
    expect(controller.snapshot.value?.capturedAtUtc).toBe('2026-07-22T08:00:00Z')
    wrapper.unmount()
  })

  it('enters forbidden and clears data without expiring the auth session on 403', async () => {
    const expireSession = vi.fn()
    const fetchPlayers = vi.fn()
      .mockResolvedValueOnce(snapshot('2026-07-22T08:00:00Z'))
      .mockRejectedValueOnce(new HttpError('http', 'forbidden', { status: 403 }))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession },
      fetchPlayers,
      visibility: createVisibility().source,
    })
    await flushPromises()
    await controller.refresh()

    expect(controller.state.value).toBe('forbidden')
    expect(controller.snapshot.value).toBeNull()
    expect(controller.errorCode.value).toBeNull()
    expect(expireSession).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('does not automatically retry after a forbidden response but allows manual refresh', async () => {
    const fetchPlayers = vi.fn()
      .mockRejectedValueOnce(new HttpError('http', 'forbidden', { status: 403 }))
      .mockResolvedValue(snapshot('2026-07-22T08:00:10Z'))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
      visibility: createVisibility().source,
    })
    await flushPromises()

    await vi.advanceTimersByTimeAsync(30_000)
    expect(fetchPlayers).toHaveBeenCalledOnce()

    await controller.refresh()
    expect(fetchPlayers).toHaveBeenCalledTimes(2)
    expect(controller.state.value).toBe('fresh')
    wrapper.unmount()
  })

  it('notifies when a protected refresh finds the local session expired', async () => {
    const auth = { authorizationHeader: 'Bearer token' as string | null, expireSession: vi.fn() }
    const onSessionExpired = vi.fn()
    const fetchPlayers = vi.fn().mockResolvedValue(snapshot('2026-07-22T08:00:00Z'))
    const { controller, wrapper } = mountComposable({
      auth,
      fetchPlayers,
      visibility: createVisibility().source,
      onSessionExpired,
    })
    await flushPromises()
    auth.authorizationHeader = null

    await controller.refresh()

    expect(onSessionExpired).toHaveBeenCalledOnce()
    expect(controller.state.value).toBe('stale')
    expect(controller.snapshot.value?.capturedAtUtc).toBe('2026-07-22T08:00:00Z')
    wrapper.unmount()
  })

  it('expires the session and notifies once on 401 without replaying', async () => {
    const expireSession = vi.fn()
    const onSessionExpired = vi.fn()
    const fetchPlayers = vi.fn().mockRejectedValue(new HttpError('http', 'unauthorized', { status: 401 }))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession },
      fetchPlayers,
      visibility: createVisibility().source,
      onSessionExpired,
    })
    await flushPromises()

    expect(expireSession).toHaveBeenCalledOnce()
    expect(onSessionExpired).toHaveBeenCalledOnce()
    expect(fetchPlayers).toHaveBeenCalledOnce()
    expect(controller.isRefreshing.value).toBe(false)
    wrapper.unmount()
  })

  it('does not notify again when refreshed after a 401 cleared the local session', async () => {
    const auth = { authorizationHeader: 'Bearer token' as string | null, expireSession: vi.fn() }
    auth.expireSession.mockImplementation(() => {
      auth.authorizationHeader = null
    })
    const onSessionExpired = vi.fn()
    const fetchPlayers = vi.fn().mockRejectedValue(new HttpError('http', 'unauthorized', { status: 401 }))
    const { controller, wrapper } = mountComposable({
      auth,
      fetchPlayers,
      visibility: createVisibility().source,
      onSessionExpired,
    })
    await flushPromises()

    await controller.refresh()

    expect(onSessionExpired).toHaveBeenCalledOnce()
    expect(fetchPlayers).toHaveBeenCalledOnce()
    wrapper.unmount()
  })

  it('does not request while hidden and refreshes immediately when visibility returns with a reset period', async () => {
    const visibility = createVisibility(false)
    const fetchPlayers = vi.fn().mockResolvedValue(snapshot('2026-07-22T08:00:00Z'))
    const { wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
      visibility: visibility.source,
    })

    await vi.advanceTimersByTimeAsync(30_000)
    expect(fetchPlayers).not.toHaveBeenCalled()

    visibility.setVisible(true)
    expect(fetchPlayers).toHaveBeenCalledOnce()
    await flushPromises()
    await vi.advanceTimersByTimeAsync(9_999)
    expect(fetchPlayers).toHaveBeenCalledOnce()
    await vi.advanceTimersByTimeAsync(1)
    expect(fetchPlayers).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })

  it('uses document visibility by default and removes its listener on unmount', () => {
    const addListener = vi.spyOn(document, 'addEventListener')
    const removeListener = vi.spyOn(document, 'removeEventListener')
    const fetchPlayers = vi.fn(() => deferred<OnlinePlayersSnapshot>().promise)
    const { wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
    })

    const visibilityRegistration = addListener.mock.calls.find(([type]) => type === 'visibilitychange')
    expect(visibilityRegistration).toBeDefined()
    wrapper.unmount()
    expect(removeListener).toHaveBeenCalledWith('visibilitychange', visibilityRegistration![1])
  })

  it('ignores aborted failures without changing existing state or data', async () => {
    const fetchPlayers = vi.fn()
      .mockResolvedValueOnce(snapshot('2026-07-22T08:00:00Z'))
      .mockRejectedValueOnce(new HttpError('aborted', 'cancelled'))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
      visibility: createVisibility().source,
    })
    await flushPromises()
    await controller.refresh()

    expect(controller.state.value).toBe('fresh')
    expect(controller.snapshot.value?.capturedAtUtc).toBe('2026-07-22T08:00:00Z')
    expect(controller.errorCode.value).toBeNull()
    wrapper.unmount()
  })

  it('disposes idempotently, aborts work, suppresses late results, and releases timers and visibility', async () => {
    const pending = deferred<OnlinePlayersSnapshot>()
    let requestSignal: AbortSignal | undefined
    const fetchPlayers = vi.fn((_header: string, signal?: AbortSignal) => {
      requestSignal = signal
      return pending.promise
    })
    const visibility = createVisibility()
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
      visibility: visibility.source,
    })

    controller.dispose()
    controller.dispose()
    expect(requestSignal?.aborted).toBe(true)
    expect(visibility.unsubscribe).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)

    pending.resolve(snapshot('2026-07-22T08:00:00Z'))
    await flushPromises()
    expect(controller.snapshot.value).toBeNull()
    expect(controller.state.value).toBe('loading')
    wrapper.unmount()
    expect(visibility.unsubscribe).toHaveBeenCalledOnce()
  })
})
