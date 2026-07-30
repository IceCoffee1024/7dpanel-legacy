import { requestJson } from '../../../shared/api/http'

export interface BanEntry {
  playerId: string
  displayName: string
  bannedUntilUtc: string | null
  reason: string | null
}

export interface WhitelistEntry {
  playerId: string
  displayName: string
}

export interface BanInput extends BanEntry {}
export interface WhitelistInput extends WhitelistEntry {}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]) {
  return Object.keys(value).sort().join(',') === [...keys].sort().join(',')
}

function isUtcTimestamp(value: unknown): value is string {
  if (typeof value !== 'string' || Number.isNaN(Date.parse(value)))
    return false
  return /(?:Z|\+00:00)$/i.test(value)
}

export function parseBanList(value: unknown): readonly BanEntry[] {
  if (!Array.isArray(value))
    throw new Error('Invalid ban list response')
  const result = value.map((entry): BanEntry => {
    if (!isRecord(entry) || !hasExactKeys(entry, ['playerId', 'displayName', 'bannedUntilUtc', 'reason'])
      || typeof entry.playerId !== 'string' || entry.playerId.length === 0
      || typeof entry.displayName !== 'string'
      || (entry.bannedUntilUtc !== null && !isUtcTimestamp(entry.bannedUntilUtc))
      || (entry.reason !== null && typeof entry.reason !== 'string')) {
      throw new Error('Invalid ban list response')
    }
    return Object.freeze({
      playerId: entry.playerId,
      displayName: entry.displayName,
      bannedUntilUtc: entry.bannedUntilUtc,
      reason: entry.reason,
    })
  })
  return Object.freeze(result)
}

export function parseWhitelist(value: unknown): readonly WhitelistEntry[] {
  if (!Array.isArray(value))
    throw new Error('Invalid whitelist response')
  return Object.freeze(value.map((entry): WhitelistEntry => {
    if (!isRecord(entry) || !hasExactKeys(entry, ['playerId', 'displayName'])
      || typeof entry.playerId !== 'string' || entry.playerId.length === 0
      || typeof entry.displayName !== 'string') {
      throw new Error('Invalid whitelist response')
    }
    return Object.freeze({ playerId: entry.playerId, displayName: entry.displayName })
  }))
}

function headers(authorizationHeader: string, json = false): Record<string, string> {
  return json
    ? { 'Authorization': authorizationHeader, 'Content-Type': 'application/json' }
    : { Authorization: authorizationHeader }
}

export async function fetchBans(authorizationHeader: string, signal?: AbortSignal) {
  return parseBanList(await requestJson<unknown>('/api/v1/access-lists/bans', {
    headers: headers(authorizationHeader),
    signal,
  }))
}

export async function fetchWhitelist(authorizationHeader: string, signal?: AbortSignal) {
  return parseWhitelist(await requestJson<unknown>('/api/v1/access-lists/whitelist', {
    headers: headers(authorizationHeader),
    signal,
  }))
}

export async function upsertBan(authorizationHeader: string, input: BanInput, signal?: AbortSignal) {
  await requestJson<void>(`/api/v1/access-lists/bans/${encodeURIComponent(input.playerId)}`, {
    method: 'PUT',
    headers: headers(authorizationHeader, true),
    body: JSON.stringify({ displayName: input.displayName.trim(), bannedUntilUtc: input.bannedUntilUtc, reason: input.reason }),
    signal,
  })
}

export async function removeBan(authorizationHeader: string, playerId: string, signal?: AbortSignal) {
  await requestJson<void>(`/api/v1/access-lists/bans/${encodeURIComponent(playerId)}`, {
    method: 'DELETE',
    headers: headers(authorizationHeader),
    signal,
  })
}

export async function upsertWhitelist(authorizationHeader: string, input: WhitelistInput, signal?: AbortSignal) {
  await requestJson<void>(`/api/v1/access-lists/whitelist/${encodeURIComponent(input.playerId)}`, {
    method: 'PUT',
    headers: headers(authorizationHeader, true),
    body: JSON.stringify({ displayName: input.displayName.trim() }),
    signal,
  })
}

export async function removeWhitelist(authorizationHeader: string, playerId: string, signal?: AbortSignal) {
  await requestJson<void>(`/api/v1/access-lists/whitelist/${encodeURIComponent(playerId)}`, {
    method: 'DELETE',
    headers: headers(authorizationHeader),
    signal,
  })
}
