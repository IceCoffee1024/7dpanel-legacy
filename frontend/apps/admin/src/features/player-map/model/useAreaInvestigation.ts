import type { ComputedRef, DeepReadonly, ShallowRef } from 'vue'

import { computed, readonly, shallowRef } from 'vue'

import { requestJson } from '../../../shared/api/http'
import { isRecord, isValidUtcTimestamp } from '../../players/api/playerSnapshot'

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

export type AreaInvestigationState = 'idle' | 'loading' | 'ready' | 'empty' | 'truncated' | 'failed'

type AreaInvestigationRequest = (
  authorizationHeader: string,
  query: AreaInvestigationQuery,
  signal: AbortSignal,
) => Promise<AreaInvestigationResponse>

export interface CreateAreaInvestigationControllerOptions {
  readonly authorizationHeader: () => string | null
  readonly initialQuery?: URLSearchParams
  readonly replaceQuery?: (query: URLSearchParams) => void
  readonly request?: AreaInvestigationRequest
  readonly limit?: number
}

export interface AreaInvestigationController {
  readonly state: DeepReadonly<ShallowRef<AreaInvestigationState>>
  readonly geometry: DeepReadonly<ShallowRef<AreaGeometry | null>>
  readonly timeRange: DeepReadonly<ShallowRef<Readonly<{ fromUtc: string, toUtc: string }> | null>>
  readonly players: ComputedRef<readonly AreaInvestigationPlayer[]>
  readonly truncated: ComputedRef<boolean>
  readonly truncation: ComputedRef<AreaInvestigationResponse['truncation']>
  readonly candidateObservationCount: ComputedRef<number>
  readonly matchingObservationCount: ComputedRef<number>
  readonly selectedCombinedId: DeepReadonly<ShallowRef<string | null>>
  readonly selectedPlayer: ComputedRef<AreaInvestigationPlayer | null>
  readonly error: DeepReadonly<ShallowRef<string | null>>
  readonly limit: DeepReadonly<ShallowRef<number>>
  setRectangle: (minimumX: number, minimumZ: number, maximumX: number, maximumZ: number) => void
  setCircle: (centerX: number, centerZ: number, radius: number) => void
  setTimeRange: (fromUtc: string, toUtc: string) => void
  setLimit: (limit: number) => void
  clear: () => void
  cancel: () => void
  search: () => Promise<void>
  selectResult: (combinedId: string | null) => void
}

const EMPTY_PLAYERS: readonly AreaInvestigationPlayer[] = Object.freeze([])
const NO_TRUNCATION = Object.freeze({ candidateObservations: false, playerResults: false })
const MAX_RANGE_MS = MAX_AREA_INVESTIGATION_DAYS * 24 * 60 * 60 * 1000

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

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

function nonBlankString(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '')
    throw new Error('string')
  return value
}

function nonNegativeInteger(value: unknown, maximum = Number.MAX_SAFE_INTEGER): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0 || value > maximum)
    throw new Error('integer')
  return value
}

function positiveInteger(value: unknown, maximum = Number.MAX_SAFE_INTEGER): number {
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

function validateTimeRange(fromUtc: string, toUtc: string): void {
  utcTimestamp(fromUtc)
  utcTimestamp(toUtc)
  const from = Date.parse(fromUtc)
  const to = Date.parse(toUtc)
  if (to < from || to - from > MAX_RANGE_MS)
    throw new Error('range')
}

function rectangle(minimumX: number, minimumZ: number, maximumX: number, maximumZ: number): AreaRectangle {
  finiteNumber(minimumX)
  finiteNumber(minimumZ)
  finiteNumber(maximumX)
  finiteNumber(maximumZ)
  if (maximumX <= minimumX || maximumZ <= minimumZ)
    throw new Error('rectangle')
  return Object.freeze({ kind: 'rectangle', minimumX, minimumZ, maximumX, maximumZ })
}

function circle(centerX: number, centerZ: number, radius: number): AreaCircle {
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

function parsePlayer(value: unknown): AreaInvestigationPlayer {
  if (!isRecord(value) || !hasExactKeys(value, [
    'crossplatformId',
    'displayName',
    'firstHitUtc',
    'lastHitUtc',
    'hitObservationCount',
    'lastPosition',
  ]) || !isRecord(value.lastPosition) || !hasExactKeys(value.lastPosition, ['x', 'y', 'z'])) {
    throw new Error('player')
  }
  const firstHitUtc = utcTimestamp(value.firstHitUtc)
  const lastHitUtc = utcTimestamp(value.lastHitUtc)
  if (Date.parse(firstHitUtc) > Date.parse(lastHitUtc))
    throw new Error('observation order')
  const position = Object.freeze({
    x: finiteNumber(value.lastPosition.x),
    y: finiteNumber(value.lastPosition.y),
    z: finiteNumber(value.lastPosition.z),
  })
  return Object.freeze({
    combinedId: nonBlankString(value.crossplatformId),
    displayName: nonBlankString(value.displayName),
    firstMatchingObservation: Object.freeze({ observedAtUtc: firstHitUtc }),
    lastMatchingObservation: Object.freeze({ observedAtUtc: lastHitUtc, position }),
    matchingObservationCount: positiveInteger(value.hitObservationCount),
  })
}

export function parseAreaInvestigationResponse(value: unknown): AreaInvestigationResponse {
  try {
    if (!isRecord(value) || !hasExactKeys(value, [
      'hits',
      'candidateObservationCount',
      'matchingObservationCount',
      'candidateObservationLimitReached',
      'playerResultLimitReached',
    ]) || !Array.isArray(value.hits) || value.hits.length > MAX_AREA_INVESTIGATION_LIMIT
    || typeof value.candidateObservationLimitReached !== 'boolean'
    || typeof value.playerResultLimitReached !== 'boolean') {
      throw new Error('shape')
    }
    const players = value.hits.map(parsePlayer)
    const combinedIds = new Set(players.map(player => player.combinedId))
    if (combinedIds.size !== players.length)
      throw new Error('duplicate player')
    const candidateObservationCount = nonNegativeInteger(value.candidateObservationCount, 20_000)
    const matchingObservationCount = nonNegativeInteger(value.matchingObservationCount, candidateObservationCount)
    const representedMatchingObservations = players.reduce((total, player) => total + player.matchingObservationCount, 0)
    if (representedMatchingObservations > matchingObservationCount
      || (!value.playerResultLimitReached && representedMatchingObservations !== matchingObservationCount)) {
      throw new Error('observation count')
    }
    const truncation = Object.freeze({
      candidateObservations: value.candidateObservationLimitReached,
      playerResults: value.playerResultLimitReached,
    })
    return Object.freeze({
      players: Object.freeze(players),
      candidateObservationCount,
      matchingObservationCount,
      truncated: truncation.candidateObservations || truncation.playerResults,
      truncation,
    })
  }
  catch {
    throw new Error('Invalid area investigation response')
  }
}

export async function fetchAreaInvestigation(
  authorizationHeader: string,
  query: AreaInvestigationQuery,
  signal: AbortSignal,
): Promise<AreaInvestigationResponse> {
  const value = await requestJson<unknown>(areaInvestigationPath(query), {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseAreaInvestigationResponse(value)
}

export function createAreaInvestigationController(
  options: CreateAreaInvestigationControllerOptions,
): AreaInvestigationController {
  const restored = restoreAreaInvestigationUrlState(options.initialQuery ?? new URLSearchParams())
  const geometry = shallowRef<AreaGeometry | null>(restored.geometry)
  const timeRange = shallowRef<Readonly<{ fromUtc: string, toUtc: string }> | null>(
    restored.fromUtc !== null && restored.toUtc !== null
      ? Object.freeze({ fromUtc: restored.fromUtc, toUtc: restored.toUtc })
      : null,
  )
  const response = shallowRef<AreaInvestigationResponse | null>(null)
  const selectedCombinedId = shallowRef<string | null>(null)
  const state = shallowRef<AreaInvestigationState>('idle')
  const error = shallowRef<string | null>(null)
  const players = computed(() => response.value?.players ?? EMPTY_PLAYERS)
  const truncated = computed(() => response.value?.truncated ?? false)
  const truncation = computed(() => response.value?.truncation ?? NO_TRUNCATION)
  const candidateObservationCount = computed(() => response.value?.candidateObservationCount ?? 0)
  const matchingObservationCount = computed(() => response.value?.matchingObservationCount ?? 0)
  const selectedPlayer = computed(() =>
    players.value.find(player => player.combinedId === selectedCombinedId.value) ?? null)
  const request = options.request ?? fetchAreaInvestigation
  const replaceQuery = options.replaceQuery ?? (() => {})
  const limit = shallowRef(options.limit ?? DEFAULT_AREA_INVESTIGATION_LIMIT)
  positiveInteger(limit.value, MAX_AREA_INVESTIGATION_LIMIT)
  let activeController: AbortController | null = null
  let sequence = 0

  function syncUrl() {
    replaceQuery(serializeAreaInvestigationUrlState({
      geometry: geometry.value,
      fromUtc: timeRange.value?.fromUtc ?? null,
      toUtc: timeRange.value?.toUtc ?? null,
    }))
  }

  function stateForResponse(): AreaInvestigationState {
    if (response.value === null)
      return 'idle'
    if (response.value.truncated)
      return 'truncated'
    return response.value.players.length === 0 ? 'empty' : 'ready'
  }

  function abortActive() {
    sequence++
    activeController?.abort()
    activeController = null
  }

  function resetResults() {
    response.value = null
    selectedCombinedId.value = null
    error.value = null
    state.value = 'idle'
  }

  function changeGeometry(nextGeometry: AreaGeometry) {
    abortActive()
    geometry.value = nextGeometry
    resetResults()
    syncUrl()
  }

  function setRectangle(minimumX: number, minimumZ: number, maximumX: number, maximumZ: number) {
    changeGeometry(rectangle(minimumX, minimumZ, maximumX, maximumZ))
  }

  function setCircle(centerX: number, centerZ: number, radius: number) {
    changeGeometry(circle(centerX, centerZ, radius))
  }

  function setTimeRange(fromUtc: string, toUtc: string) {
    validateTimeRange(fromUtc, toUtc)
    abortActive()
    timeRange.value = Object.freeze({ fromUtc, toUtc })
    resetResults()
    syncUrl()
  }

  function setLimit(nextLimit: number) {
    positiveInteger(nextLimit, MAX_AREA_INVESTIGATION_LIMIT)
    if (limit.value === nextLimit)
      return
    abortActive()
    limit.value = nextLimit
    resetResults()
  }

  function cancel() {
    abortActive()
    state.value = stateForResponse()
  }

  function clear() {
    abortActive()
    geometry.value = null
    resetResults()
    syncUrl()
  }

  async function search(): Promise<void> {
    abortActive()
    if (geometry.value === null || timeRange.value === null) {
      error.value = 'An area and UTC time range are required'
      state.value = 'failed'
      return
    }
    const authorization = options.authorizationHeader()
    if (authorization === null) {
      error.value = 'Area matching observations could not be loaded'
      state.value = 'failed'
      return
    }
    const controller = new AbortController()
    activeController = controller
    const requestSequence = ++sequence
    state.value = 'loading'
    error.value = null
    try {
      const result = await request(authorization, {
        geometry: geometry.value,
        fromUtc: timeRange.value.fromUtc,
        toUtc: timeRange.value.toUtc,
        limit: limit.value,
      }, controller.signal)
      if (controller.signal.aborted || requestSequence !== sequence)
        return
      response.value = result
      selectedCombinedId.value = null
      state.value = stateForResponse()
    }
    catch {
      if (controller.signal.aborted || requestSequence !== sequence)
        return
      error.value = 'Area matching observations could not be loaded'
      state.value = response.value === null ? 'failed' : stateForResponse()
    }
    finally {
      if (activeController === controller)
        activeController = null
    }
  }

  function selectResult(combinedId: string | null) {
    selectedCombinedId.value = combinedId !== null
      && players.value.some(player => player.combinedId === combinedId)
      ? combinedId
      : null
  }

  return {
    state: readonly(state),
    geometry: readonly(geometry),
    timeRange: readonly(timeRange),
    players,
    truncated,
    truncation,
    candidateObservationCount,
    matchingObservationCount,
    selectedCombinedId: readonly(selectedCombinedId),
    selectedPlayer,
    error: readonly(error),
    limit: readonly(limit),
    setRectangle,
    setCircle,
    setTimeRange,
    setLimit,
    clear,
    cancel,
    search,
    selectResult,
  }
}

export const useAreaInvestigation = createAreaInvestigationController
