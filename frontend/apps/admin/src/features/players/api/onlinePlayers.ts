import { requestJson } from '../../../shared/api/http'

export interface PlayerIdentity {
  readonly combinedId: string
  readonly platform: string
}

export type OnlinePlayerDeviceType = 'linux' | 'mac' | 'windows' | 'playStation' | 'xbox' | 'unknown'

export type OnlinePlayerPosition = Readonly<{
  x: number
  y: number
  z: number
}>

export interface OnlinePlayer {
  readonly entityId: number
  readonly name: string
  readonly platformIdentity: PlayerIdentity
  readonly crossplatformIdentity: PlayerIdentity | null
  readonly deviceType: OnlinePlayerDeviceType
  readonly ip: string | null
  readonly ping: number
  readonly compatibilityVersion: string | null
  readonly discordUserId: string | null
  readonly permissionLevel: number
  readonly position: OnlinePlayerPosition
  readonly isDead: boolean
  readonly health: number
  readonly maxHealth: number
  readonly level: number
  readonly score: number
  readonly zombieKills: number
  readonly playerKills: number
  readonly deaths: number
  readonly totalTimePlayedMinutes: number
  readonly distanceWalkedMeters: number
  readonly totalItemsCrafted: number
  readonly longestLifeMinutes: number
  readonly currentLifeMinutes: number
  readonly observedAtUtc: string
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

function parseNonNegativeInteger(value: unknown): number {
  const result = parseInteger(value)
  if (result < 0)
    throw new Error('Invalid online players response')
  return result
}

function parseFiniteNumber(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value))
    throw new Error('Invalid online players response')
  return value
}

function parseNonNegativeNumber(value: unknown): number {
  const result = parseFiniteNumber(value)
  if (result < 0)
    throw new Error('Invalid online players response')
  return result
}

function parseNullableNonBlankString(value: unknown): string | null {
  if (value === null)
    return null
  if (typeof value !== 'string' || value.trim() === '')
    throw new Error('Invalid online players response')
  return value
}

function parseDeviceType(value: unknown): OnlinePlayerDeviceType {
  switch (value) {
    case 'linux':
    case 'mac':
    case 'windows':
    case 'playStation':
    case 'xbox':
    case 'unknown':
      return value
    default:
      throw new Error('Invalid online players response')
  }
}

function parsePosition(value: unknown): OnlinePlayerPosition {
  if (!isRecord(value))
    throw new Error('Invalid online players response')

  return Object.freeze({
    x: parseFiniteNumber(value.x),
    y: parseFiniteNumber(value.y),
    z: parseFiniteNumber(value.z),
  })
}

function parsePlayer(value: unknown): OnlinePlayer {
  if (!isRecord(value)
    || typeof value.name !== 'string'
    || value.name.trim() === ''
    || typeof value.observedAtUtc !== 'string'
    || !isValidUtcTimestamp(value.observedAtUtc)
    || typeof value.isDead !== 'boolean') {
    throw new Error('Invalid online players response')
  }

  return Object.freeze({
    entityId: parseNonNegativeInteger(value.entityId),
    name: value.name,
    platformIdentity: parseIdentity(value.platformIdentity),
    crossplatformIdentity: value.crossplatformIdentity === null
      ? null
      : parseIdentity(value.crossplatformIdentity),
    deviceType: parseDeviceType(value.deviceType),
    ip: parseNullableNonBlankString(value.ip),
    ping: parseInteger(value.ping),
    compatibilityVersion: parseNullableNonBlankString(value.compatibilityVersion),
    discordUserId: parseNullableNonBlankString(value.discordUserId),
    permissionLevel: parseInteger(value.permissionLevel),
    position: parsePosition(value.position),
    isDead: value.isDead,
    health: parseInteger(value.health),
    maxHealth: parseInteger(value.maxHealth),
    level: parseInteger(value.level),
    score: parseInteger(value.score),
    zombieKills: parseInteger(value.zombieKills),
    playerKills: parseInteger(value.playerKills),
    deaths: parseInteger(value.deaths),
    totalTimePlayedMinutes: parseNonNegativeNumber(value.totalTimePlayedMinutes),
    distanceWalkedMeters: parseNonNegativeNumber(value.distanceWalkedMeters),
    totalItemsCrafted: parseNonNegativeInteger(value.totalItemsCrafted),
    longestLifeMinutes: parseNonNegativeNumber(value.longestLifeMinutes),
    currentLifeMinutes: parseNonNegativeNumber(value.currentLifeMinutes),
    observedAtUtc: value.observedAtUtc,
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
