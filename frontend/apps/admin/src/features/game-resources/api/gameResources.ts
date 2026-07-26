export type GameResourceKind = 'item' | 'block'
export type GameResourceVisibility = 'public' | 'hidden'
export type GameResourceIconStatus = 'available' | 'missing' | 'invalid'
export type GameResourceLanguage = 'zh-CN' | 'en'
export type GameResourceViewState
  = | 'loading'
    | 'success'
    | 'empty'
    | 'stale'
    | 'building'
    | 'unavailable'
    | 'partial'
    | 'forbidden'

export interface GameResourceItem {
  readonly resourceId: string
  readonly numericId: number
  readonly internalName: string
  readonly localizedName: string | null
  readonly kind: GameResourceKind
  readonly visibility: GameResourceVisibility
  readonly maxStack: number | null
  readonly hasQuality: boolean | null
  readonly iconStatus: GameResourceIconStatus
  readonly iconTintHex: string | null
}

export interface GameResourcePage {
  readonly catalogVersion: string
  readonly gameVersion: string | null
  readonly observedAtUtc: string
  readonly total: number
  readonly page: number
  readonly pageSize: number
  readonly warnings: readonly string[]
  readonly items: readonly GameResourceItem[]
}

export interface GameResourceRequestQuery {
  readonly search?: string
  readonly kind?: GameResourceKind
  readonly includeHidden: boolean
  readonly language: GameResourceLanguage
  readonly page: number
  readonly pageSize: number
}

export type GameResourcesRequest = (
  query: GameResourceRequestQuery,
  signal: AbortSignal,
) => Promise<unknown>

export type LoadGameResources = (
  query: GameResourceRequestQuery,
  signal: AbortSignal,
) => Promise<GameResourcePage>

export interface GameResourcesRequestErrorFields {
  readonly code?: string
  readonly status?: number
  readonly problemCode?: string
  readonly retryAfterSeconds?: number
}

export class GameResourcesRequestError extends Error {
  declare readonly code?: string
  declare readonly status?: number
  declare readonly problemCode?: string
  declare readonly retryAfterSeconds?: number

  constructor(message: string, fields: GameResourcesRequestErrorFields = {}) {
    super(message)
    this.name = 'GameResourcesRequestError'
    for (const [field, value] of Object.entries(fields)) {
      if (value !== undefined)
        Object.defineProperty(this, field, { enumerable: true, value })
    }
  }
}

const pageKeys = Object.freeze([
  'catalogVersion',
  'gameVersion',
  'observedAtUtc',
  'total',
  'page',
  'pageSize',
  'warnings',
  'items',
])

const itemKeys = Object.freeze([
  'resourceId',
  'numericId',
  'internalName',
  'localizedName',
  'kind',
  'visibility',
  'maxStack',
  'hasQuality',
  'iconStatus',
  'iconTintHex',
])

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length
    && actual.every((key, index) => key === expected[index])
}

function nonBlankString(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '')
    throw new TypeError('string')
  return value
}

function nullableNonBlankString(value: unknown): string | null {
  return value === null ? null : nonBlankString(value)
}

function integer(value: unknown, minimum: number, maximum = Number.MAX_SAFE_INTEGER): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < minimum || value > maximum)
    throw new TypeError('integer')
  return value
}

function nullableInteger(value: unknown, minimum: number): number | null {
  return value === null ? null : integer(value, minimum)
}

function nullableBoolean(value: unknown): boolean | null {
  if (value !== null && typeof value !== 'boolean')
    throw new TypeError('boolean')
  return value
}

function utcTimestamp(value: unknown): string {
  const timestamp = nonBlankString(value)
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:Z|[+-]00:00)$/.exec(timestamp)
  if (match === null || !Number.isFinite(Date.parse(timestamp)))
    throw new TypeError('timestamp')
  const normalized = timestamp.endsWith('Z') ? timestamp : `${timestamp.slice(0, -6)}Z`
  const date = new Date(normalized)
  const [, year, month, day, hour, minute, second, fraction] = match
  const millisecond = Number((fraction ?? '').padEnd(3, '0').slice(0, 3) || 0)
  if (date.getUTCFullYear() !== Number(year)
    || date.getUTCMonth() + 1 !== Number(month)
    || date.getUTCDate() !== Number(day)
    || date.getUTCHours() !== Number(hour)
    || date.getUTCMinutes() !== Number(minute)
    || date.getUTCSeconds() !== Number(second)
    || date.getUTCMilliseconds() !== millisecond) {
    throw new TypeError('timestamp')
  }
  return timestamp
}

function parseItem(value: unknown): GameResourceItem {
  if (!isRecord(value) || !hasExactKeys(value, itemKeys))
    throw new TypeError('item')
  if (value.kind !== 'item' && value.kind !== 'block')
    throw new TypeError('kind')
  if (value.visibility !== 'public' && value.visibility !== 'hidden')
    throw new TypeError('visibility')
  if (value.iconStatus !== 'available' && value.iconStatus !== 'missing' && value.iconStatus !== 'invalid')
    throw new TypeError('icon status')
  if (value.iconTintHex !== null && (typeof value.iconTintHex !== 'string' || !/^[0-9A-F]{6}$/.test(value.iconTintHex)))
    throw new TypeError('icon tint')

  return Object.freeze({
    resourceId: nonBlankString(value.resourceId),
    numericId: integer(value.numericId, 0),
    internalName: nonBlankString(value.internalName),
    localizedName: nullableNonBlankString(value.localizedName),
    kind: value.kind,
    visibility: value.visibility,
    maxStack: nullableInteger(value.maxStack, 1),
    hasQuality: nullableBoolean(value.hasQuality),
    iconStatus: value.iconStatus,
    iconTintHex: value.iconTintHex,
  })
}

export function parseGameResourcePage(value: unknown): GameResourcePage {
  try {
    if (!isRecord(value) || !hasExactKeys(value, pageKeys)
      || !Array.isArray(value.warnings)
      || !Array.isArray(value.items)) {
      throw new TypeError('page')
    }
    const warnings = value.warnings.map(nonBlankString)
    const items = value.items.map(parseItem)
    return Object.freeze({
      catalogVersion: nonBlankString(value.catalogVersion),
      gameVersion: nullableNonBlankString(value.gameVersion),
      observedAtUtc: utcTimestamp(value.observedAtUtc),
      total: integer(value.total, 0),
      page: integer(value.page, 1, 100_000),
      pageSize: integer(value.pageSize, 1, 100),
      warnings: Object.freeze(warnings),
      items: Object.freeze(items),
    })
  }
  catch {
    throw new GameResourcesRequestError(
      'Invalid game resource page response',
      { code: 'invalid' },
    )
  }
}

export function createGameResourcesLoader(request: GameResourcesRequest): LoadGameResources {
  return async (query, signal) => parseGameResourcePage(await request(query, signal))
}
