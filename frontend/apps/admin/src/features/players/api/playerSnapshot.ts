export interface PlayerIdentity {
  readonly combinedId: string
  readonly platform: string
}

export type PlayerDeviceType = 'linux' | 'mac' | 'windows' | 'playStation' | 'xbox' | 'unknown'

export type PlayerPosition = Readonly<{
  x: number
  y: number
  z: number
}>

export interface PlayerSnapshot {
  readonly entityId: number
  readonly name: string
  readonly platformIdentity: PlayerIdentity
  readonly crossplatformIdentity: PlayerIdentity | null
  readonly deviceType: PlayerDeviceType
  readonly ip: string | null
  readonly ping: number
  readonly compatibilityVersion: string | null
  readonly discordUserId: string | null
  readonly permissionLevel: number
  readonly position: PlayerPosition
  readonly isDead: boolean
  readonly health: number
  readonly maxHealth: number
  readonly level: number
  readonly playGroup: string | null
  readonly lastLoginUtc: string | null
  readonly gameStage: number | null
  readonly expToNextLevel: number | null
  readonly skillPoints: number | null
  readonly bedroll: PlayerPosition | null
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

function invalidResponse(): never {
  throw new Error('Invalid player snapshot response')
}

export function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function isValidUtcTimestamp(value: string): boolean {
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

export function parseInteger(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || !Number.isInteger(value))
    return invalidResponse()
  return value
}

export function parseNonNegativeInteger(value: unknown): number {
  const result = parseInteger(value)
  if (result < 0)
    return invalidResponse()
  return result
}

export function parseFiniteNumber(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value))
    return invalidResponse()
  return value
}

export function parseNonNegativeNumber(value: unknown): number {
  const result = parseFiniteNumber(value)
  if (result < 0)
    return invalidResponse()
  return result
}

export function parseNullableNonBlankString(value: unknown): string | null {
  if (value === null)
    return null
  if (typeof value !== 'string' || value.trim() === '')
    return invalidResponse()
  return value
}

export function parsePlayerIdentity(value: unknown): PlayerIdentity {
  if (!isRecord(value)
    || typeof value.combinedId !== 'string'
    || value.combinedId.trim() === ''
    || typeof value.platform !== 'string'
    || value.platform.trim() === '') {
    return invalidResponse()
  }

  return Object.freeze({
    combinedId: value.combinedId,
    platform: value.platform,
  })
}

export function parsePlayerPosition(value: unknown): PlayerPosition {
  if (!isRecord(value))
    return invalidResponse()

  return Object.freeze({
    x: parseFiniteNumber(value.x),
    y: parseFiniteNumber(value.y),
    z: parseFiniteNumber(value.z),
  })
}

function parseDeviceType(value: unknown): PlayerDeviceType {
  switch (value) {
    case 'linux':
    case 'mac':
    case 'windows':
    case 'playStation':
    case 'xbox':
    case 'unknown':
      return value
    default:
      return invalidResponse()
  }
}

function parseNullableUtcTimestamp(value: unknown): string | null {
  if (value === null)
    return null
  if (typeof value !== 'string' || !isValidUtcTimestamp(value))
    return invalidResponse()
  return value
}

function parseNullableNonNegativeInteger(value: unknown): number | null {
  if (value === null)
    return null
  return parseNonNegativeInteger(value)
}

function parseNullablePosition(value: unknown): PlayerPosition | null {
  if (value === null)
    return null
  return parsePlayerPosition(value)
}

export function parsePlayerSnapshot(value: unknown): PlayerSnapshot {
  if (!isRecord(value)
    || typeof value.name !== 'string'
    || value.name.trim() === ''
    || typeof value.observedAtUtc !== 'string'
    || !isValidUtcTimestamp(value.observedAtUtc)
    || typeof value.isDead !== 'boolean') {
    return invalidResponse()
  }

  return Object.freeze({
    entityId: parseNonNegativeInteger(value.entityId),
    name: value.name,
    platformIdentity: parsePlayerIdentity(value.platformIdentity),
    crossplatformIdentity: value.crossplatformIdentity === null
      ? null
      : parsePlayerIdentity(value.crossplatformIdentity),
    deviceType: parseDeviceType(value.deviceType),
    ip: parseNullableNonBlankString(value.ip),
    ping: parseInteger(value.ping),
    compatibilityVersion: parseNullableNonBlankString(value.compatibilityVersion),
    discordUserId: parseNullableNonBlankString(value.discordUserId),
    permissionLevel: parseInteger(value.permissionLevel),
    position: parsePlayerPosition(value.position),
    isDead: value.isDead,
    health: parseInteger(value.health),
    maxHealth: parseInteger(value.maxHealth),
    level: parseInteger(value.level),
    playGroup: parseNullableNonBlankString(value.playGroup),
    lastLoginUtc: parseNullableUtcTimestamp(value.lastLoginUtc),
    gameStage: parseNullableNonNegativeInteger(value.gameStage),
    expToNextLevel: parseNullableNonNegativeInteger(value.expToNextLevel),
    skillPoints: parseNullableNonNegativeInteger(value.skillPoints),
    bedroll: parseNullablePosition(value.bedroll),
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
