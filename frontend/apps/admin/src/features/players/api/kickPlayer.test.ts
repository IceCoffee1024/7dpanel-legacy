import { afterEach, describe, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import { kickPlayer, parseKickPlayerResponse } from './kickPlayer'

vi.mock('../../../shared/api/http', () => ({
  requestJson: vi.fn(),
}))

const validResponse = {
  operationId: '8f742dcfe65a454d8f919e164ace77d7',
  status: 'succeeded',
  target: {
    entityId: 7,
    name: 'Ada',
    platformIdentity: {
      combinedId: 'Steam_123',
      platform: 'Steam',
    },
  },
  requestedAtUtc: '2026-07-22T08:00:00.0000000+00:00',
  completedAtUtc: '2026-07-22T08:00:00.1000000+00:00',
}

describe('parseKickPlayerResponse', () => {
  it('copies and freezes only the approved response fields', () => {
    const response = {
      ...validResponse,
      internalDependency: 'ignored',
      target: {
        ...validResponse.target,
        ipAddress: '192.0.2.10',
        platformIdentity: {
          ...validResponse.target.platformIdentity,
          internalIdentity: 'ignored',
        },
      },
    }

    const result = parseKickPlayerResponse(response)
    response.target.name = 'Changed'
    response.target.platformIdentity.combinedId = 'Changed'

    expect(result).toEqual(validResponse)
    expect(Object.isFrozen(result)).toBe(true)
    expect(Object.isFrozen(result.target)).toBe(true)
    expect(Object.isFrozen(result.target.platformIdentity)).toBe(true)
    expect(result).not.toHaveProperty('internalDependency')
    expect(result.target).not.toHaveProperty('ipAddress')
  })

  it.each([
    ['a non-object root', null],
    ['an uppercase operation id', { ...validResponse, operationId: '8F742DCFE65A454D8F919E164ACE77D7' }],
    ['a short operation id', { ...validResponse, operationId: '8f742dcf' }],
    ['an unknown status', { ...validResponse, status: 'failed' }],
    ['a negative target entity id', { ...validResponse, target: { ...validResponse.target, entityId: -1 } }],
    ['a fractional target entity id', { ...validResponse, target: { ...validResponse.target, entityId: 1.5 } }],
    ['an empty target name', { ...validResponse, target: { ...validResponse.target, name: '   ' } }],
    ['a missing target identity', { ...validResponse, target: { ...validResponse.target, platformIdentity: null } }],
    ['an empty target combined id', { ...validResponse, target: { ...validResponse.target, platformIdentity: { combinedId: '', platform: 'Steam' } } }],
    ['an empty target platform', { ...validResponse, target: { ...validResponse.target, platformIdentity: { combinedId: 'Steam_123', platform: ' ' } } }],
    ['an invalid requested date', { ...validResponse, requestedAtUtc: 'not-a-date' }],
    ['a non-existent completed date', { ...validResponse, completedAtUtc: '2026-02-29T08:00:00Z' }],
    ['a non-UTC completed date', { ...validResponse, completedAtUtc: '2026-07-22T16:00:00+08:00' }],
  ])('rejects %s', (_name, value) => {
    expect(() => parseKickPlayerResponse(value)).toThrow('Invalid kick player response')
  })
})

describe('kickPlayer', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('posts the confirmed identity and reason with the bearer token only in the header', async () => {
    vi.mocked(requestJson).mockResolvedValue(validResponse)
    const authorizationHeader = 'Bearer opaque.token+/= value'
    const controller = new AbortController()

    await expect(kickPlayer(authorizationHeader, {
      entityId: 7,
      expectedPlatformIdentity: {
        combinedId: 'Steam_123',
        platform: 'Steam',
      },
      reason: '违反服务器规则',
    }, controller.signal)).resolves.toEqual(validResponse)

    expect(requestJson).toHaveBeenCalledOnce()
    expect(requestJson).toHaveBeenCalledWith('/api/v1/players/7/kick', {
      method: 'POST',
      headers: {
        'Authorization': authorizationHeader,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        expectedPlatformIdentity: {
          combinedId: 'Steam_123',
          platform: 'Steam',
        },
        reason: '违反服务器规则',
        confirmed: true,
      }),
      signal: controller.signal,
    })

    const [path, options] = vi.mocked(requestJson).mock.calls[0]!
    expect(path).not.toContain(authorizationHeader)
    expect(options?.body).not.toContain(authorizationHeader)
    expect(options).not.toHaveProperty('timeoutMs')
  })

  it.each([
    -1,
    1.5,
    Number.NaN,
    Number.POSITIVE_INFINITY,
    Number.MAX_SAFE_INTEGER + 1,
    1e21,
  ])('rejects invalid entity id %s before requesting', async (entityId) => {
    await expect(kickPlayer('Bearer token', {
      entityId,
      expectedPlatformIdentity: validResponse.target.platformIdentity,
      reason: 'reason',
    })).rejects.toThrow('Invalid kick player entity id')

    expect(requestJson).not.toHaveBeenCalled()
  })
})
