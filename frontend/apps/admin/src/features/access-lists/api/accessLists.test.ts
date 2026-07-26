import { afterEach, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import { fetchBans, parseBanList, upsertWhitelist } from './accessLists'

vi.mock('../../../shared/api/http', () => ({ requestJson: vi.fn() }))

const ban = {
  playerId: 'EOS_1',
  displayName: 'Player',
  bannedUntilUtc: null,
  reason: 'reason',
}

afterEach(() => vi.clearAllMocks())

it('parses approved ban fields and freezes a copy', () => {
  const source = [{ ...ban }]
  const result = parseBanList(source)
  source[0]!.displayName = 'changed'

  expect(result).toEqual([ban])
  expect(Object.isFrozen(result)).toBe(true)
  expect(Object.isFrozen(result[0])).toBe(true)
})

it('rejects malformed access-list payloads', () => {
  expect(() => parseBanList([{ ...ban, serverPath: 'private' }])).toThrow('Invalid ban list response')
  expect(() => parseBanList([{ ...ban, bannedUntilUtc: '2026-07-26T08:00:00+08:00' }])).toThrow('Invalid ban list response')
})

it('uses stable URLs and safely encoded player identifiers', async () => {
  vi.mocked(requestJson).mockResolvedValueOnce([ban]).mockResolvedValueOnce(undefined)

  await fetchBans('Bearer token')
  await upsertWhitelist('Bearer token', { playerId: 'EOS id/1', displayName: 'Player' })

  expect(requestJson).toHaveBeenNthCalledWith(1, '/api/v1/access-lists/bans', {
    headers: { Authorization: 'Bearer token' },
    signal: undefined,
  })
  expect(requestJson).toHaveBeenNthCalledWith(2, '/api/v1/access-lists/whitelist/EOS%20id%2F1', {
    method: 'PUT',
    headers: { Authorization: 'Bearer token', 'Content-Type': 'application/json' },
    body: JSON.stringify({ displayName: 'Player' }),
    signal: undefined,
  })
})
