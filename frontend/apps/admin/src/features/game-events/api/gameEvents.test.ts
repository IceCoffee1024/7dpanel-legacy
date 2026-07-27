import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listGameEventsQuery } from '../../../shared/api/generated/@pinia/colada.gen'
import { createEmptyGameEventFilters, loadGameEvents, parseGameEventPage } from './gameEvents'

vi.mock('../../../shared/api/generated/@pinia/colada.gen', () => ({
  listGameEventsQuery: vi.fn(),
}))

function validPage() {
  return {
    events: [{
      eventId: '9a5c37a0-8055-45cf-8618-9a4c16fe539c',
      eventType: 'PlayerJoined',
      occurredAtUtc: '2026-07-26T08:00:00Z',
      observedAtUtc: '2026-07-26T08:00:01Z',
      actor: {
        crossplatformId: 'EOS_player',
        platformId: null,
        entityId: 7,
        displayName: 'Player',
      },
      target: null,
      gameShuttingDown: null,
    }],
    gaps: [{
      gapId: 'a3dd7c7d-e9e7-47eb-a538-86b110935cd4',
      reason: 'QueueFull',
      startedAtUtc: '2026-07-26T07:59:00Z',
      endedAtUtc: null,
      affectedCount: 3,
    }],
    nextCursor: 'opaque-event-cursor',
  }
}

describe('game-event generated transport', () => {
  beforeEach(() => vi.clearAllMocks())

  it('uses listGameEvents and keeps gap metadata separate from events', async () => {
    const query = vi.fn().mockResolvedValue(validPage())
    vi.mocked(listGameEventsQuery).mockReturnValue({ query } as never)
    const signal = new AbortController().signal

    const result = await loadGameEvents(
      'Bearer owner',
      { ...createEmptyGameEventFilters(), eventType: 'PlayerJoined' },
      null,
      50,
      signal,
    )

    expect(listGameEventsQuery).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer owner' },
      query: { eventType: 'PlayerJoined', limit: '50' },
    })
    expect(query).toHaveBeenCalledWith(expect.objectContaining({ signal }))
    expect(result.events).toHaveLength(1)
    expect(result.gaps).toHaveLength(1)
  })

  it.each([
    ['missing event field', () => {
      const page = validPage()
      delete (page.events[0] as Partial<typeof page.events[number]>).eventId
      return page
    }],
    ['unknown event enum', () => {
      const page = validPage()
      page.events[0]!.eventType = 'PlayerSpawned'
      return page
    }],
    ['unknown gap enum', () => {
      const page = validPage()
      page.gaps[0]!.reason = 'Unknown'
      return page
    }],
    ['non-UTC timestamp', () => {
      const page = validPage()
      page.events[0]!.observedAtUtc = '2026-07-26T16:00:01+08:00'
      return page
    }],
    ['empty cursor', () => ({ ...validPage(), nextCursor: '' })],
  ])('rejects %s', (_label, input) => {
    expect(() => parseGameEventPage(input())).toThrow('Invalid game event page response')
  })
})
