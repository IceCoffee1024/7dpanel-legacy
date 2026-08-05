import type { ShallowRef } from 'vue'
import type {
  FetchHistoricalPlayersOptions,
  HistoricalPlayerDetails,
  HistoricalPlayersPage,
  HistoricalPlayerSummary,
  MapGameTime,
  MapGameTimeEnvelope,
} from '../api/playerMap'
import type { PlayerMapLifecycle } from './playerMapLifecycle'
import type { PlayerMapFilters } from './playerMapProjection'

import { HttpError } from '../../../shared/api/http'

export type FetchGameTime = (authorizationHeader: string, signal?: AbortSignal) => Promise<MapGameTimeEnvelope>
export type FetchPlayers = (
  authorizationHeader: string,
  options: FetchHistoricalPlayersOptions,
  signal?: AbortSignal,
) => Promise<HistoricalPlayersPage>
export type FetchPlayer = (authorizationHeader: string, crossplatformId: string, signal?: AbortSignal) => Promise<HistoricalPlayerDetails>

export interface PlayerMapDataOperationsState {
  historicalPlayers: ShallowRef<readonly HistoricalPlayerSummary[]>
  historyState: ShallowRef<'loading' | 'ready' | 'empty' | 'stale' | 'forbidden' | 'failed'>
  playerSearch: ShallowRef<string>
  gameTime: ShallowRef<MapGameTime | null>
  gameTimeState: ShallowRef<'loading' | 'ready' | 'stale' | 'unavailable'>
  filters: ShallowRef<PlayerMapFilters>
  state: ShallowRef<'loading' | 'ready' | 'empty' | 'partial' | 'stale' | 'forbidden' | 'failed'>
}

export interface PlayerMapDataOperationsOptions {
  authorizationHeader: () => string | null
  visibility: { isVisible: () => boolean }
  lifecycle: PlayerMapLifecycle
  state: PlayerMapDataOperationsState
  fetchGameTime: FetchGameTime
  fetchPlayers: FetchPlayers
  fetchPlayer: FetchPlayer
}

export interface PlayerMapDataOperations {
  searchHistoricalPlayers: (query: string) => Promise<void>
  refreshGameTime: () => Promise<void>
}

export function isPlayerMapForbidden(error: unknown): boolean {
  return error instanceof HttpError && error.status === 403
}

export function isPlayerMapAborted(error: unknown): boolean {
  return error instanceof HttpError && error.code === 'aborted'
}

export function createPlayerMapDataOperations(options: PlayerMapDataOperationsOptions): PlayerMapDataOperations {
  async function searchHistoricalPlayers(query: string): Promise<void> {
    if (options.lifecycle.isDisposed())
      return
    options.state.playerSearch.value = query
    const authorizationHeader = options.authorizationHeader()
    if (authorizationHeader === null) {
      options.state.historyState.value = options.state.historicalPlayers.value.length === 0 ? 'failed' : 'stale'
      return
    }
    const request = options.lifecycle.begin('history')
    if (options.state.historicalPlayers.value.length === 0)
      options.state.historyState.value = 'loading'
    try {
      const trimmed = query.trim()
      const page = await options.fetchPlayers(authorizationHeader, {
        query: trimmed === '' ? null : trimmed,
        pageSize: 50,
        cursor: null,
      }, request.controller.signal)
      if (!options.lifecycle.isCurrent(request))
        return
      let players = [...page.players]
      const restoredPlayerId = options.state.filters.value.player
      if (restoredPlayerId !== null && !players.some(player => player.crossplatformId === restoredPlayerId)) {
        const restored = await options.fetchPlayer(authorizationHeader, restoredPlayerId, request.controller.signal)
        if (!options.lifecycle.isCurrent(request))
          return
        players = [restored.player, ...players]
      }
      options.state.historicalPlayers.value = Object.freeze(players)
      options.state.historyState.value = players.length === 0 ? 'empty' : 'ready'
    }
    catch (error) {
      if (!options.lifecycle.isCurrent(request) || isPlayerMapAborted(error))
        return
      if (isPlayerMapForbidden(error)) {
        options.state.historyState.value = 'forbidden'
        options.state.state.value = 'forbidden'
      }
      else {
        options.state.historyState.value = options.state.historicalPlayers.value.length === 0 ? 'failed' : 'stale'
      }
    }
    finally {
      options.lifecycle.finish(request)
    }
  }

  async function refreshGameTime(): Promise<void> {
    if (options.lifecycle.isDisposed() || !options.visibility.isVisible())
      return
    const authorizationHeader = options.authorizationHeader()
    if (authorizationHeader === null) {
      options.state.gameTimeState.value = options.state.gameTime.value === null ? 'unavailable' : 'stale'
      return
    }
    const request = options.lifecycle.begin('time')
    try {
      const result = await options.fetchGameTime(authorizationHeader, request.controller.signal)
      if (options.lifecycle.isCurrent(request)) {
        if (result.availability === 'unavailable') {
          options.state.gameTime.value = null
          options.state.gameTimeState.value = 'unavailable'
        }
        else {
          options.state.gameTime.value = result
          options.state.gameTimeState.value = result.availability === 'stale' ? 'stale' : 'ready'
        }
      }
    }
    catch (error) {
      if (options.lifecycle.isCurrent(request) && !isPlayerMapAborted(error))
        options.state.gameTimeState.value = options.state.gameTime.value === null ? 'unavailable' : 'stale'
    }
    finally {
      options.lifecycle.finish(request)
    }
  }

  return { searchHistoricalPlayers, refreshGameTime }
}
