import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  createChatMuteMutation,
  listChatMutesQuery,
  releaseChatMuteMutation,
  updateChatMuteMutation,
} from '../../../shared/api/generated/@pinia/colada.gen'
import {
  createChatMuteRecord,
  loadChatMutes,
  parseChatMutePage,
  releaseChatMuteRecord,
  updateChatMuteRecord,
} from './chatMutes'

vi.mock('../../../shared/api/generated/@pinia/colada.gen', () => ({
  createChatMuteMutation: vi.fn(),
  listChatMutesQuery: vi.fn(),
  releaseChatMuteMutation: vi.fn(),
  updateChatMuteMutation: vi.fn(),
}))

const mute = {
  crossplatformId: 'EOS_player',
  displayName: 'Player',
  reason: 'spam',
  mutedUntilUtc: null,
  createdBy: 'owner',
  createdAtUtc: '2026-07-26T08:00:00Z',
  updatedBy: 'owner',
  updatedAtUtc: '2026-07-26T08:00:00Z',
}

describe('chat-mute generated transport', () => {
  beforeEach(() => vi.clearAllMocks())

  it('uses the generated list operation and strict parser', async () => {
    const query = vi.fn().mockResolvedValue({
      mutes: [mute],
      nextCursorUpdatedAtUtc: '2026-07-26T08:00:00Z',
      nextCursorCrossplatformId: 'EOS_player',
    })
    vi.mocked(listChatMutesQuery).mockReturnValue({ query } as never)
    const signal = new AbortController().signal

    await loadChatMutes('Bearer owner', { updatedAtUtc: null, crossplatformId: null }, 50, signal)

    expect(listChatMutesQuery).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer owner' },
      query: { limit: 50 },
    })
    expect(query).toHaveBeenCalledWith(expect.objectContaining({ signal }))
  })

  it('uses generated create, update, and release mutation contracts', async () => {
    const create = vi.fn().mockResolvedValue(mute)
    const update = vi.fn().mockResolvedValue({ ...mute, reason: 'updated' })
    const release = vi.fn().mockResolvedValue(undefined)
    vi.mocked(createChatMuteMutation).mockReturnValue({ mutation: create } as never)
    vi.mocked(updateChatMuteMutation).mockReturnValue({ mutation: update } as never)
    vi.mocked(releaseChatMuteMutation).mockReturnValue({ mutation: release } as never)
    const signal = new AbortController().signal

    await createChatMuteRecord('Bearer owner', {
      crossplatformId: 'EOS_player',
      displayName: 'Player',
      reason: 'spam',
      mutedUntilUtc: null,
      correlationId: null,
    }, signal)
    await updateChatMuteRecord('Bearer owner', 'EOS_player', {
      displayName: 'Player',
      reason: 'updated',
      mutedUntilUtc: null,
      correlationId: null,
    }, signal)
    await releaseChatMuteRecord('Bearer owner', 'EOS_player', 'correlation-1', signal)

    expect(create.mock.calls[0]?.[0]).toEqual({
      body: { crossplatformId: 'EOS_player', displayName: 'Player', reason: 'spam', mutedUntilUtc: null, correlationId: null },
      signal,
    })
    expect(update.mock.calls[0]?.[0]).toEqual({
      path: { crossplatformId: 'EOS_player' },
      body: { displayName: 'Player', reason: 'updated', mutedUntilUtc: null, correlationId: null },
      signal,
    })
    expect(release.mock.calls[0]?.[0]).toEqual({
      path: { crossplatformId: 'EOS_player' },
      query: { correlationId: 'correlation-1' },
      signal,
    })
  })

  it.each([
    ['missing required field', () => ({ mutes: [{ ...mute, reason: undefined }], nextCursorUpdatedAtUtc: null, nextCursorCrossplatformId: null })],
    ['non-UTC timestamp', () => ({ mutes: [{ ...mute, createdAtUtc: '2026-07-26T16:00:00+08:00' }], nextCursorUpdatedAtUtc: null, nextCursorCrossplatformId: null })],
    ['partial cursor', () => ({ mutes: [mute], nextCursorUpdatedAtUtc: '2026-07-26T08:00:00Z', nextCursorCrossplatformId: null })],
  ])('rejects %s', (_label, input) => {
    expect(() => parseChatMutePage(input())).toThrow('Invalid chat mute page response')
  })
})
