import type {
  ServerEventNotification,
  ServerEventsConnectionStatus,
} from '../../../app/serverEvents'
import type { ConsoleLogEntry } from './consoleLog'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { useConsoleLogs } from './useConsoleLogs'

function entry(sequence: number, message = `message-${sequence}`): ConsoleLogEntry {
  return {
    sequence,
    formattedMessage: null,
    message,
    trace: null,
    logType: 'log',
    timestamp: '2026-07-26T08:00:00Z',
    uptimeMilliseconds: sequence,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, reject, resolve }
}

function mountConsoleLogs(
  loadRecent: (limit: number, signal?: AbortSignal) => Promise<readonly ConsoleLogEntry[]>,
  order?: string[],
) {
  let eventListener!: (event: ServerEventNotification) => void
  let statusListener!: (status: ServerEventsConnectionStatus) => void
  const unsubscribeEvents = vi.fn()
  const unsubscribeStatus = vi.fn()
  const subscribeEvents = vi.fn((listener: typeof eventListener) => {
    order?.push('subscribe')
    eventListener = listener
    return unsubscribeEvents
  })
  const subscribeStatus = vi.fn((listener: typeof statusListener) => {
    statusListener = listener
    listener('reconnecting')
    return unsubscribeStatus
  })
  let logs!: ReturnType<typeof useConsoleLogs>
  const Host = defineComponent({
    setup() {
      logs = useConsoleLogs({ loadRecent, subscribeEvents, subscribeStatus })
      return () => null
    },
  })
  const wrapper = mount(Host)
  return {
    emit: (event: ServerEventNotification) => eventListener(event),
    logs: () => logs,
    setStatus: (status: ServerEventsConnectionStatus) => statusListener(status),
    subscribeEvents,
    unsubscribeEvents,
    unsubscribeStatus,
    wrapper,
  }
}

describe('useConsoleLogs', () => {
  it('subscribes before loading and keeps the first valid item for duplicate sequences', async () => {
    const snapshot = deferred<readonly ConsoleLogEntry[]>()
    const order: string[] = []
    const loadRecent = vi.fn(() => {
      order.push('load')
      return snapshot.promise
    })
    const mounted = mountConsoleLogs(loadRecent, order)

    mounted.emit({ type: 'console-log', id: '2', data: entry(2, 'live-first') })
    snapshot.resolve([entry(3), entry(2, 'snapshot-late'), entry(1)])
    await flushPromises()

    expect(order).toEqual(['subscribe', 'load'])
    expect(loadRecent).toHaveBeenCalledWith(1000, expect.any(AbortSignal))
    expect(mounted.logs().entries.value.map(item => [item.sequence, item.message])).toEqual([
      [1, 'message-1'],
      [2, 'live-first'],
      [3, 'message-3'],
    ])
    expect(mounted.logs().snapshotLoading.value).toBe(false)
  })

  it('ignores invalid live payloads, tracks gap and connection status independently', async () => {
    const mounted = mountConsoleLogs(vi.fn().mockResolvedValue([]))
    await flushPromises()

    mounted.emit({ type: 'console-log', data: { ...entry(1), timestamp: 'invalid' } })
    mounted.emit({ type: 'gap', data: { afterSequence: 0 } })
    mounted.setStatus('live')

    expect(mounted.logs().entries.value).toEqual([])
    expect(mounted.logs().hasGap.value).toBe(true)
    expect(mounted.logs().connectionStatus.value).toBe('live')
  })

  it('retains buffered live entries when the recent snapshot fails', async () => {
    const pending = deferred<readonly ConsoleLogEntry[]>()
    const mounted = mountConsoleLogs(vi.fn().mockReturnValue(pending.promise))
    mounted.emit({ type: 'console-log', data: entry(4) })
    pending.reject(new Error('unavailable'))
    await flushPromises()

    expect(mounted.logs().snapshotLoading.value).toBe(false)
    expect(mounted.logs().entries.value).toEqual([entry(4)])
  })

  it('keeps the newest 2000 entries by numeric sequence', async () => {
    const mounted = mountConsoleLogs(vi.fn().mockResolvedValue(
      Array.from({ length: 2001 }, (_, index) => entry(index + 1)),
    ))
    await flushPromises()

    expect(mounted.logs().entries.value).toHaveLength(2000)
    expect(mounted.logs().entries.value[0]?.sequence).toBe(2)
    expect(mounted.logs().entries.value[mounted.logs().entries.value.length - 1]?.sequence).toBe(2001)
  })

  it('counts unread live entries only while not following and clears page state without touching SSE', async () => {
    const mounted = mountConsoleLogs(vi.fn().mockResolvedValue([entry(1)]))
    await flushPromises()

    mounted.logs().setFollowingLatest(false)
    mounted.emit({ type: 'console-log', data: entry(2) })
    mounted.emit({ type: 'console-log', data: entry(2) })
    expect(mounted.logs().unreadCount.value).toBe(1)

    mounted.logs().clearEntries()
    expect(mounted.logs().entries.value).toEqual([])
    expect(mounted.logs().unreadCount.value).toBe(0)
    expect(mounted.unsubscribeEvents).not.toHaveBeenCalled()

    mounted.emit({ type: 'console-log', data: entry(3) })
    expect(mounted.logs().entries.value).toEqual([entry(3)])
  })

  it('does not clear while the snapshot is loading and releases subscriptions and request on unmount', () => {
    const pending = deferred<readonly ConsoleLogEntry[]>()
    let requestSignal: AbortSignal | undefined
    const mounted = mountConsoleLogs(vi.fn((_limit, signal) => {
      requestSignal = signal
      return pending.promise
    }))
    mounted.emit({ type: 'console-log', data: entry(1) })

    mounted.logs().clearEntries()
    expect(mounted.logs().entries.value).toEqual([entry(1)])

    mounted.wrapper.unmount()
    expect(requestSignal?.aborted).toBe(true)
    expect(mounted.unsubscribeEvents).toHaveBeenCalledOnce()
    expect(mounted.unsubscribeStatus).toHaveBeenCalledOnce()
  })
})
