export type AuthRole = 'Owner' | 'Admin' | 'Viewer'
export type SessionPersistence = 'tab' | 'browser'

export interface AuthSession {
  token: string
  expiresAt: number
  username: string
  role: AuthRole
}

const sessionRecordKeys = ['expiresAt', 'role', 'token', 'username', 'version']

export function parseAuthSession(value: string | null, now: number): AuthSession | null {
  if (value === null)
    return null

  try {
    const record = JSON.parse(value) as Record<string, unknown>
    if (
      typeof record !== 'object'
      || record === null
      || Object.keys(record).sort().join(',') !== sessionRecordKeys.join(',')
      || record.version !== 1
      || typeof record.token !== 'string'
      || !record.token.startsWith('7dp_t_')
      || typeof record.expiresAt !== 'number'
      || !Number.isSafeInteger(record.expiresAt)
      || record.expiresAt <= now
      || typeof record.username !== 'string'
      || record.username.trim().length === 0
      || (record.role !== 'Owner' && record.role !== 'Admin' && record.role !== 'Viewer')
    ) {
      return null
    }

    return {
      token: record.token,
      expiresAt: record.expiresAt,
      username: record.username,
      role: record.role,
    }
  }
  catch {
    return null
  }
}

export function serializeAuthSession(session: AuthSession): string {
  return JSON.stringify({
    version: 1,
    token: session.token,
    expiresAt: session.expiresAt,
    username: session.username,
    role: session.role,
  })
}
