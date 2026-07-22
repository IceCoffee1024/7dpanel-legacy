import { HttpError, requestJson } from '../../../shared/api/http'

export interface AccessToken {
  token: string
  expiresAt: number
}

export type AuthErrorCode = 'invalid-credentials' | 'rate-limited' | 'unavailable' | 'invalid-response'

export class AuthError extends Error {
  readonly code: AuthErrorCode

  constructor(code: AuthErrorCode) {
    super(code)
    this.name = 'AuthError'
    this.code = code
  }
}

export function parseAccessToken(value: unknown, now: number): AccessToken {
  if (typeof value !== 'object' || value === null)
    throw new AuthError('invalid-response')

  const response = value as Record<string, unknown>
  const token = response.access_token
  const tokenType = response.token_type
  const expiresIn = response.expires_in

  if (
    typeof token !== 'string'
    || token.length === 0
    || typeof tokenType !== 'string'
    || tokenType.toLowerCase() !== 'bearer'
    || typeof expiresIn !== 'number'
    || !Number.isInteger(expiresIn)
    || expiresIn <= 0
  ) {
    throw new AuthError('invalid-response')
  }

  return {
    token,
    expiresAt: now + expiresIn * 1000,
  }
}

export async function loginWithPassword(
  username: string,
  password: string,
  signal?: AbortSignal,
): Promise<AccessToken> {
  const body = new URLSearchParams({
    grant_type: 'password',
    username,
    password,
  })

  try {
    const response = await requestJson<unknown>('/api/v1/auth/token', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8',
      },
      body,
      signal,
    })
    return parseAccessToken(response, Date.now())
  }
  catch (error) {
    if (error instanceof AuthError)
      throw error
    if (!(error instanceof HttpError))
      throw new AuthError('unavailable')
    if (error.status === 400 || error.status === 401)
      throw new AuthError('invalid-credentials')
    if (error.status === 429)
      throw new AuthError('rate-limited')
    throw new AuthError('unavailable')
  }
}
