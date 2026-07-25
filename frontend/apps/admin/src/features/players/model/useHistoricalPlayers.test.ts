import type { HistoricalPlayersPage, HistoricalPlayerSummary } from '../api/historyPlayers'

import type { HistoricalPlayersController } from './useHistoricalPlayers'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { defineComponent } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useHistoricalPlayers } from './useHistoricalPlayers'

interface Deferred<T> {
  promise: Promise<T>
  resolve: (value: T) => void
  reject: (reason?: unknown) => void
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((nextResolve, nextReject) => {
    resolve = nextResolve
    reject = nextReject
  })
  return { promise, resolve, reject }
}

function player(crossplatformId = 'EOS_ada', latestName = 'Ada'): HistoricalPlayerSummary {
  return {
    crossplatformId,
    latestName,
    firstObservedAtUtc: '2026-07-22T08:00:00Z',
    lastObservedAtUtc: '2026-07-22T08:30:00Z',
    totalObservationCount: 8,
    retainedSnapshotCount: 5,
    compactedSnapshotCount: 3,
    hasGaps: false,
  }
}

function page(players: HistoricalPlayerSummary[], nextCursor: string | null = null): HistoricalPlayersPage {
  return { players, nextCursor }
}

function mountComposable(options: Parameters<typeof useHistoricalPlayers>[0]) {
  let controller!: HistoricalPlayersController
  const wrapper = mount(defineComponent({
    setup() {
      controller = useHistoricalPlayers(options)
      return () => null
    },
  }))
  return { controller, wrapper }
}

describe('useHistoricalPlayers', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('loads once on mount without setting up polling', async () => {
    const fetchPlayers = vi.fn().mockResolvedValue(page([player()]))
    const setInterval = vi.spyOn(globalThis, 'setInterval')
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
    })

    await flushPromises()

    expect(fetchPlayers).toHaveBeenCalledOnce()
    expect(controller.state.value).toBe('ready')
    expect(controller.players.value).toEqual([player()])
    expect(setInterval).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('cancels and clears the old page when search changes so late data cannot replace it', async () => {
    const first = deferred<HistoricalPlayersPage>()
    const second = deferred<HistoricalPlayersPage>()
    let firstSignal: AbortSignal | undefined
    const fetchPlayers = vi.fn()
      .mockImplementationOnce((_header: string, _query: unknown, signal?: AbortSignal) => {
        firstSignal = signal
        return first.promise
      })
      .mockImplementationOnce(() => second.promise)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
    })

    controller.search.value = 'Ada'
    await flushPromises()
    expect(firstSignal?.aborted).toBe(true)
    expect(controller.players.value).toEqual([])

    second.resolve(page([player('EOS_ada', 'Ada')]))
    first.resolve(page([player('EOS_old', 'Old')]))
    await flushPromises()

    expect(controller.players.value).toEqual([player('EOS_ada', 'Ada')])
    wrapper.unmount()
  })

  it('keeps successful data and marks a refresh failure stale', async () => {
    const fetchPlayers = vi.fn()
      .mockResolvedValueOnce(page([player()]))
      .mockRejectedValueOnce(new HttpError('network', 'offline'))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
    })
    await flushPromises()

    await controller.refresh()

    expect(controller.state.value).toBe('stale')
    expect(controller.players.value).toEqual([player()])
    wrapper.unmount()
  })

  it('preserves loaded players when loading more fails', async () => {
    const fetchPlayers = vi.fn()
      .mockResolvedValueOnce(page([player()], 'cursor-2'))
      .mockRejectedValueOnce(new HttpError('network', 'offline'))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
    })
    await flushPromises()

    await controller.loadMore()

    expect(controller.state.value).toBe('stale')
    expect(controller.players.value).toEqual([player()])
    expect(controller.nextCursor.value).toBe('cursor-2')
    wrapper.unmount()
  })

  it('deduplicates a server page by cross-platform identity', async () => {
    const fetchPlayers = vi.fn().mockResolvedValue(page([
      player('EOS_ada', 'Ada'),
      player('EOS_ada', 'Changed'),
      player('EOS_bob', 'Bob'),
    ]))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayers,
    })
    await flushPromises()

    expect(controller.players.value).toEqual([player('EOS_ada', 'Ada'), player('EOS_bob', 'Bob')])
    wrapper.unmount()
  })

  it('clears the refresh indicator when a new search cancels work after session expiry', async () => {
    const pending = deferred<HistoricalPlayersPage>()
    const auth = { authorizationHeader: 'Bearer token' as string | null, expireSession: vi.fn() }
    const { controller, wrapper } = mountComposable({ auth, fetchPlayers: vi.fn(() => pending.promise) })

    auth.authorizationHeader = null
    controller.search.value = 'Ada'
    await flushPromises()

    expect(controller.isRefreshing.value).toBe(false)
    wrapper.unmount()
  })
})
