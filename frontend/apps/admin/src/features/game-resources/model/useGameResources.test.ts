import type { App } from 'vue'

import type { GameResourcePage, LoadGameResources } from '../api/gameResources'
import { flushPromises } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createApp, nextTick, shallowRef } from 'vue'
import { GameResourcesRequestError } from '../api/gameResources'
import { useGameResources } from './useGameResources'

function page(overrides: Partial<GameResourcePage> = {}): GameResourcePage {
  return Object.freeze({
    catalogVersion: 'catalog-1',
    gameVersion: 'v3.0.1-b4',
    observedAtUtc: '2026-07-26T08:00:00Z',
    total: 1,
    page: 1,
    pageSize: 50,
    warnings: Object.freeze([]),
    items: Object.freeze([Object.freeze({
      resourceId: 'resource-1',
      numericId: 1,
      internalName: 'resourceStone',
      localizedName: 'Stone',
      kind: 'item' as const,
      visibility: 'public' as const,
      maxStack: 6000,
      hasQuality: false,
      iconStatus: 'available' as const,
      iconTintHex: null,
    })]),
    ...overrides,
  })
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, reject, resolve }
}

function mountComposable(load: LoadGameResources, overrides: Record<string, unknown> = {}) {
  let result!: ReturnType<typeof useGameResources>
  const locale = shallowRef('zh-CN')
  const isOwner = shallowRef(true)
  const replaceQuery = vi.fn()
  const app = createApp({
    setup() {
      result = useGameResources({
        load,
        locale,
        isOwner,
        replaceQuery,
        ...overrides,
      })
      return () => null
    },
  })
  app.mount(document.createElement('div'))
  return { app, isOwner, locale, replaceQuery, result }
}

describe('useGameResources', () => {
  let apps: App[]

  beforeEach(() => {
    apps = []
  })

  afterEach(() => {
    for (const app of apps)
      app.unmount()
    vi.useRealTimers()
  })

  it('loads defaults on mount and derives success, empty, and partial states', async () => {
    const load = vi.fn<LoadGameResources>()
      .mockResolvedValueOnce(page())
      .mockResolvedValueOnce(page({ total: 0, items: Object.freeze([]) }))
      .mockResolvedValueOnce(page({ warnings: Object.freeze(['game-resource-localization-partial']) }))
    const mounted = mountComposable(load)
    apps.push(mounted.app)

    await flushPromises()
    expect(load).toHaveBeenNthCalledWith(1, {
      includeHidden: false,
      language: 'zh-CN',
      page: 1,
      pageSize: 50,
    }, expect.any(AbortSignal))
    expect(mounted.result.state.value).toBe('success')

    await mounted.result.refresh()
    expect(mounted.result.state.value).toBe('empty')

    await mounted.result.refresh()
    expect(mounted.result.state.value).toBe('partial')
  })

  it('debounces search for 250ms while applying other filters immediately', async () => {
    vi.useFakeTimers()
    const load = vi.fn<LoadGameResources>().mockResolvedValue(page())
    const mounted = mountComposable(load)
    apps.push(mounted.app)
    await flushPromises()

    mounted.result.setSearch('steel')
    await vi.advanceTimersByTimeAsync(249)
    expect(load).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(load).toHaveBeenCalledTimes(2)
    expect(mounted.replaceQuery).toHaveBeenLastCalledWith({ search: 'steel' })

    mounted.result.setKind('block')
    await flushPromises()
    expect(load).toHaveBeenCalledTimes(3)
    expect(load.mock.calls[2]?.[0]).toMatchObject({ search: 'steel', kind: 'block', page: 1 })
  })

  it('removes hidden filtering immediately when the role stops being Owner', async () => {
    const load = vi.fn<LoadGameResources>().mockResolvedValue(page())
    const mounted = mountComposable(load, {
      initialQuery: { includeHidden: 'true' },
    })
    apps.push(mounted.app)
    await flushPromises()
    expect(mounted.result.filters.value.includeHidden).toBe(true)

    mounted.isOwner.value = false
    await nextTick()
    await flushPromises()

    expect(mounted.result.filters.value.includeHidden).toBe(false)
    expect(mounted.replaceQuery).toHaveBeenLastCalledWith({})
    expect(load.mock.calls.slice(-1)[0]?.[0].includeHidden).toBe(false)
  })

  it('aborts an old query, isolates its late result, and single-flights equal refreshes', async () => {
    const first = deferred<GameResourcePage>()
    const second = deferred<GameResourcePage>()
    const load = vi.fn<LoadGameResources>()
      .mockImplementationOnce((_query, signal) => {
        signal.addEventListener('abort', () => {})
        return first.promise
      })
      .mockImplementationOnce(() => second.promise)
    const mounted = mountComposable(load)
    apps.push(mounted.app)
    await nextTick()
    const firstSignal = load.mock.calls[0]?.[1]

    mounted.result.setKind('item')
    await nextTick()
    expect(firstSignal?.aborted).toBe(true)
    expect(load).toHaveBeenCalledTimes(2)

    const sameRequest = mounted.result.refresh()
    expect(load).toHaveBeenCalledTimes(2)
    second.resolve(page({ catalogVersion: 'catalog-2' }))
    await sameRequest
    first.resolve(page({ catalogVersion: 'late-catalog' }))
    await flushPromises()

    expect(mounted.result.page.value?.catalogVersion).toBe('catalog-2')
  })

  it('keeps the last successful page stale after refresh failure', async () => {
    const load = vi.fn<LoadGameResources>()
      .mockResolvedValueOnce(page())
      .mockRejectedValueOnce(new GameResourcesRequestError('offline', { code: 'network' }))
    const mounted = mountComposable(load)
    apps.push(mounted.app)
    await flushPromises()

    await mounted.result.refresh()

    expect(mounted.result.state.value).toBe('stale')
    expect(mounted.result.page.value?.observedAtUtc).toBe('2026-07-26T08:00:00Z')
  })

  it('retries Building from bounded Retry-After but leaves Unavailable for manual retry', async () => {
    vi.useFakeTimers()
    const building = new GameResourcesRequestError('building', {
      status: 503,
      problemCode: 'game-resource-catalog-building',
      retryAfterSeconds: 2,
    })
    const unavailable = new GameResourcesRequestError('unavailable', {
      status: 503,
      problemCode: 'game-resource-catalog-unavailable',
    })
    const load = vi.fn<LoadGameResources>()
      .mockRejectedValueOnce(building)
      .mockResolvedValueOnce(page())
      .mockRejectedValueOnce(unavailable)
    const mounted = mountComposable(load)
    apps.push(mounted.app)
    await flushPromises()

    expect(mounted.result.state.value).toBe('building')
    await vi.advanceTimersByTimeAsync(1_999)
    expect(load).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(load).toHaveBeenCalledTimes(2)
    expect(mounted.result.state.value).toBe('success')

    await mounted.result.refresh()
    expect(mounted.result.state.value).toBe('stale')
    await vi.advanceTimersByTimeAsync(30_000)
    expect(load).toHaveBeenCalledTimes(3)
  })

  it('shows Forbidden without retaining rows and releases requests and retry timers on unmount', async () => {
    vi.useFakeTimers()
    const pending = deferred<GameResourcePage>()
    const load = vi.fn<LoadGameResources>()
      .mockRejectedValueOnce(new GameResourcesRequestError('forbidden', { status: 403 }))
      .mockImplementationOnce(() => pending.promise)
    const mounted = mountComposable(load)
    apps.push(mounted.app)
    await flushPromises()

    expect(mounted.result.state.value).toBe('forbidden')
    expect(mounted.result.page.value).toBeNull()

    const refresh = mounted.result.retry()
    await nextTick()
    const signal = load.mock.calls[1]?.[1]
    mounted.app.unmount()
    apps = []

    expect(signal?.aborted).toBe(true)
    pending.resolve(page())
    await refresh
    expect(mounted.result.page.value).toBeNull()
  })
})
