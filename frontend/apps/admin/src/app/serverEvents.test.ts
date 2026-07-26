import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createServerEvents } from './serverEvents'

interface StreamOptions {
  headers?: HeadersInit
  onSseEvent?: (event: { data: unknown, event?: string, id?: string }) => void
  signal?: AbortSignal
}

async function* untilAborted(signal: AbortSignal) {
  await new Promise<void>((resolve) => {
    signal.addEventListener('abort', () => resolve(), { once: true })
  })
}

async function flushTasks() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('server events runtime', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('opens one authenticated stream and publishes supported protocol events', async () => {
    const openStream = vi.fn(async (options: StreamOptions) => ({
      stream: untilAborted(options.signal!),
    }))
    const runtime = createServerEvents({ openStream })
    const listener = vi.fn()
    runtime.subscribe(listener)

    runtime.start('Bearer owner-token')
    runtime.start('Bearer owner-token')
    await flushTasks()

    expect(openStream).toHaveBeenCalledOnce()
    const options = openStream.mock.calls[0]![0]
    expect(new Headers(options.headers).get('Accept')).toBe('text/event-stream')
    options.onSseEvent?.({ data: { product: '7DPanel' }, event: 'welcome' })
    options.onSseEvent?.({ data: { ready: true }, event: 'game-ready', id: '41' })
    options.onSseEvent?.({ data: { stopping: true }, event: 'server-stopping', id: '42' })
    options.onSseEvent?.({ data: { afterSequence: 41 }, event: 'gap' })
    options.onSseEvent?.({ data: undefined })

    expect(listener.mock.calls.map(([event]) => event.type)).toEqual([
      'welcome',
      'game-ready',
      'server-stopping',
      'gap',
      'heartbeat',
    ])

    runtime.stop({ clearCursor: true })
    expect(options.signal?.aborted).toBe(true)
  })

  it('reconnects a normally ended stream with the last processed event id', async () => {
    const openStream = vi.fn()
      .mockImplementationOnce(async (options: StreamOptions) => {
        options.onSseEvent?.({ data: {}, event: 'game-ready', id: '73' })
        return { stream: (async function* () {})() }
      })
      .mockImplementationOnce(async (options: StreamOptions) => ({
        stream: untilAborted(options.signal!),
      }))
    const runtime = createServerEvents({ openStream, reconnectDelayMs: 3_000 })

    runtime.start('Bearer owner-token')
    await flushTasks()
    await vi.advanceTimersByTimeAsync(2_999)
    expect(openStream).toHaveBeenCalledOnce()
    await vi.advanceTimersByTimeAsync(1)
    await flushTasks()

    expect(openStream).toHaveBeenCalledTimes(2)
    const replayHeaders = new Headers(openStream.mock.calls[1]![0].headers)
    expect(replayHeaders.get('Last-Event-ID')).toBe('73')
    runtime.stop({ clearCursor: true })
  })

  it('aborts the old connection and clears its cursor when the session changes', async () => {
    const openStream = vi.fn(async (options: StreamOptions) => ({
      stream: untilAborted(options.signal!),
    }))
    const runtime = createServerEvents({ openStream })

    runtime.start('Bearer first-token')
    await flushTasks()
    const firstOptions = openStream.mock.calls[0]![0]
    firstOptions.onSseEvent?.({ data: {}, event: 'game-ready', id: '88' })

    runtime.start('Bearer second-token')
    await flushTasks()

    expect(firstOptions.signal?.aborted).toBe(true)
    const replacementHeaders = new Headers(openStream.mock.calls[1]![0].headers)
    expect(replacementHeaders.get('Last-Event-ID')).toBeNull()
    runtime.stop({ clearCursor: true })
  })
})
