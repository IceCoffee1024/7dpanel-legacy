import type { ComputedRef, DeepReadonly, ShallowRef } from 'vue'
import type {
  AreaGeometry,
  AreaInvestigationPlayer,
  AreaInvestigationQuery,
  AreaInvestigationResponse,
} from './areaInvestigationProjection'

import { computed, readonly, shallowRef } from 'vue'

import { fetchAreaInvestigation } from './areaInvestigationAdapter'
import {
  circle,
  DEFAULT_AREA_INVESTIGATION_LIMIT,
  MAX_AREA_INVESTIGATION_LIMIT,
  positiveInteger,
  rectangle,
  restoreAreaInvestigationUrlState,
  serializeAreaInvestigationUrlState,
  validateTimeRange,
} from './areaInvestigationProjection'

export { fetchAreaInvestigation, parseAreaInvestigationResponse } from './areaInvestigationAdapter'
export type {
  AreaCircle,
  AreaGeometry,
  AreaInvestigationPlayer,
  AreaInvestigationQuery,
  AreaInvestigationResponse,
  AreaInvestigationUrlState,
  AreaRectangle,
  MatchingObservationPosition,
  MatchingObservationTime,
} from './areaInvestigationProjection'
export {
  AREA_INVESTIGATION_URL_KEYS,
  areaInvestigationPath,
  DEFAULT_AREA_INVESTIGATION_LIMIT,
  MAX_AREA_INVESTIGATION_DAYS,
  MAX_AREA_INVESTIGATION_LIMIT,
  restoreAreaInvestigationUrlState,
  serializeAreaInvestigationUrlState,
} from './areaInvestigationProjection'

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
