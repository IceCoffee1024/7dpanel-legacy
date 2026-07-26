import type { OverviewSnapshot } from './overview'
import type { ServerEventType } from '../../../app/serverEvents'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { defineComponent } from 'vue'

import { configureGeneratedClient } from '../../../shared/api/generatedClient'
import { HttpError } from '../../../shared/api/http'
import { useOverview } from './useOverview'

function snapshot(
  availability: OverviewSnapshot['availability'] = 'available',
  gameAvailability: OverviewSnapshot['game']['availability'] = 'available',
): OverviewSnapshot {
  return {
    availability,
    game: {
      availability: gameAvailability,
      sampledAtUtc: '2026-07-25T01:02:03Z',
      gameTitle: null,
      saveGameName: null,
      worldName: null,
      worldSessionUptimeSeconds: null,
      version: null,
      gameMode: null,
      difficulty: null,
      region: null,
      language: null,
      connectionAddress: null,
      connectionPort: null,
      onlinePlayerCount: null,
      maximumPlayerCount: null,
      historicalPlayerCount: null,
      framesPerSecond: null,
      gameTime: null,
    },
    host: {
      availability: 'available',
      identityAvailability: 'forbidden',
      sampledAtUtc: '2026-07-25T01:02:03Z',
      processUptimeSeconds: null,
      residentSetBytes: null,
      managedHeapBytes: null,
      otherMemoryBytes: null,
      cpuUsagePercent: null,
      operatingSystem: null,
      operatingSystemVersion: null,
      processorCount: null,
      memoryTotalBytes: null,
      memoryAvailableBytes: null,
      storageVolumes: [],
      publicNetwork: { availability: 'forbidden' },
      osFamily: null,
      operatingSystemArchitecture: null,
      runtimeVersion: null,
      cpuModel: null,
      logicalCoreCount: null,
      cpuFrequencyMhz: null,
      deviceName: null,
      deviceModel: null,
      deviceType: null,
      processId: null,
      processStartedAtUtc: null,
    },
    restartPolicy: {
      availability: 'available',
      isConfigured: false,
      scheduleDescription: null,
      nextRestartAtUtc: null,
    },
    recentActivity: {
      availability: 'available',
      sampledAtUtc: '2026-07-25T01:02:03Z',
      totalCount: 0,
      latestOccurredAtUtc: null,
      items: [],
    },
    attention: [],
  }
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

describe('useOverview', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  function mountOverview(
    fetch = vi.fn().mockResolvedValue(snapshot()),
    subscribeServerEvents: (
      listener: (event: { type: ServerEventType }) => void,
    ) => () => void = vi.fn(() => () => {}),
  ) {
    const auth = {
      authorizationHeader: 'Bearer owner' as string | null,
      expireSession: vi.fn(),
    }
    const onSessionExpired = vi.fn()
    let overview!: ReturnType<typeof useOverview>
    const Host = defineComponent({
      setup() {
        overview = useOverview({
          auth,
          fetchOverview: fetch,
          onSessionExpired,
          subscribeServerEvents,
        })
        return () => null
      },
    })
    return {
      auth,
      fetch,
      onSessionExpired,
      overview: () => overview,
      subscribeServerEvents,
      wrapper: mount(Host),
    }
  }

  it('starts loading, performs the first authenticated load, and becomes fresh', async () => {
    const pending = deferred<OverviewSnapshot>()
    const mounted = mountOverview(vi.fn().mockReturnValue(pending.promise))

    expect(mounted.overview().status.value).toBe('loading')
    expect(mounted.overview().snapshot.value).toBeNull()
    expect(mounted.fetch).toHaveBeenCalledOnce()
    expect(mounted.fetch.mock.calls[0]?.[0]).toBe('Bearer owner')
    expect(mounted.fetch.mock.calls[0]?.[1]).toBeInstanceOf(AbortSignal)

    const next = snapshot()
    pending.resolve(next)
    await flushPromises()

    expect(mounted.overview().snapshot.value).toEqual(next)
    expect(mounted.overview().status.value).toBe('fresh')
    expect(mounted.overview().error.value).toBeNull()
    mounted.wrapper.unmount()
  })

  it('uses the generated Colada query for the production request path', async () => {
    const next = snapshot()
    const fetchMock = vi.fn().mockResolvedValue(Response.json(next)) as typeof fetch
    const auth = {
      authorizationHeader: 'Bearer generated-owner' as string | null,
      expireSession: vi.fn(),
    }
    configureGeneratedClient({
      fetch: fetchMock,
      getAuthorizationHeader: () => auth.authorizationHeader,
      origin: location.origin,
    })
    let overview!: ReturnType<typeof useOverview>
    const Host = defineComponent({
      setup() {
        overview = useOverview({ auth })
        return () => null
      },
    })
    const wrapper = mount(Host, {
      global: { plugins: [createPinia(), PiniaColada] },
    })

    await flushPromises()

    expect(overview.snapshot.value).toEqual(next)
    expect(overview.status.value).toBe('fresh')
    const request = vi.mocked(fetchMock).mock.calls[0]?.[0] as Request
    expect(request.headers.get('Authorization')).toBe('Bearer generated-owner')
    wrapper.unmount()
  })

  it('refreshes every 3 seconds without overlapping requests', async () => {
    const mounted = mountOverview()
    await flushPromises()

    await vi.advanceTimersByTimeAsync(2_999)
    expect(mounted.fetch).toHaveBeenCalledOnce()
    await vi.advanceTimersByTimeAsync(1)
    await flushPromises()
    expect(mounted.fetch).toHaveBeenCalledTimes(2)

    mounted.wrapper.unmount()
  })

  it('manual refresh aborts the previous request and resets the period', async () => {
    const first = deferred<OverviewSnapshot>()
    const second = deferred<OverviewSnapshot>()
    const fetch = vi.fn()
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)
      .mockResolvedValue(snapshot())
    const mounted = mountOverview(fetch)
    const firstSignal = fetch.mock.calls[0]?.[1] as AbortSignal

    await vi.advanceTimersByTimeAsync(2_999)
    const manual = mounted.overview().refresh()
    expect(firstSignal.aborted).toBe(true)
    second.resolve(snapshot())
    await manual
    await vi.advanceTimersByTimeAsync(1)
    expect(fetch).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(2_999)
    await flushPromises()
    expect(fetch).toHaveBeenCalledTimes(3)

    mounted.wrapper.unmount()
  })

  it('refreshes for lifecycle and gap events but not for welcome or heartbeat', async () => {
    let listener!: (event: { type: ServerEventType }) => void
    const unsubscribe = vi.fn()
    const subscribe = vi.fn((nextListener: typeof listener) => {
      listener = nextListener
      return unsubscribe
    })
    const mounted = mountOverview(vi.fn().mockResolvedValue(snapshot()), subscribe)
    await flushPromises()

    listener({ type: 'welcome' })
    listener({ type: 'heartbeat' })
    await flushPromises()
    expect(mounted.fetch).toHaveBeenCalledOnce()

    listener({ type: 'game-ready' })
    await flushPromises()
    listener({ type: 'server-stopping' })
    await flushPromises()
    listener({ type: 'gap' })
    await flushPromises()
    expect(mounted.fetch).toHaveBeenCalledTimes(4)

    mounted.wrapper.unmount()
    expect(unsubscribe).toHaveBeenCalledOnce()
  })

  it('prevents an older response from replacing a newer generation', async () => {
    const first = deferred<OverviewSnapshot>()
    const second = deferred<OverviewSnapshot>()
    const fetch = vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const mounted = mountOverview(fetch)

    const newer = snapshot('available')
    newer.game.gameTitle = 'newer'
    const older = snapshot('available')
    older.game.gameTitle = 'older'
    const refresh = mounted.overview().refresh()
    second.resolve(newer)
    await refresh
    first.resolve(older)
    await flushPromises()

    expect(mounted.overview().snapshot.value?.game.gameTitle).toBe('newer')
    mounted.wrapper.unmount()
  })

  it('maps overall unavailable to offline even when partitions remain available', async () => {
    const mounted = mountOverview(vi.fn().mockResolvedValue(snapshot('unavailable')))
    await flushPromises()

    expect(mounted.overview().status.value).toBe('offline')
    mounted.wrapper.unmount()
  })

  it.each([
    ['partial for an unavailable partition', snapshot('stale', 'unavailable'), 'partial'],
    ['partial for a forbidden partition', { ...snapshot(), restartPolicy: { ...snapshot().restartPolicy, availability: 'forbidden' as const } }, 'partial'],
    ['stale for stale availability without unavailable or forbidden data partitions', snapshot('stale', 'stale'), 'stale'],
  ])('maps %s from backend availability only', async (_name, next, expected) => {
    const mounted = mountOverview(vi.fn().mockResolvedValue(next))
    await flushPromises()

    expect(mounted.overview().status.value).toBe(expected)
    mounted.wrapper.unmount()
  })

  it('retains the last snapshot and its sampled times when a refresh fails', async () => {
    const previous = snapshot()
    const fetch = vi.fn()
      .mockResolvedValueOnce(previous)
      .mockRejectedValueOnce(new HttpError('network', 'private backend detail'))
    const mounted = mountOverview(fetch)
    await flushPromises()

    await mounted.overview().refresh()

    expect(mounted.overview().snapshot.value).toEqual(previous)
    expect(mounted.overview().snapshot.value?.game.sampledAtUtc).toBe('2026-07-25T01:02:03Z')
    expect(mounted.overview().status.value).toBe('stale')
    expect(mounted.overview().error.value).toEqual({ code: 'network' })
    expect(JSON.stringify(mounted.overview().error.value)).not.toContain('private backend detail')
    mounted.wrapper.unmount()
  })

  it('becomes offline on the first failure', async () => {
    const mounted = mountOverview(vi.fn().mockRejectedValue(new HttpError('timeout', 'timeout')))
    await flushPromises()

    expect(mounted.overview().snapshot.value).toBeNull()
    expect(mounted.overview().status.value).toBe('offline')
    expect(mounted.overview().error.value).toEqual({ code: 'timeout' })
    mounted.wrapper.unmount()
  })

  it('does not display AbortError as a failure', async () => {
    const mounted = mountOverview(vi.fn().mockRejectedValue(new HttpError('aborted', 'aborted')))
    await flushPromises()

    expect(mounted.overview().status.value).toBe('loading')
    expect(mounted.overview().error.value).toBeNull()
    mounted.wrapper.unmount()
  })

  it('uses the shared 401 session invalidation flow', async () => {
    const mounted = mountOverview(vi.fn().mockRejectedValue(new HttpError('http', 'unauthorized', { status: 401 })))
    await flushPromises()

    expect(mounted.auth.expireSession).toHaveBeenCalledOnce()
    expect(mounted.onSessionExpired).toHaveBeenCalledOnce()
    expect(mounted.overview().error.value).toBeNull()
    mounted.wrapper.unmount()
  })

  it('aborts and cleans up on unmount', () => {
    const pending = deferred<OverviewSnapshot>()
    const fetch = vi.fn().mockReturnValue(pending.promise)
    const mounted = mountOverview(fetch)
    const signal = fetch.mock.calls[0]?.[1] as AbortSignal

    mounted.wrapper.unmount()

    expect(signal.aborted).toBe(true)
    expect(vi.getTimerCount()).toBe(0)
  })
})
