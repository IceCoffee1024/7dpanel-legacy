import { requestJson } from '../../../shared/api/http'

export interface PlayerIdentity {
  combinedId: string
  platform: string
}

export interface OnlinePlayer {
  entityId: number
  name: string
  observedAtUtc: string
  platformIdentity: PlayerIdentity
  crossplatformIdentity: PlayerIdentity | null
  ping: number
  level: number
  health: number
}

export interface OnlinePlayersSnapshot {
  players: readonly OnlinePlayer[]
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isValidUtcTimestamp(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:Z|[+-]00:00)$/.exec(value)
  if (!match)
    return false

  const [, yearText, monthText, dayText, hourText, minuteText, secondText, fractionText] = match
  const normalized = value.endsWith('Z') ? value : `${value.slice(0, -6)}Z`
  const timestamp = Date.parse(normalized)
  if (!Number.isFinite(timestamp))
    return false

  const date = new Date(timestamp)
  const millisecond = Number((fractionText ?? '').padEnd(3, '0').slice(0, 3) || 0)
  return date.getUTCFullYear() === Number(yearText)
    && date.getUTCMonth() + 1 === Number(monthText)
    && date.getUTCDate() === Number(dayText)
    && date.getUTCHours() === Number(hourText)
    && date.getUTCMinutes() === Number(minuteText)
    && date.getUTCSeconds() === Number(secondText)
    && date.getUTCMilliseconds() === millisecond
}

function parseIdentity(value: unknown): PlayerIdentity {
  if (!isRecord(value)
    || typeof value.combinedId !== 'string'
    || value.combinedId.trim() === ''
    || typeof value.platform !== 'string'
    || value.platform.trim() === '') {
    throw new Error('Invalid online players response')
  }

  return Object.freeze({
    combinedId: value.combinedId,
    platform: value.platform,
  })
}

function parseInteger(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || !Number.isInteger(value))
    throw new Error('Invalid online players response')
  return value
}

function parsePlayer(value: unknown): OnlinePlayer {
  if (!isRecord(value)
    || typeof value.name !== 'string'
    || value.name.trim() === ''
    || typeof value.observedAtUtc !== 'string'
    || !isValidUtcTimestamp(value.observedAtUtc)) {
    throw new Error('Invalid online players response')
  }

  return Object.freeze({
    entityId: parseInteger(value.entityId),
    name: value.name,
    observedAtUtc: value.observedAtUtc,
    platformIdentity: parseIdentity(value.platformIdentity),
    crossplatformIdentity: value.crossplatformIdentity === null
      ? null
      : parseIdentity(value.crossplatformIdentity),
    ping: parseInteger(value.ping),
    level: parseInteger(value.level),
    health: parseInteger(value.health),
  })
}

export function parseOnlinePlayers(value: unknown): OnlinePlayersSnapshot {
  if (!isRecord(value) || !Array.isArray(value.players)) {
    throw new Error('Invalid online players response')
  }

  return Object.freeze({
    players: Object.freeze(value.players.map(parsePlayer)),
  })
}

export async function fetchOnlinePlayers(
  authorizationHeader: string,
  signal?: AbortSignal,
): Promise<OnlinePlayersSnapshot> {
  const response = await requestJson<unknown>('/api/v1/players/online', {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseOnlinePlayers(response)
}
