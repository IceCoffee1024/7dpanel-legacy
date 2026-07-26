import type { GameResourceKind, GameResourceLanguage, GameResourceRequestQuery } from '../api/gameResources'

export type GameResourceKindFilter = 'all' | GameResourceKind

export interface GameResourceFilters {
  readonly search: string
  readonly kind: GameResourceKindFilter
  readonly includeHidden: boolean
  readonly page: number
  readonly pageSize: number
}

export type GameResourceRouteQuery = Readonly<Record<string, unknown>>
export type GameResourceRouteQueryOutput = Record<string, string>

const DEFAULT_PAGE = 1
const DEFAULT_PAGE_SIZE = 50

function firstString(value: unknown): string | null {
  if (typeof value === 'string')
    return value
  if (Array.isArray(value) && typeof value[0] === 'string')
    return value[0]
  return null
}

function boundedInteger(value: unknown, minimum: number, maximum: number, fallback: number): number {
  const text = firstString(value)
  if (text === null || !/^\d+$/.test(text))
    return fallback
  const parsed = Number(text)
  return Number.isSafeInteger(parsed) && parsed >= minimum && parsed <= maximum
    ? parsed
    : fallback
}

export function restoreGameResourceFilters(
  query: GameResourceRouteQuery,
  isOwner: boolean,
): GameResourceFilters {
  const rawSearch = firstString(query.search)?.trim() ?? ''
  const search = rawSearch.length <= 100 ? rawSearch : ''
  const rawKind = firstString(query.kind)
  const kind: GameResourceKindFilter = rawKind === 'item' || rawKind === 'block'
    ? rawKind
    : 'all'
  return Object.freeze({
    search,
    kind,
    includeHidden: isOwner && firstString(query.includeHidden) === 'true',
    page: boundedInteger(query.page, 1, 100_000, DEFAULT_PAGE),
    pageSize: boundedInteger(query.pageSize, 1, 100, DEFAULT_PAGE_SIZE),
  })
}

export function gameResourceFiltersToRouteQuery(filters: GameResourceFilters): GameResourceRouteQueryOutput {
  const query: GameResourceRouteQueryOutput = {}
  if (filters.search !== '')
    query.search = filters.search
  if (filters.kind !== 'all')
    query.kind = filters.kind
  if (filters.includeHidden)
    query.includeHidden = 'true'
  if (filters.page !== DEFAULT_PAGE)
    query.page = String(filters.page)
  if (filters.pageSize !== DEFAULT_PAGE_SIZE)
    query.pageSize = String(filters.pageSize)
  return query
}

export function normalizeGameResourceLanguage(locale: string): GameResourceLanguage {
  return locale === 'zh-CN' ? 'zh-CN' : 'en'
}

export function toGameResourceRequestQuery(
  filters: GameResourceFilters,
  language: GameResourceLanguage,
): GameResourceRequestQuery {
  return Object.freeze({
    ...(filters.search === '' ? {} : { search: filters.search }),
    ...(filters.kind === 'all' ? {} : { kind: filters.kind }),
    includeHidden: filters.includeHidden,
    language,
    page: filters.page,
    pageSize: filters.pageSize,
  })
}
