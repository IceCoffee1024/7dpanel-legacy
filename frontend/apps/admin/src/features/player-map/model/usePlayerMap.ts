import type { ComputedRef, DeepReadonly, ShallowRef } from 'vue'
import type { LocationQueryRaw } from 'vue-router'
import type { HistoricalPlayerSummary } from '../../players/api/historyPlayers'
import type { OnlinePlayersSnapshot } from '../../players/api/onlinePlayers'
import type { MapGameTime, MapMetadata, MapMetadataEnvelope, PlayerTrack, PlayerTrackFilters } from '../api/playerMap'
import type { FetchGameTime, FetchPlayer, FetchPlayers, PlayerMapDataOperations } from './playerMapDataOperations'
import type { PlayerMapVisibility } from './playerMapLifecycle'
import type { PlayerMapFilters, PlayerMapPageState } from './playerMapProjection'

import { computed, onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '../../auth'
import { fetchHistoricalPlayer, fetchHistoricalPlayers } from '../../players/api/historyPlayers'
import { fetchOnlinePlayers } from '../../players/api/onlinePlayers'
import { fetchMapGameTime, fetchMapMetadata, fetchPlayerTrack } from '../api/playerMap'
import { createPlayerMapDataOperations, isPlayerMapAborted, isPlayerMapForbidden } from './playerMapDataOperations'
import { createPlayerMapLifecycle } from './playerMapLifecycle'
import {
  mapPlayerMapPageState,
  playerMapWorldIdentity,
  playerTrackFitExtent,
  playerTrackQueryKey,
  restorePlayerMapFilters,
  restorePlayerMapObservation,
} from './playerMapProjection'

export type { PlayerMapVisibility } from './playerMapLifecycle'
export type { PlayerMapFilters, PlayerMapPageState } from './playerMapProjection'

export type GameTimeState = 'loading' | 'ready' | 'stale' | 'unavailable'
export type PlayerMapDataState = 'loading' | 'ready' | 'empty' | 'stale' | 'forbidden' | 'failed'

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

type FetchMetadata = (authorizationHeader: string, signal?: AbortSignal) => Promise<MapMetadataEnvelope>
type FetchOnline = (authorizationHeader: string, signal?: AbortSignal) => Promise<OnlinePlayersSnapshot>
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
  const filters = shallowRef<PlayerMapFilters>(restorePlayerMapFilters(options.initialQuery ?? new URLSearchParams()))
  const selectedSnapshotId = shallowRef<number | null>(restorePlayerMapObservation(options.initialQuery ?? new URLSearchParams()))
  const fitRequest = shallowRef<FitRequest | null>(null)
  const observationCount = computed(() => track.value?.segments.reduce((total, segment) => total + segment.points.length, 0) ?? 0)

  const fittedQueries = new Set<string>()
  let dataOperations!: PlayerMapDataOperations
  const lifecycle = createPlayerMapLifecycle({
    visibility,
    onVisible() {
      void refresh()
      void dataOperations.refreshGameTime()
    },
    onInterval() {
      void dataOperations.refreshGameTime()
    },
  })
  dataOperations = createPlayerMapDataOperations({
    authorizationHeader: options.authorizationHeader,
    visibility,
    lifecycle,
    fetchGameTime: requestGameTime,
    fetchPlayers: requestPlayers,
    fetchPlayer: requestPlayer,
    state: {
      historicalPlayers,
      historyState,
      playerSearch,
      gameTime,
      gameTimeState,
      filters,
      state,
    },
  })
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
    lifecycle.invalidate('track')
    track.value = null
    trackState.value = player === null ? 'empty' : 'loading'
    selectedSnapshotId.value = null
    filters.value = Object.freeze({ ...filters.value, player: player?.trim() || null })
    syncUrl()
  }

  function setRange(fromUtc: string | null, toUtc: string | null) {
    lifecycle.invalidate('track')
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
    lifecycle.invalidate('track')
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

  async function refresh(): Promise<void> {
    if (lifecycle.isDisposed() || !visibility.isVisible())
      return
    const authorizationHeader = options.authorizationHeader()
    if (authorizationHeader === null) {
      state.value = 'failed'
      return
    }
    const hadPreviousData = metadata.value !== null || onlinePlayers.value.length > 0 || historicalPlayers.value.length > 0
    const request = lifecycle.begin('core')
    if (!hadPreviousData)
      state.value = 'loading'

    const historyRequest = dataOperations.searchHistoricalPlayers(playerSearch.value)
    const results = await Promise.allSettled([
      requestMetadata(authorizationHeader, request.controller.signal),
      requestOnline(authorizationHeader, request.controller.signal),
    ])
    await historyRequest
    if (!lifecycle.isCurrent(request))
      return
    if (results.some(result => result.status === 'rejected' && isPlayerMapForbidden(result.reason)) || historyState.value === 'forbidden') {
      onlineState.value = 'forbidden'
      historyState.value = 'forbidden'
      state.value = 'forbidden'
      lifecycle.finish(request)
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
        if (metadata.value !== null && playerMapWorldIdentity(metadata.value) !== playerMapWorldIdentity(metadataResult.value))
          clearWorldBoundState()
        metadata.value = metadataResult.value
        if (metadataResult.value.availability === 'stale') {
          metadataWasStale = true
          failedCount++
        }
      }
    }
    else if (!isPlayerMapAborted(metadataResult.reason)) {
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
    else if (!isPlayerMapAborted(onlineResult.reason)) {
      failedCount++
      onlineState.value = onlinePlayers.value.length === 0 ? 'failed' : 'stale'
    }
    if (historyState.value === 'failed' || historyState.value === 'stale')
      failedCount++
    state.value = mapPlayerMapPageState(
      metadata.value,
      onlinePlayers.value.length,
      historicalPlayers.value.length,
      failedCount,
      hadPreviousData,
    )
    if (metadataWasStale)
      state.value = 'stale'
    lifecycle.finish(request)

    if (metadata.value !== null && visibility.isVisible()
      && filters.value.player !== null && filters.value.fromUtc !== null && filters.value.toUtc !== null) {
      await refreshTrack()
    }
  }

  async function refreshTrack(): Promise<void> {
    if (lifecycle.isDisposed())
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
    const key = playerTrackQueryKey(requestFilters)
    const request = lifecycle.begin('track')
    if (track.value === null)
      trackState.value = 'loading'
    try {
      const result = await requestTrack(authorizationHeader, requestFilters, request.controller.signal)
      if (!lifecycle.isCurrent(request))
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
      const extent = playerTrackFitExtent(result)
      if (extent !== null && !fittedQueries.has(key)) {
        fittedQueries.add(key)
        fitRequest.value = Object.freeze({ queryKey: key, extent })
      }
      if (observationCount.value === 0)
        state.value = 'empty'
    }
    catch (error) {
      if (lifecycle.isCurrent(request) && !isPlayerMapAborted(error)) {
        if (isPlayerMapForbidden(error)) {
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
      lifecycle.finish(request)
    }
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
    searchHistoricalPlayers: dataOperations.searchHistoricalPlayers,
    selectObservation,
    refresh,
    refreshTrack,
    start: lifecycle.start,
    dispose: lifecycle.dispose,
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
