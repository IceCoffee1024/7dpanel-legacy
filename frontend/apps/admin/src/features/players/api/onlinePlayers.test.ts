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
  deviceType: 'windows',
  ip: '192.0.2.10',
  ping: 23,
  compatibilityVersion: 'V 3.0.1',
  discordUserId: '18446744073709551615',
  permissionLevel: 1000,
  position: { x: 100.5, y: 51, z: 200.25 },
  isDead: false,
  health: 96,
  maxHealth: 100,
  level: 17,
  score: 827,
  zombieKills: 317,
  playerKills: 2,
  deaths: 4,
  totalTimePlayedMinutes: 4823.5,
  distanceWalkedMeters: 127540.75,
  totalItemsCrafted: 2360,
  longestLifeMinutes: 920.25,
  currentLifeMinutes: 134.5,
  observedAtUtc: '2026-07-22T08:30:00.123Z',
}

function validSnapshot() {
  return {
    players: [{
      ...validPlayer,
      platformIdentity: { ...validPlayer.platformIdentity },
      crossplatformIdentity: { ...validPlayer.crossplatformIdentity },
      position: { ...validPlayer.position },
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
    response.players[0]!.position.x = 999
    response.players.push(validPlayer)

    expect(result.players).toEqual([validPlayer])
    expect(result.players).not.toBe(response.players)
    expect(Object.isFrozen(result.players[0])).toBe(true)
    expect(Object.isFrozen(result.players[0]!.platformIdentity)).toBe(true)
    expect(Object.isFrozen(result.players[0]!.crossplatformIdentity)).toBe(true)
    expect(Object.isFrozen(result.players[0]!.position)).toBe(true)
    expect(result.players[0]!.position.x).toBe(100.5)
  })

  it('accepts null optional values', () => {
    const response = validSnapshot()
    response.players[0]!.crossplatformIdentity = null as unknown as typeof validPlayer.crossplatformIdentity
    response.players[0]!.ip = null as unknown as string
    response.players[0]!.compatibilityVersion = null as unknown as string
    response.players[0]!.discordUserId = null as unknown as string

    expect(parseOnlinePlayers(response).players[0]).toMatchObject({
      crossplatformIdentity: null,
      ip: null,
      compatibilityVersion: null,
      discordUserId: null,
    })
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
    ['a negative entity id', { ...validSnapshot(), players: [{ ...validPlayer, entityId: -1 }] }],
    ['an infinite ping', { ...validSnapshot(), players: [{ ...validPlayer, ping: Number.POSITIVE_INFINITY }] }],
    ['a fractional level', { ...validSnapshot(), players: [{ ...validPlayer, level: 2.5 }] }],
    ['a NaN health value', { ...validSnapshot(), players: [{ ...validPlayer, health: Number.NaN }] }],
    ['a missing approved field', { ...validSnapshot(), players: [{ ...validPlayer, score: undefined }] }],
    ['an unknown device', { ...validSnapshot(), players: [{ ...validPlayer, deviceType: 'switch' }] }],
    ['a missing position axis', { ...validSnapshot(), players: [{ ...validPlayer, position: { x: 1, y: 2 } }] }],
    ['a non-object position', { ...validSnapshot(), players: [{ ...validPlayer, position: '1,2,3' }] }],
    ['a non-finite position axis', { ...validSnapshot(), players: [{ ...validPlayer, position: { ...validPlayer.position, x: Number.NaN } }] }],
    ['a blank optional value', { ...validSnapshot(), players: [{ ...validPlayer, ip: ' ' }] }],
    ['a non-string Discord value', { ...validSnapshot(), players: [{ ...validPlayer, discordUserId: 1 }] }],
    ['a fractional accumulated item count', { ...validSnapshot(), players: [{ ...validPlayer, totalItemsCrafted: 1.5 }] }],
    ['a negative accumulated distance', { ...validSnapshot(), players: [{ ...validPlayer, distanceWalkedMeters: -1 }] }],
    ['an infinite accumulated duration', { ...validSnapshot(), players: [{ ...validPlayer, totalTimePlayedMinutes: Number.POSITIVE_INFINITY }] }],
    ['an invalid player in the same response array', { players: [validPlayer, { ...validPlayer, deaths: 1.5 }] }],
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
