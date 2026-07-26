import type { ServerEventNotification, ServerEventsConnectionStatus } from '../../../app/serverEvents'
import type { ChatMessage } from './chatMessage'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { useLiveChat } from './useLiveChat'

function message(sequence: number, channel: ChatMessage['channel'] = 'Global'): ChatMessage {
  return {
    sequence,
    occurredAtUtc: '2026-07-26T08:00:00Z',
    entityId: sequence,
    crossplatformId: `EOS_${sequence}`,
    senderName: `player-${sequence}`,
    channel,
    sourceKind: 'Player',
    message: `message-${sequence}`,
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

function mountLiveChat(loadRecent: (limit: number, signal?: AbortSignal) => Promise<readonly ChatMessage[]>) {
  let eventListener!: (event: ServerEventNotification) => void
  let statusListener!: (status: ServerEventsConnectionStatus) => void
  const unsubscribeEvents = vi.fn()
  const unsubscribeStatus = vi.fn()
  const subscribeEvents = vi.fn((listener: typeof eventListener) => {
    eventListener = listener
    return unsubscribeEvents
  })
  const subscribeStatus = vi.fn((listener: typeof statusListener) => {
    statusListener = listener
    listener('reconnecting')
    return unsubscribeStatus
  })
  let chat!: ReturnType<typeof useLiveChat>
  const Host = defineComponent({
    setup() {
      chat = useLiveChat({ loadRecent, subscribeEvents, subscribeStatus })
      return () => null
    },
  })
  const wrapper = mount(Host)
  return {
    chat: () => chat,
    emit: (event: ServerEventNotification) => eventListener(event),
    setStatus: (status: ServerEventsConnectionStatus) => statusListener(status),
    unsubscribeEvents,
    unsubscribeStatus,
    wrapper,
  }
}

describe('useLiveChat', () => {
  it('subscribes before snapshot, merges by numeric sequence and keeps the first valid duplicate', async () => {
    const snapshot = deferred<readonly ChatMessage[]>()
    const loadRecent = vi.fn().mockReturnValue(snapshot.promise)
    const mounted = mountLiveChat(loadRecent)

    mounted.emit({ type: 'chat-message', data: { ...message(2), message: 'live-first' }, id: '2' })
    snapshot.resolve([message(3), { ...message(2), message: 'snapshot-late' }, message(1)])
    await flushPromises()

    expect(loadRecent).toHaveBeenCalledWith(200, expect.any(AbortSignal))
    expect(mounted.chat().messages.value.map(item => [item.sequence, item.message])).toEqual([
      [1, 'message-1'],
      [2, 'live-first'],
      [3, 'message-3'],
    ])
    expect(mounted.chat().snapshotLoading.value).toBe(false)
    mounted.wrapper.unmount()
  })

  it('ignores invalid events and owns channel filter, unread and connection state', async () => {
    const mounted = mountLiveChat(vi.fn().mockResolvedValue([]))
    await flushPromises()

    mounted.emit({ type: 'chat-message', data: { ...message(1), channel: 'unsupported' } })
    mounted.chat().setChannelFilter('Whisper')
    mounted.chat().setFollowingLatest(false)
    mounted.emit({ type: 'chat-message', data: message(2, 'Whisper') })
    mounted.setStatus('live')

    expect(mounted.chat().messages.value).toEqual([message(2, 'Whisper')])
    expect(mounted.chat().channelFilter.value).toBe('Whisper')
    expect(mounted.chat().unreadCount.value).toBe(1)
    expect(mounted.chat().connectionStatus.value).toBe('live')
    mounted.chat().setFollowingLatest(true)
    expect(mounted.chat().unreadCount.value).toBe(0)
    mounted.wrapper.unmount()
  })

  it('keeps live data on snapshot failure, caps the newest window and cleans up on unmount', async () => {
    const snapshot = deferred<readonly ChatMessage[]>()
    let signal: AbortSignal | undefined
    const mounted = mountLiveChat(vi.fn((_limit, nextSignal) => {
      signal = nextSignal
      return snapshot.promise
    }))
    for (let sequence = 1; sequence <= 1001; sequence++)
      mounted.emit({ type: 'chat-message', data: message(sequence) })
    snapshot.reject(new Error('unavailable'))
    await flushPromises()

    expect(mounted.chat().messages.value).toHaveLength(1000)
    expect(mounted.chat().messages.value[0]?.sequence).toBe(2)
    const latestMessage = mounted.chat().messages.value[mounted.chat().messages.value.length - 1]
    expect(latestMessage?.sequence).toBe(1001)
    mounted.wrapper.unmount()
    expect(signal?.aborted).toBe(true)
    expect(mounted.unsubscribeEvents).toHaveBeenCalledOnce()
    expect(mounted.unsubscribeStatus).toHaveBeenCalledOnce()
  })

  it('marks a gap and coalesces a recent-context refresh without clearing live messages', async () => {
    const refresh = deferred<readonly ChatMessage[]>()
    const loadRecent = vi.fn()
      .mockResolvedValueOnce([message(1)])
      .mockReturnValueOnce(refresh.promise)
    const mounted = mountLiveChat(loadRecent)
    await flushPromises()

    mounted.emit({ type: 'gap', data: { afterSequence: 1 } })
    mounted.emit({ type: 'gap', data: { afterSequence: 1 } })
    mounted.emit({ type: 'chat-message', data: message(3) })
    expect(loadRecent).toHaveBeenCalledTimes(2)
    expect(mounted.chat().hasGap.value).toBe(true)

    refresh.resolve([message(2)])
    await flushPromises()
    expect(mounted.chat().messages.value.map(item => item.sequence)).toEqual([1, 2, 3])
    expect(mounted.chat().hasGap.value).toBe(true)
    mounted.wrapper.unmount()
  })
})
