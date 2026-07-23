import { afterEach, describe, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import { fetchOnlinePlayers, parseOnlinePlayers } from './onlinePlayers'

vi.mock('../../../shared/api/http', () => ({
  requestJson: vi.fn(),
}))

const validPlayer = {
  entityId: 42,
  name: 'Ada',
  observedAtUtc: '2026-07-22T08:30:00.123Z',
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
    players: [{
      ...validPlayer,
      platformIdentity: { ...validPlayer.platformIdentity },
      crossplatformIdentity: { ...validPlayer.crossplatformIdentity },
    }],
  }
}

describe('parseOnlinePlayers', () => {
  it('parses an empty player snapshot without root freshness fields', () => {
    const result = parseOnlinePlayers({ players: [] })

    expect(result).toEqual({ players: [] })
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
    ['a non-array players field', { players: {} }],
    ['a non-object player', { players: [null] }],
    ['a missing observation time', { ...validSnapshot(), players: [{ ...validPlayer, observedAtUtc: undefined }] }],
    ['an invalid observation time', { ...validSnapshot(), players: [{ ...validPlayer, observedAtUtc: 'not-a-date' }] }],
    ['a non-leap-year February 29 observation time', { ...validSnapshot(), players: [{ ...validPlayer, observedAtUtc: '2026-02-29T08:30:00Z' }] }],
    ['a non-UTC observation time', { ...validSnapshot(), players: [{ ...validPlayer, observedAtUtc: '2026-07-22T08:30:00+08:00' }] }],
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
      players: [],
    })
    const authorizationHeader = 'Bearer opaque.token+/= value'
    const controller = new AbortController()

    await expect(fetchOnlinePlayers(authorizationHeader, controller.signal)).resolves.toEqual({
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
