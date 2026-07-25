import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { usePageVisibilityRefresh } from './usePageVisibilityRefresh'

describe('usePageVisibilityRefresh', () => {
  let visibility: DocumentVisibilityState

  beforeEach(() => {
    vi.useFakeTimers()
    visibility = 'visible'
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility)
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  function mountScheduler(refresh = vi.fn().mockResolvedValue(undefined)) {
    let scheduler!: ReturnType<typeof usePageVisibilityRefresh>
    const Host = defineComponent({
      setup() {
        scheduler = usePageVisibilityRefresh(refresh)
        return () => null
      },
    })
    return { refresh, scheduler: () => scheduler, wrapper: mount(Host) }
  }

  it('uses one visibility listener and a 30-second interval', async () => {
    const add = vi.spyOn(document, 'addEventListener')
    const { refresh, wrapper } = mountScheduler()

    expect(add.mock.calls.filter(([type]) => type === 'visibilitychange')).toHaveLength(1)
    await vi.advanceTimersByTimeAsync(29_999)
    expect(refresh).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(refresh).toHaveBeenCalledOnce()

    wrapper.unmount()
  })

  it('pauses while hidden then refreshes immediately and resumes when visible', async () => {
    const { refresh, wrapper } = mountScheduler()

    visibility = 'hidden'
    document.dispatchEvent(new Event('visibilitychange'))
    await vi.advanceTimersByTimeAsync(60_000)
    expect(refresh).not.toHaveBeenCalled()

    visibility = 'visible'
    document.dispatchEvent(new Event('visibilitychange'))
    await flushPromises()
    expect(refresh).toHaveBeenCalledOnce()

    await vi.advanceTimersByTimeAsync(30_000)
    expect(refresh).toHaveBeenCalledTimes(2)

    wrapper.unmount()
  })

  it('resets the period after manual refresh to avoid an adjacent duplicate', async () => {
    const { refresh, scheduler, wrapper } = mountScheduler()

    await vi.advanceTimersByTimeAsync(29_999)
    scheduler().resetPeriod()
    await vi.advanceTimersByTimeAsync(1)
    expect(refresh).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(29_999)
    expect(refresh).toHaveBeenCalledOnce()

    wrapper.unmount()
  })

  it('removes its listener and timer on unmount', () => {
    const remove = vi.spyOn(document, 'removeEventListener')
    const { wrapper } = mountScheduler()

    wrapper.unmount()

    expect(remove.mock.calls.filter(([type]) => type === 'visibilitychange')).toHaveLength(1)
    expect(vi.getTimerCount()).toBe(0)
  })
})
