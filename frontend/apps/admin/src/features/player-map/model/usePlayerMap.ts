import type { ComputedRef, DeepReadonly, ShallowRef } from 'vue'
import type { LocationQueryRaw } from 'vue-router'
import type { FetchHistoricalPlayersOptions, HistoricalPlayerDetails, HistoricalPlayersPage, HistoricalPlayerSummary } from '../../players/api/historyPlayers'
import type { OnlinePlayersSnapshot } from '../../players/api/onlinePlayers'
import type { MapGameTime, MapGameTimeEnvelope, MapMetadata, MapMetadataEnvelope, PlayerTrack, PlayerTrackFilters } from '../api/playerMap'

import { computed, onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { fetchHistoricalPlayer, fetchHistoricalPlayers } from '../../players/api/historyPlayers'
import { fetchOnlinePlayers } from '../../players/api/onlinePlayers'
import { isValidUtcTimestamp } from '../../players/api/playerSnapshot'
import { fetchMapGameTime, fetchMapMetadata, fetchPlayerTrack } from '../api/playerMap'
import { toMapCoordinate } from './mapProjection'

export type PlayerMapPageState = 'loading' | 'ready' | 'empty' | 'partial' | 'stale' | 'forbidden' | 'failed'
export type GameTimeState = 'loading' | 'ready' | 'stale' | 'unavailable'
export type PlayerMapDataState = 'loading' | 'ready' | 'empty' | 'stale' | 'forbidden' | 'failed'

export interface PlayerMapFilters {
  readonly player: string | null
  readonly fromUtc: string | null
  readonly toUtc: string | null
}

export interface OnlineMapPlayer {
  readonly combinedId: string
  readonly name: string
  readonly position: Readonly<{ x: number, y: number, z: number }>
  readonly observedAtUtc: string
}

export interface FitRequest {
  readonly queryKey: string
  readonly extent: readonly [number, number, number, number]
}

export interface PlayerMapVisibility {
  isVisible: () => boolean
  subscribe: (listener: () => void) => () => void
}

type FetchMetadata = (authorizationHeader: string, signal?: AbortSignal) => Promise<MapMetadataEnvelope>
type FetchGameTime = (authorizationHeader: string, signal?: AbortSignal) => Promise<MapGameTimeEnvelope>
type FetchOnline = (authorizationHeader: string, signal?: AbortSignal) => Promise<OnlinePlayersSnapshot>
type FetchPlayers = (
  authorizationHeader: string,
  options: FetchHistoricalPlayersOptions,
  signal?: AbortSignal,
) => Promise<HistoricalPlayersPage>
type FetchPlayer = (authorizationHeader: string, crossplatformId: string, signal?: AbortSignal) => Promise<HistoricalPlayerDetails>
type FetchTrack = (authorizationHeader: string, filters: PlayerTrackFilters, signal?: AbortSignal) => Promise<PlayerTrack>

export interface CreatePlayerMapControllerOptions {
  authorizationHeader: () => string | null
  initialQuery?: URLSearchParams
  replaceQuery?: (query: URLSearchParams) => void
  fetchMetadata?: FetchMetadata
  fetchGameTime?: FetchGameTime
  fetchOnline?: FetchOnline
  fetchPlayers?: FetchPlayers
  fetchPlayer?: FetchPlayer
  fetchTrack?: FetchTrack
  visibility?: PlayerMapVisibility
}

export interface PlayerMapController {
  state: DeepReadonly<ShallowRef<PlayerMapPageState>>
  metadata: DeepReadonly<ShallowRef<MapMetadata | null>>
  onlinePlayers: DeepReadonly<ShallowRef<readonly OnlineMapPlayer[]>>
  onlineState: DeepReadonly<ShallowRef<PlayerMapDataState>>
  historicalPlayers: DeepReadonly<ShallowRef<readonly HistoricalPlayerSummary[]>>
  historyState: DeepReadonly<ShallowRef<PlayerMapDataState>>
  playerSearch: ShallowRef<string>
  track: DeepReadonly<ShallowRef<PlayerTrack | null>>
  trackState: DeepReadonly<ShallowRef<PlayerMapDataState>>
  observationCount: ComputedRef<number>
  gameTime: DeepReadonly<ShallowRef<MapGameTime | null>>
  gameTimeState: DeepReadonly<ShallowRef<GameTimeState>>
  filters: DeepReadonly<ShallowRef<PlayerMapFilters>>
  selectedSnapshotId: DeepReadonly<ShallowRef<number | null>>
  fitRequest: DeepReadonly<ShallowRef<FitRequest | null>>
  setPlayer: (player: string | null) => void
  setRange: (fromUtc: string | null, toUtc: string | null) => void
  searchHistoricalPlayers: (query: string) => Promise<void>
  selectObservation: (snapshotId: number | null) => void
  refresh: () => Promise<void>
  refreshTrack: () => Promise<void>
  start: () => void
  dispose: () => void
}

const browserVisibility: PlayerMapVisibility = {
  isVisible: () => document.visibilityState === 'visible',
  subscribe(listener) {
    document.addEventListener('visibilitychange', listener)
    return () => document.removeEventListener('visibilitychange', listener)
  },
}

function restoredFilters(query: URLSearchParams): PlayerMapFilters {
  const player = query.get('player')?.trim() || null
  const fromUtc = query.get('from')
  const toUtc = query.get('to')
  const validRange = fromUtc !== null && toUtc !== null
    && isValidUtcTimestamp(fromUtc) && isValidUtcTimestamp(toUtc)
    && Date.parse(fromUtc) <= Date.parse(toUtc)
  return Object.freeze({
    player,
    fromUtc: validRange ? fromUtc : null,
    toUtc: validRange ? toUtc : null,
  })
}

function restoredObservation(query: URLSearchParams): number | null {
  const value = Number(query.get('observation'))
  return Number.isSafeInteger(value) && value > 0 ? value : null
}

function isForbidden(error: unknown): boolean {
  return error instanceof HttpError && error.status === 403
}

function isAborted(error: unknown): boolean {
  return error instanceof HttpError && error.code === 'aborted'
}

function trackQueryKey(filters: PlayerTrackFilters): string {
  return `${filters.player}\n${filters.fromUtc}\n${filters.toUtc}`
}

function worldIdentity(value: MapMetadata): string {
  const { minimumX, minimumZ, maximumX, maximumZ } = value.extent
  return `${value.worldId}\n${minimumX}\n${minimumZ}\n${maximumX}\n${maximumZ}`
}

function fitExtent(track: PlayerTrack): readonly [number, number, number, number] | null {
  const coordinates = track.segments.flatMap(segment => segment.points.map(toMapCoordinate))
  if (coordinates.length === 0)
    return null
  const xs = coordinates.map(coordinate => coordinate[0] ?? 0)
  const ys = coordinates.map(coordinate => coordinate[1] ?? 0)
  let minX = Math.min(...xs)
  let minY = Math.min(...ys)
  let maxX = Math.max(...xs)
  let maxY = Math.max(...ys)
  if (minX === maxX) {
    minX -= 1
    maxX += 1
  }
  if (minY === maxY) {
    minY -= 1
    maxY += 1
  }
  return Object.freeze([minX, minY, maxX, maxY])
}

export function createPlayerMapController(options: CreatePlayerMapControllerOptions): PlayerMapController {
  const requestMetadata = options.fetchMetadata ?? fetchMapMetadata
  const requestGameTime = options.fetchGameTime ?? fetchMapGameTime
  const requestOnline = options.fetchOnline ?? fetchOnlinePlayers
  const requestPlayers = options.fetchPlayers ?? fetchHistoricalPlayers
  const requestPlayer = options.fetchPlayer ?? fetchHistoricalPlayer
  const requestTrack = options.fetchTrack ?? fetchPlayerTrack
  const visibility = options.visibility ?? browserVisibility
  const replaceQuery = options.replaceQuery ?? (() => {})

  const state = shallowRef<PlayerMapPageState>('loading')
  const metadata = shallowRef<MapMetadata | null>(null)
  const onlinePlayers = shallowRef<readonly OnlineMapPlayer[]>(Object.freeze([]))
  const onlineState = shallowRef<PlayerMapDataState>('loading')
  const historicalPlayers = shallowRef<readonly HistoricalPlayerSummary[]>(Object.freeze([]))
  const historyState = shallowRef<PlayerMapDataState>('loading')
  const playerSearch = shallowRef('')
  const track = shallowRef<PlayerTrack | null>(null)
  const trackState = shallowRef<PlayerMapDataState>('empty')
  const gameTime = shallowRef<MapGameTime | null>(null)
  const gameTimeState = shallowRef<GameTimeState>('loading')
  const filters = shallowRef<PlayerMapFilters>(restoredFilters(options.initialQuery ?? new URLSearchParams()))
  const selectedSnapshotId = shallowRef<number | null>(restoredObservation(options.initialQuery ?? new URLSearchParams()))
  const fitRequest = shallowRef<FitRequest | null>(null)
  const observationCount = computed(() => track.value?.segments.reduce((total, segment) => total + segment.points.length, 0) ?? 0)

  const fittedQueries = new Set<string>()
  let coreController: AbortController | null = null
  let timeController: AbortController | null = null
  let historyController: AbortController | null = null
  let trackController: AbortController | null = null
  let coreSequence = 0
  let historySequence = 0
  let trackSequence = 0
  let timer: ReturnType<typeof setInterval> | null = null
  let unsubscribeVisibility: (() => void) | null = null
  let started = false
  let disposed = false

  function syncUrl() {
    const query = new URLSearchParams()
    if (filters.value.player !== null)
      query.set('player', filters.value.player)
    if (filters.value.fromUtc !== null)
      query.set('from', filters.value.fromUtc)
    if (filters.value.toUtc !== null)
      query.set('to', filters.value.toUtc)
    if (selectedSnapshotId.value !== null)
      query.set('observation', String(selectedSnapshotId.value))
    replaceQuery(query)
  }

  function setPlayer(player: string | null) {
    trackController?.abort()
    trackSequence++
    track.value = null
    trackState.value = player === null ? 'empty' : 'loading'
    selectedSnapshotId.value = null
    filters.value = Object.freeze({ ...filters.value, player: player?.trim() || null })
    syncUrl()
  }

  function setRange(fromUtc: string | null, toUtc: string | null) {
    trackController?.abort()
    trackSequence++
    track.value = null
    trackState.value = filters.value.player === null ? 'empty' : 'loading'
    selectedSnapshotId.value = null
    filters.value = Object.freeze({ ...filters.value, fromUtc, toUtc })
    syncUrl()
  }

  function selectObservation(snapshotId: number | null) {
    selectedSnapshotId.value = snapshotId
    syncUrl()
  }

  function clearWorldBoundState(nextOnlineState: PlayerMapDataState = 'loading') {
    trackController?.abort()
    trackController = null
    trackSequence++
    onlinePlayers.value = Object.freeze([])
    onlineState.value = nextOnlineState
    track.value = null
    trackState.value = filters.value.player !== null && filters.value.fromUtc !== null && filters.value.toUtc !== null
      ? 'loading'
      : 'empty'
    selectedSnapshotId.value = null
    fitRequest.value = null
    fittedQueries.clear()
    syncUrl()
  }

  function updatePageState(failedCount: number, hadPreviousData: boolean) {
    if (metadata.value === null) {
      state.value = 'failed'
      return
    }
    if (failedCount > 0) {
      state.value = hadPreviousData ? 'stale' : 'partial'
      return
    }
    state.value = onlinePlayers.value.length === 0 && historicalPlayers.value.length === 0
      ? 'empty'
      : 'ready'
  }

  async function searchHistoricalPlayers(query: string): Promise<void> {
    if (disposed)
      return
    playerSearch.value = query
    const authorizationHeader = options.authorizationHeader()
    if (authorizationHeader === null) {
      historyState.value = historicalPlayers.value.length === 0 ? 'failed' : 'stale'
      return
    }
    historyController?.abort()
    const controller = new AbortController()
    historyController = controller
    const sequence = ++historySequence
    if (historicalPlayers.value.length === 0)
      historyState.value = 'loading'
    try {
      const trimmed = query.trim()
      const page = await requestPlayers(authorizationHeader, {
        query: trimmed === '' ? null : trimmed,
        pageSize: 50,
        cursor: null,
      }, controller.signal)
      if (disposed || sequence !== historySequence)
        return
      let players = [...page.players]
      const restoredPlayerId = filters.value.player
      if (restoredPlayerId !== null && !players.some(player => player.crossplatformId === restoredPlayerId)) {
        const restored = await requestPlayer(authorizationHeader, restoredPlayerId, controller.signal)
        if (disposed || sequence !== historySequence)
          return
        players = [restored.player, ...players]
      }
      historicalPlayers.value = Object.freeze(players)
      historyState.value = players.length === 0 ? 'empty' : 'ready'
    }
    catch (error) {
      if (disposed || sequence !== historySequence || isAborted(error))
        return
      if (isForbidden(error)) {
        historyState.value = 'forbidden'
        state.value = 'forbidden'
      }
      else {
        historyState.value = historicalPlayers.value.length === 0 ? 'failed' : 'stale'
      }
    }
    finally {
      if (historyController === controller)
        historyController = null
    }
  }

  async function refreshGameTime() {
    if (disposed || !visibility.isVisible())
      return
    const authorizationHeader = options.authorizationHeader()
    if (authorizationHeader === null) {
      gameTimeState.value = gameTime.value === null ? 'unavailable' : 'stale'
      return
    }
    timeController?.abort()
    const controller = new AbortController()
    timeController = controller
    try {
      const result = await requestGameTime(authorizationHeader, controller.signal)
      if (!disposed && timeController === controller) {
        if (result.availability === 'unavailable') {
          gameTime.value = null
          gameTimeState.value = 'unavailable'
        }
        else {
          gameTime.value = result
          gameTimeState.value = result.availability === 'stale' ? 'stale' : 'ready'
        }
      }
    }
    catch (error) {
      if (!disposed && timeController === controller && !isAborted(error))
        gameTimeState.value = gameTime.value === null ? 'unavailable' : 'stale'
    }
    finally {
      if (timeController === controller)
        timeController = null
    }
  }

  async function refresh(): Promise<void> {
    if (disposed || !visibility.isVisible())
      return
    const authorizationHeader = options.authorizationHeader()
    if (authorizationHeader === null) {
      state.value = 'failed'
      return
    }
    const hadPreviousData = metadata.value !== null || onlinePlayers.value.length > 0 || historicalPlayers.value.length > 0
    coreController?.abort()
    const controller = new AbortController()
    coreController = controller
    const sequence = ++coreSequence
    if (!hadPreviousData)
      state.value = 'loading'

    const historyRequest = searchHistoricalPlayers(playerSearch.value)
    const results = await Promise.allSettled([
      requestMetadata(authorizationHeader, controller.signal),
      requestOnline(authorizationHeader, controller.signal),
    ])
    await historyRequest
    if (disposed || sequence !== coreSequence)
      return
    if (results.some(result => result.status === 'rejected' && isForbidden(result.reason)) || historyState.value === 'forbidden') {
      onlineState.value = 'forbidden'
      historyState.value = 'forbidden'
      state.value = 'forbidden'
      return
    }
    let failedCount = 0
    let metadataWasStale = false
    let metadataUnavailable = false
    const metadataResult = results[0]
    if (metadataResult.status === 'fulfilled') {
      if (metadataResult.value.availability === 'unavailable') {
        clearWorldBoundState('empty')
        metadata.value = null
        metadataUnavailable = true
        failedCount++
      }
      else {
        if (metadata.value !== null && worldIdentity(metadata.value) !== worldIdentity(metadataResult.value))
          clearWorldBoundState()
        metadata.value = metadataResult.value
        if (metadataResult.value.availability === 'stale') {
          metadataWasStale = true
          failedCount++
        }
      }
    }
    else if (!isAborted(metadataResult.reason)) {
      failedCount++
    }
    const onlineResult = results[1]
    if (metadataUnavailable) {
      onlinePlayers.value = Object.freeze([])
      onlineState.value = 'empty'
    }
    else if (onlineResult.status === 'fulfilled') {
      onlinePlayers.value = Object.freeze(onlineResult.value.players.map(player => Object.freeze({
        combinedId: player.crossplatformIdentity?.combinedId ?? player.platformIdentity.combinedId,
        name: player.name,
        position: player.position,
        observedAtUtc: player.observedAtUtc,
      })))
      onlineState.value = onlinePlayers.value.length === 0 ? 'empty' : 'ready'
    }
    else if (!isAborted(onlineResult.reason)) {
      failedCount++
      onlineState.value = onlinePlayers.value.length === 0 ? 'failed' : 'stale'
    }
    if (historyState.value === 'failed' || historyState.value === 'stale')
      failedCount++
    updatePageState(failedCount, hadPreviousData)
    if (metadataWasStale)
      state.value = 'stale'
    coreController = null

    if (metadata.value !== null && visibility.isVisible()
      && filters.value.player !== null && filters.value.fromUtc !== null && filters.value.toUtc !== null) {
      await refreshTrack()
    }
  }

  async function refreshTrack(): Promise<void> {
    if (disposed)
      return
    const authorizationHeader = options.authorizationHeader()
    const current = filters.value
    if (authorizationHeader === null || current.player === null || current.fromUtc === null || current.toUtc === null)
      return
    const requestFilters: PlayerTrackFilters = {
      player: current.player,
      fromUtc: current.fromUtc,
      toUtc: current.toUtc,
    }
    const key = trackQueryKey(requestFilters)
    trackController?.abort()
    const controller = new AbortController()
    trackController = controller
    const sequence = ++trackSequence
    if (track.value === null)
      trackState.value = 'loading'
    try {
      const result = await requestTrack(authorizationHeader, requestFilters, controller.signal)
      if (disposed || sequence !== trackSequence)
        return
      track.value = result
      trackState.value = observationCount.value === 0 ? 'empty' : 'ready'
      const points = result.segments.flatMap(segment => segment.points)
      const selectedStillExists = selectedSnapshotId.value !== null
        && points.some(point => point.snapshotId === selectedSnapshotId.value)
      const nextSelectedSnapshotId = selectedStillExists
        ? selectedSnapshotId.value
        : (points[0]?.snapshotId ?? null)
      if (selectedSnapshotId.value !== nextSelectedSnapshotId) {
        selectedSnapshotId.value = nextSelectedSnapshotId
        syncUrl()
      }
      const extent = fitExtent(result)
      if (extent !== null && !fittedQueries.has(key)) {
        fittedQueries.add(key)
        fitRequest.value = Object.freeze({ queryKey: key, extent })
      }
      if (observationCount.value === 0)
        state.value = 'empty'
    }
    catch (error) {
      if (!disposed && sequence === trackSequence && !isAborted(error)) {
        if (isForbidden(error)) {
          trackState.value = 'forbidden'
          state.value = 'forbidden'
        }
        else {
          trackState.value = track.value === null ? 'failed' : 'stale'
          state.value = track.value === null ? 'partial' : 'stale'
        }
      }
    }
    finally {
      if (trackController === controller)
        trackController = null
    }
  }

  function clearTimer() {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
  }

  function startTimer() {
    clearTimer()
    if (!disposed && visibility.isVisible())
      timer = setInterval(() => void refreshGameTime(), 30_000)
  }

  function handleVisibility() {
    if (!visibility.isVisible()) {
      clearTimer()
      coreSequence++
      historySequence++
      trackSequence++
      coreController?.abort()
      timeController?.abort()
      historyController?.abort()
      trackController?.abort()
      return
    }
    startTimer()
    void refresh()
    void refreshGameTime()
  }

  function start() {
    if (started || disposed)
      return
    started = true
    unsubscribeVisibility = visibility.subscribe(handleVisibility)
    startTimer()
    void refresh()
    void refreshGameTime()
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    coreSequence++
    historySequence++
    trackSequence++
    coreController?.abort()
    timeController?.abort()
    historyController?.abort()
    trackController?.abort()
    coreController = null
    timeController = null
    historyController = null
    trackController = null
    clearTimer()
    unsubscribeVisibility?.()
    unsubscribeVisibility = null
  }

  return {
    state: readonly(state),
    metadata: readonly(metadata),
    onlinePlayers: readonly(onlinePlayers),
    onlineState: readonly(onlineState),
    historicalPlayers: readonly(historicalPlayers),
    historyState: readonly(historyState),
    playerSearch,
    track: readonly(track),
    trackState: readonly(trackState),
    observationCount,
    gameTime: readonly(gameTime),
    gameTimeState: readonly(gameTimeState),
    filters: readonly(filters),
    selectedSnapshotId: readonly(selectedSnapshotId),
    fitRequest: readonly(fitRequest),
    setPlayer,
    setRange,
    searchHistoricalPlayers,
    selectObservation,
    refresh,
    refreshTrack,
    start,
    dispose,
  }
}

export function usePlayerMap(): PlayerMapController {
  const auth = useAuthStore()
  const route = useRoute()
  const router = useRouter()
  const initialQuery = new URLSearchParams()
  for (const [key, value] of Object.entries(route.query)) {
    if (typeof value === 'string')
      initialQuery.set(key, value)
  }
  const controller = createPlayerMapController({
    authorizationHeader: () => auth.authorizationHeader,
    initialQuery,
    replaceQuery(query) {
      const next: LocationQueryRaw = {}
      for (const [key, value] of query)
        next[key] = value
      void router.replace({ query: next })
    },
  })
  onMounted(controller.start)
  onUnmounted(controller.dispose)
  return controller
}
