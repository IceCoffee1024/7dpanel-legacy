import { afterEach, describe, expect, it, vi } from 'vitest'

import { HttpError, requestJson } from '../../../shared/api/http'
import { AuthError, loginWithPassword, parseAccessToken } from './auth'

vi.mock('../../../shared/api/http', async (importOriginal) => {
  const original = await importOriginal<typeof import('../../../shared/api/http')>()
  return {
    ...original,
    requestJson: vi.fn(),
  }
})

function requestJsonMock() {
  return vi.mocked(requestJson)
}

describe('parseAccessToken', () => {
  it('parses an opaque bearer token and computes its Unix millisecond expiry', () => {
    expect(parseAccessToken({
      access_token: 'opaque.token+/=',
      token_type: 'bEaReR',
      expires_in: 90,
    }, 1_000)).toEqual({
      token: 'opaque.token+/=',
      expiresAt: 91_000,
    })
  })

  it.each([
    ['a non-object value', null],
    ['an empty token', { access_token: '', token_type: 'Bearer', expires_in: 60 }],
    ['a non-bearer token type', { access_token: 'token', token_type: 'Basic', expires_in: 60 }],
    ['a zero expiry', { access_token: 'token', token_type: 'Bearer', expires_in: 0 }],
    ['a fractional expiry', { access_token: 'token', token_type: 'Bearer', expires_in: 1.5 }],
  ])('rejects %s with a stable invalid-response error', (_name, value) => {
    expect(() => parseAccessToken(value, 0)).toThrowError(expect.objectContaining({
      code: 'invalid-response',
    }))
  })
})

describe('loginWithPassword', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('posts only the password grant fields without an Authorization header', async () => {
    requestJsonMock().mockResolvedValue({
      access_token: 'opaque-token',
      token_type: 'Bearer',
      expires_in: 120,
    })
    vi.spyOn(Date, 'now').mockReturnValue(5_000)
    const controller = new AbortController()

    await expect(loginWithPassword('admin@example.com', 'secret value', controller.signal)).resolves.toEqual({
      token: 'opaque-token',
      expiresAt: 125_000,
    })

    expect(requestJsonMock()).toHaveBeenCalledOnce()
    const [path, options] = requestJsonMock().mock.calls[0]!
    expect(path).toBe('/api/v1/auth/token')
    expect(options).toMatchObject({
      method: 'POST',
      signal: controller.signal,
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8',
      },
    })
    expect(options?.headers).not.toHaveProperty('Authorization')
    expect(options?.body).toBeInstanceOf(URLSearchParams)
    expect(Object.fromEntries((options?.body as URLSearchParams).entries())).toEqual({
      grant_type: 'password',
      username: 'admin@example.com',
      password: 'secret value',
    })
  })

  it.each([
    [400, 'invalid-credentials'],
    [401, 'invalid-credentials'],
    [429, 'rate-limited'],
    [500, 'unavailable'],
    [503, 'unavailable'],
  ] as const)('maps HTTP %s to %s without retaining OAuth details', async (status, code) => {
    requestJsonMock().mockRejectedValue(new HttpError(
      'http',
      'sensitive OAuth error_description',
      { status, problemCode: 'identity-provider-detail', traceId: 'trace-secret' },
    ))

    const error = await loginWithPassword('sensitive-user', 'sensitive-password').catch(value => value)

    expect(error).toEqual(new AuthError(code))
    expect(JSON.stringify(error)).not.toContain('sensitive')
    expect(String(error)).not.toContain('OAuth')
    expect(error).not.toHaveProperty('status')
    expect(error).not.toHaveProperty('problemCode')
    expect(error).not.toHaveProperty('traceId')
  })

  it.each(['network', 'timeout'] as const)('maps %s failures to unavailable', async (httpCode) => {
    requestJsonMock().mockRejectedValue(new HttpError(httpCode, 'sensitive transport detail'))

    await expect(loginWithPassword('user', 'password')).rejects.toEqual(new AuthError('unavailable'))
  })

  it('maps malformed successful responses to invalid-response', async () => {
    requestJsonMock().mockResolvedValue({
      access_token: 'token',
      token_type: 'Basic',
      expires_in: 60,
      error_description: 'sensitive response detail',
    })

    const error = await loginWithPassword('user', 'password').catch(value => value)

    expect(error).toEqual(new AuthError('invalid-response'))
    expect(JSON.stringify(error)).not.toContain('sensitive')
  })
})
