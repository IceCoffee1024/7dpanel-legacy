import type { App } from 'vue'

import type { GameEventPage, LoadGameEvents } from '../api/gameEvents'
import { flushPromises } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { createApp } from 'vue'
import { createEmptyGameEventFilters } from '../api/gameEvents'
import { useGameEvents } from './useGameEvents'

function page(id: string, nextCursor: string | null = null): GameEventPage {
  return Object.freeze({
    events: Object.freeze([Object.freeze({
      eventId: id,
      eventType: 'PlayerJoined' as const,
      occurredAtUtc: '2026-07-26T08:00:00Z',
      observedAtUtc: '2026-07-26T08:00:01Z',
      actor: null,
      target: null,
      gameShuttingDown: null,
    })]),
    gaps: Object.freeze([Object.freeze({
      gapId: 'a3dd7c7d-e9e7-47eb-a538-86b110935cd4',
      reason: 'QueueFull' as const,
      startedAtUtc: '2026-07-26T07:59:00Z',
      endedAtUtc: null,
      affectedCount: 1,
    })]),
    nextCursor,
  })
}

function mountComposable(load: LoadGameEvents) {
  let result!: ReturnType<typeof useGameEvents>
  const app = createApp({
    setup() {
      result = useGameEvents({
        auth: { authorizationHeader: 'Bearer owner', expireSession: vi.fn() },
        load,
      })
      return () => null
    },
  })
  app.mount(document.createElement('div'))
  return { app, result }
}

describe('useGameEvents', () => {
  const apps: App[] = []

  afterEach(() => {
    while (apps.length > 0)
      apps.pop()!.unmount()
  })

  it('owns an independent cursor and clears it on filter changes', async () => {
    const load = vi.fn<LoadGameEvents>()
      .mockResolvedValueOnce(page('first', 'event-cursor'))
      .mockResolvedValueOnce(page('second'))
      .mockResolvedValueOnce(page('filtered'))
    const mounted = mountComposable(load)
    apps.push(mounted.app)
    await flushPromises()

    await mounted.result.goToPage(2)
    expect(load.mock.calls[1]?.[2]).toBe('event-cursor')

    await mounted.result.applyFilters({ ...createEmptyGameEventFilters(), crossplatformId: 'EOS_next' })
    expect(mounted.result.pageNumber.value).toBe(1)
    expect(load.mock.calls[2]?.[1].crossplatformId).toBe('EOS_next')
    expect(load.mock.calls[2]?.[2]).toBeNull()
  })

  it('retains events and separate gaps as stale after refresh failure', async () => {
    const load = vi.fn<LoadGameEvents>()
      .mockResolvedValueOnce(page('retained'))
      .mockRejectedValueOnce(new Error('offline'))
    const mounted = mountComposable(load)
    apps.push(mounted.app)
    await flushPromises()

    await mounted.result.refresh()

    expect(mounted.result.state.value).toBe('stale')
    expect(mounted.result.events.value[0]?.eventId).toBe('retained')
    expect(mounted.result.gaps.value[0]?.reason).toBe('QueueFull')
  })
})
