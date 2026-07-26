import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  chatGetRecentMessagesQuery,
  chatSendGlobalMessageMutation,
  chatSendPrivateMessageMutation,
} from '../../../shared/api/generated/@pinia/colada.gen'
import {
  createRecentChatMessagesLoader,
  parseRecentChatMessages,
  sendChatMessage,
} from './chat'

vi.mock('../../../shared/api/generated/@pinia/colada.gen', () => ({
  chatGetRecentMessagesQuery: vi.fn(),
  chatSendGlobalMessageMutation: vi.fn(),
  chatSendPrivateMessageMutation: vi.fn(),
}))

const validMessage = {
  sequence: 1,
  occurredAtUtc: '2026-07-26T08:00:00Z',
  entityId: 7,
  crossplatformId: 'EOS_player',
  senderName: 'Player',
  channel: 'Global',
  sourceKind: 'Player',
  message: 'hello',
}

describe('chat generated transport', () => {
  beforeEach(() => vi.clearAllMocks())

  it('loads recent messages through the generated query definition and strict parser', async () => {
    const query = vi.fn().mockResolvedValue({ messages: [validMessage] })
    vi.mocked(chatGetRecentMessagesQuery).mockReturnValue({ query } as never)
    const loader = createRecentChatMessagesLoader(() => 'Bearer owner')
    const controller = new AbortController()

    await expect(loader(200, controller.signal)).resolves.toEqual([validMessage])

    expect(chatGetRecentMessagesQuery).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer owner' },
      query: { limit: 200 },
    })
    expect(query).toHaveBeenCalledWith(expect.objectContaining({ signal: controller.signal }))
  })

  it('routes global and private sends through their generated mutation definitions', async () => {
    const sendGlobal = vi.fn().mockResolvedValue({ status: 'Accepted' })
    const sendPrivate = vi.fn().mockResolvedValue({ status: 'Accepted' })
    vi.mocked(chatSendGlobalMessageMutation).mockReturnValue({ mutation: sendGlobal } as never)
    vi.mocked(chatSendPrivateMessageMutation).mockReturnValue({ mutation: sendPrivate } as never)
    const controller = new AbortController()

    await sendChatMessage('Bearer owner', { message: 'global', targetCrossplatformId: null }, controller.signal)
    await sendChatMessage('Bearer owner', { message: 'private', targetCrossplatformId: 'EOS_target' }, controller.signal)

    expect(chatSendGlobalMessageMutation).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer owner' },
    })
    expect(sendGlobal.mock.calls[0]?.[0]).toEqual({ body: { message: 'global' }, signal: controller.signal })
    expect(chatSendPrivateMessageMutation).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer owner' },
    })
    expect(sendPrivate.mock.calls[0]?.[0]).toEqual({
      body: { message: 'private', targetCrossplatformId: 'EOS_target' },
      signal: controller.signal,
    })
  })

  it('rejects optional or unknown generated response fields instead of weakening parsing', () => {
    expect(() => parseRecentChatMessages({ messages: [{ ...validMessage, channel: 'Other' }] })).toThrow(
      'Invalid recent chat messages response',
    )
    expect(() => parseRecentChatMessages({ messages: [validMessage], extension: true })).toThrow(
      'Invalid recent chat messages response',
    )
  })
})
