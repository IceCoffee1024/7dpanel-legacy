import { isValidUtcTimestamp } from '../../players/api/playerSnapshot'

export const MAX_AREA_INVESTIGATION_DAYS = 30
export const DEFAULT_AREA_INVESTIGATION_LIMIT = 250
export const MAX_AREA_INVESTIGATION_LIMIT = 1000
export const AREA_INVESTIGATION_URL_KEYS = [
  'areaShape',
  'areaMinimumX',
  'areaMinimumZ',
  'areaMaximumX',
  'areaMaximumZ',
  'areaCenterX',
  'areaCenterZ',
  'areaRadius',
  'areaFrom',
  'areaTo',
] as const

export interface AreaRectangle {
  readonly kind: 'rectangle'
  readonly minimumX: number
  readonly minimumZ: number
  readonly maximumX: number
  readonly maximumZ: number
}

export interface AreaCircle {
  readonly kind: 'circle'
  readonly centerX: number
  readonly centerZ: number
  readonly radius: number
}

export type AreaGeometry = AreaRectangle | AreaCircle

export interface AreaInvestigationUrlState {
  readonly geometry: AreaGeometry | null
  readonly fromUtc: string | null
  readonly toUtc: string | null
}

export interface AreaInvestigationQuery {
  readonly geometry: AreaGeometry
  readonly fromUtc: string
  readonly toUtc: string
  readonly limit: number
}

export interface MatchingObservationTime {
  readonly observedAtUtc: string
}

export interface MatchingObservationPosition {
  readonly observedAtUtc: string
  readonly position: Readonly<{ x: number, y: number, z: number }>
}

export interface AreaInvestigationPlayer {
  readonly combinedId: string
  readonly displayName: string
  readonly firstMatchingObservation: MatchingObservationTime
  readonly lastMatchingObservation: MatchingObservationPosition
  readonly matchingObservationCount: number
}

export interface AreaInvestigationResponse {
  readonly players: readonly AreaInvestigationPlayer[]
  readonly candidateObservationCount: number
  readonly matchingObservationCount: number
  readonly truncated: boolean
  readonly truncation: Readonly<{
    candidateObservations: boolean
    playerResults: boolean
  }>
}

const MAX_RANGE_MS = MAX_AREA_INVESTIGATION_DAYS * 24 * 60 * 60 * 1000

function finiteNumber(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value))
    throw new Error('number')
  return value
}

function finiteQueryNumber(value: string | null): number {
  if (value === null || value.trim() === '')
    throw new Error('number')
  return finiteNumber(Number(value))
}

function nonNegativeInteger(value: unknown, maximum = Number.MAX_SAFE_INTEGER): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0 || value > maximum)
    throw new Error('integer')
  return value
}

export function positiveInteger(value: unknown, maximum = Number.MAX_SAFE_INTEGER): number {
  const parsed = nonNegativeInteger(value, maximum)
  if (parsed === 0)
    throw new Error('positive integer')
  return parsed
}

function utcTimestamp(value: unknown): string {
  if (typeof value !== 'string' || !isValidUtcTimestamp(value))
    throw new Error('timestamp')
  return value
}

export function validateTimeRange(fromUtc: string, toUtc: string): void {
  utcTimestamp(fromUtc)
  utcTimestamp(toUtc)
  const from = Date.parse(fromUtc)
  const to = Date.parse(toUtc)
  if (to < from || to - from > MAX_RANGE_MS)
    throw new Error('range')
}

export function rectangle(minimumX: number, minimumZ: number, maximumX: number, maximumZ: number): AreaRectangle {
  finiteNumber(minimumX)
  finiteNumber(minimumZ)
  finiteNumber(maximumX)
  finiteNumber(maximumZ)
  if (maximumX <= minimumX || maximumZ <= minimumZ)
    throw new Error('rectangle')
  return Object.freeze({ kind: 'rectangle', minimumX, minimumZ, maximumX, maximumZ })
}

export function circle(centerX: number, centerZ: number, radius: number): AreaCircle {
  finiteNumber(centerX)
  finiteNumber(centerZ)
  finiteNumber(radius)
  if (radius <= 0 || !Number.isFinite(centerX - radius) || !Number.isFinite(centerX + radius)
    || !Number.isFinite(centerZ - radius) || !Number.isFinite(centerZ + radius)
    || !Number.isFinite(radius * radius)) {
    throw new Error('circle')
  }
  return Object.freeze({ kind: 'circle', centerX, centerZ, radius })
}

function validateQuery(query: AreaInvestigationQuery): void {
  if (query.geometry.kind === 'rectangle') {
    rectangle(query.geometry.minimumX, query.geometry.minimumZ, query.geometry.maximumX, query.geometry.maximumZ)
  }
  else if (query.geometry.kind === 'circle') {
    circle(query.geometry.centerX, query.geometry.centerZ, query.geometry.radius)
  }
  else {
    throw new Error('geometry')
  }
  validateTimeRange(query.fromUtc, query.toUtc)
  positiveInteger(query.limit, MAX_AREA_INVESTIGATION_LIMIT)
}

export function areaInvestigationPath(query: AreaInvestigationQuery): string {
  try {
    validateQuery(query)
    const parameters = new URLSearchParams({ shape: query.geometry.kind })
    if (query.geometry.kind === 'rectangle') {
      parameters.set('minimumX', String(query.geometry.minimumX))
      parameters.set('minimumZ', String(query.geometry.minimumZ))
      parameters.set('maximumX', String(query.geometry.maximumX))
      parameters.set('maximumZ', String(query.geometry.maximumZ))
    }
    else {
      parameters.set('centerX', String(query.geometry.centerX))
      parameters.set('centerZ', String(query.geometry.centerZ))
      parameters.set('radius', String(query.geometry.radius))
    }
    parameters.set('fromUtc', query.fromUtc)
    parameters.set('toUtc', query.toUtc)
    parameters.set('limit', String(query.limit))
    return `/api/v1/map/players/area?${parameters.toString()}`
  }
  catch {
    throw new Error('Invalid area investigation query')
  }
}

function parseUrlGeometry(query: URLSearchParams): AreaGeometry | null {
  const shape = query.get('areaShape')
  const rectangleKeys = ['areaMinimumX', 'areaMinimumZ', 'areaMaximumX', 'areaMaximumZ'] as const
  const circleKeys = ['areaCenterX', 'areaCenterZ', 'areaRadius'] as const
  if (shape === 'rectangle') {
    if (circleKeys.some(key => query.has(key)) || rectangleKeys.some(key => !query.has(key)))
      return null
    return rectangle(
      finiteQueryNumber(query.get('areaMinimumX')),
      finiteQueryNumber(query.get('areaMinimumZ')),
      finiteQueryNumber(query.get('areaMaximumX')),
      finiteQueryNumber(query.get('areaMaximumZ')),
    )
  }
  if (shape === 'circle') {
    if (rectangleKeys.some(key => query.has(key)) || circleKeys.some(key => !query.has(key)))
      return null
    return circle(
      finiteQueryNumber(query.get('areaCenterX')),
      finiteQueryNumber(query.get('areaCenterZ')),
      finiteQueryNumber(query.get('areaRadius')),
    )
  }
  return null
}

export function restoreAreaInvestigationUrlState(query: URLSearchParams): AreaInvestigationUrlState {
  let geometry: AreaGeometry | null = null
  let fromUtc: string | null = null
  let toUtc: string | null = null
  try {
    geometry = parseUrlGeometry(query)
  }
  catch {}
  try {
    const candidateFrom = query.get('areaFrom')
    const candidateTo = query.get('areaTo')
    if (candidateFrom !== null && candidateTo !== null) {
      validateTimeRange(candidateFrom, candidateTo)
      fromUtc = candidateFrom
      toUtc = candidateTo
    }
  }
  catch {}
  return Object.freeze({ geometry, fromUtc, toUtc })
}

export function serializeAreaInvestigationUrlState(state: AreaInvestigationUrlState): URLSearchParams {
  const query = new URLSearchParams()
  if (state.geometry !== null) {
    query.set('areaShape', state.geometry.kind)
    if (state.geometry.kind === 'rectangle') {
      query.set('areaMinimumX', String(state.geometry.minimumX))
      query.set('areaMinimumZ', String(state.geometry.minimumZ))
      query.set('areaMaximumX', String(state.geometry.maximumX))
      query.set('areaMaximumZ', String(state.geometry.maximumZ))
    }
    else {
      query.set('areaCenterX', String(state.geometry.centerX))
      query.set('areaCenterZ', String(state.geometry.centerZ))
      query.set('areaRadius', String(state.geometry.radius))
    }
  }
  if (state.fromUtc !== null && state.toUtc !== null) {
    validateTimeRange(state.fromUtc, state.toUtc)
    query.set('areaFrom', state.fromUtc)
    query.set('areaTo', state.toUtc)
  }
  return query
}
