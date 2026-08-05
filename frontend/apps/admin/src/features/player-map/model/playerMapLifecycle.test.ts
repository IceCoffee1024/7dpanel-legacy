import type { PlayerMapRequestLane } from './playerMapLifecycle'

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createPlayerMapLifecycle } from './playerMapLifecycle'

describe('player map lifecycle', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('owns four request lanes, rejects stale responses, and keeps one game-time timer', () => {
    let visible = true
    let visibilityListener!: () => void
    const onVisible = vi.fn()
    const onInterval = vi.fn()
    const lifecycle = createPlayerMapLifecycle({
      visibility: {
        isVisible: () => visible,
        subscribe(listener) {
          visibilityListener = listener
          return () => {}
        },
      },
      onVisible,
      onInterval,
    })

    lifecycle.start()
    expect(onVisible).toHaveBeenCalledOnce()
    const lanes: readonly PlayerMapRequestLane[] = ['core', 'time', 'history', 'track']
    const requests = lanes.map(lane => lifecycle.begin(lane))
    const replacement = lifecycle.begin('track')

    expect(requests[3]?.controller.signal.aborted).toBe(true)
    expect(lifecycle.isCurrent(requests[0]!)).toBe(true)
    expect(lifecycle.isCurrent(requests[3]!)).toBe(false)
    expect(lifecycle.isCurrent(replacement)).toBe(true)

    vi.advanceTimersByTime(30_000)
    expect(onInterval).toHaveBeenCalledOnce()
    expect(onVisible).toHaveBeenCalledOnce()

    visible = false
    visibilityListener()
    expect(requests.every(request => request.controller.signal.aborted)).toBe(true)
    expect(replacement.controller.signal.aborted).toBe(true)
    lifecycle.dispose()
    expect(vi.getTimerCount()).toBe(0)
    expect(lifecycle.isDisposed()).toBe(true)
  })
})
