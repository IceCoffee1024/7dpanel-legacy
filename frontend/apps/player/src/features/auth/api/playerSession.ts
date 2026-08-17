export interface PlayerSession {
  readonly steamId: string
  readonly primaryId: string
  readonly displayName: string
}

export type PlayerSessionResult =
  | { readonly kind: 'authenticated', readonly session: PlayerSession }
  | { readonly kind: 'unauthenticated' }

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function nonBlankString(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '')
    throw new Error('Invalid player session response')

  return value
}

function parsePlayerSession(value: unknown): PlayerSession {
  if (!isRecord(value))
    throw new Error('Invalid player session response')

  const keys = Object.keys(value).sort()
  if (keys.join(',') !== 'displayName,primaryId,steamId')
    throw new Error('Invalid player session response')

  return Object.freeze({
    steamId: nonBlankString(value.steamId),
    primaryId: nonBlankString(value.primaryId),
    displayName: nonBlankString(value.displayName),
  })
}

export async function fetchPlayerSession(signal?: AbortSignal): Promise<PlayerSessionResult> {
  const response = await fetch('/api/v1/player/me', {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
    signal,
  })

  if (response.status === 401)
    return { kind: 'unauthenticated' }

  if (!response.ok)
    throw new Error(`Unable to load player session (${response.status})`)

  return {
    kind: 'authenticated',
    session: parsePlayerSession(await response.json()),
  }
}

export async function logoutPlayerSession(signal?: AbortSignal): Promise<void> {
  const response = await fetch('/api/v1/player/logout', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
    signal,
  })

  if (!response.ok)
    throw new Error(`Unable to end player session (${response.status})`)
}
