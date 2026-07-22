import { afterEach, describe, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import { fetchOnlinePlayers, parseOnlinePlayers } from './onlinePlayers'

vi.mock('../../../shared/api/http', () => ({
  requestJson: vi.fn(),
}))

const validPlayer = {
  entityId: 42,
  name: 'Ada',
  platformIdentity: {
    combinedId: 'Steam_123',
    platform: 'Steam',
  },
  crossplatformIdentity: {
    combinedId: 'EOS_456',
    platform: 'EOS',
  },
  ping: 23,
  level: 17,
  health: 96,
}

function validSnapshot() {
  return {
    capturedAtUtc: '2026-07-22T08:30:00.123Z',
    players: [{
      ...validPlayer,
      platformIdentity: { ...validPlayer.platformIdentity },
      crossplatformIdentity: { ...validPlayer.crossplatformIdentity },
    }],
  }
}

describe('parseOnlinePlayers', () => {
  it('parses an empty player snapshot and preserves the UTC text', () => {
    const result = parseOnlinePlayers({
      capturedAtUtc: '2026-07-22T08:30:00+00:00',
      players: [],
    })

    expect(result).toEqual({
      capturedAtUtc: '2026-07-22T08:30:00+00:00',
      players: [],
    })
    expect(Object.isFrozen(result)).toBe(true)
    expect(Object.isFrozen(result.players)).toBe(true)
  })

  it('copies and deeply freezes a complete player response', () => {
    const response = validSnapshot()

    const result = parseOnlinePlayers(response)
    response.players[0]!.name = 'Changed'
    response.players[0]!.platformIdentity.combinedId = 'Changed'
    response.players[0]!.crossplatformIdentity!.platform = 'Changed'
    response.players.push(validPlayer)

    expect(result.players).toEqual([validPlayer])
    expect(result.players).not.toBe(response.players)
    expect(Object.isFrozen(result.players[0])).toBe(true)
    expect(Object.isFrozen(result.players[0]!.platformIdentity)).toBe(true)
    expect(Object.isFrozen(result.players[0]!.crossplatformIdentity)).toBe(true)
  })

  it('accepts null cross-platform identity', () => {
    const response = validSnapshot()
    response.players[0]!.crossplatformIdentity = null as unknown as typeof validPlayer.crossplatformIdentity

    expect(parseOnlinePlayers(response).players[0]!.crossplatformIdentity).toBeNull()
  })

  it.each([
    ['a non-object root', null],
    ['an invalid date', { capturedAtUtc: 'not-a-date', players: [] }],
    ['a non-leap-year February 29 date', { capturedAtUtc: '2026-02-29T08:30:00Z', players: [] }],
    ['an April 31 date', { capturedAtUtc: '2026-04-31T08:30:00Z', players: [] }],
    ['a non-UTC date', { capturedAtUtc: '2026-07-22T08:30:00+08:00', players: [] }],
    ['a date without an explicit offset', { capturedAtUtc: '2026-07-22T08:30:00', players: [] }],
    ['a non-array players field', { capturedAtUtc: '2026-07-22T08:30:00Z', players: {} }],
    ['a non-object player', { capturedAtUtc: '2026-07-22T08:30:00Z', players: [null] }],
    ['an empty name', { ...validSnapshot(), players: [{ ...validPlayer, name: '   ' }] }],
    ['a missing platform identity', { ...validSnapshot(), players: [{ ...validPlayer, platformIdentity: null }] }],
    ['an empty platform combined id', { ...validSnapshot(), players: [{ ...validPlayer, platformIdentity: { combinedId: '', platform: 'Steam' } }] }],
    ['an empty platform name', { ...validSnapshot(), players: [{ ...validPlayer, platformIdentity: { combinedId: 'Steam_123', platform: ' ' } }] }],
    ['an invalid cross-platform identity', { ...validSnapshot(), players: [{ ...validPlayer, crossplatformIdentity: { combinedId: 'EOS_456', platform: '' } }] }],
    ['a fractional entity id', { ...validSnapshot(), players: [{ ...validPlayer, entityId: 1.5 }] }],
    ['an infinite ping', { ...validSnapshot(), players: [{ ...validPlayer, ping: Number.POSITIVE_INFINITY }] }],
    ['a fractional level', { ...validSnapshot(), players: [{ ...validPlayer, level: 2.5 }] }],
    ['a NaN health value', { ...validSnapshot(), players: [{ ...validPlayer, health: Number.NaN }] }],
  ])('rejects %s', (_name, value) => {
    expect(() => parseOnlinePlayers(value)).toThrow()
  })
})

describe('fetchOnlinePlayers', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('sends the supplied authorization header without placing the token in the URL or body', async () => {
    vi.mocked(requestJson).mockResolvedValue({
      capturedAtUtc: '2026-07-22T08:30:00Z',
      players: [],
    })
    const authorizationHeader = 'Bearer opaque.token+/= value'
    const controller = new AbortController()

    await expect(fetchOnlinePlayers(authorizationHeader, controller.signal)).resolves.toEqual({
      capturedAtUtc: '2026-07-22T08:30:00Z',
      players: [],
    })

    expect(requestJson).toHaveBeenCalledOnce()
    expect(requestJson).toHaveBeenCalledWith('/api/v1/players/online', {
      headers: { Authorization: authorizationHeader },
      signal: controller.signal,
    })
    const [path, options] = vi.mocked(requestJson).mock.calls[0]!
    expect(path).not.toContain(authorizationHeader)
    expect(options).not.toHaveProperty('body')
  })
})
