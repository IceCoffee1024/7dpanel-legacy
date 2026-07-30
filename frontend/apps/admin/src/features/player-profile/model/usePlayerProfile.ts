import type { DeepReadonly, MaybeRefOrGetter, ShallowRef } from 'vue'
import type { FetchPlayerProfile, PlayerProfile } from '../api/playerEvidence'

import { onScopeDispose, readonly, shallowRef, toValue, watch } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { fetchPlayerProfile } from '../api/playerEvidence'

export type PlayerProfileSectionKey = 'summary' | 'sessions' | 'activity' | 'inventory' | 'skills' | 'dailyActivity'
export type PlayerProfileSectionViewState = 'loading' | 'available' | 'partial' | 'stale' | 'unavailable' | 'forbidden'
export type PlayerProfileViewState = PlayerProfileSectionViewState

export type PlayerProfileSectionStates = Readonly<Record<PlayerProfileSectionKey, PlayerProfileSectionViewState>>

export interface PlayerProfileController {
  state: DeepReadonly<ShallowRef<PlayerProfileViewState>>
  profile: DeepReadonly<ShallowRef<PlayerProfile | null>>
  sectionStates: DeepReadonly<ShallowRef<PlayerProfileSectionStates>>
  isRefreshing: DeepReadonly<ShallowRef<boolean>>
  errorCode: DeepReadonly<ShallowRef<string | null>>
  refresh: () => Promise<void>
  dispose: () => void
}

interface ProfileAuth {
  authorizationHeader: string | null
  expireSession: () => void
}

export interface UsePlayerProfileOptions {
  auth?: ProfileAuth
  fetchProfile?: FetchPlayerProfile
  onSessionExpired?: () => void
}

const keys: readonly PlayerProfileSectionKey[] = [
  'summary',
  'sessions',
  'activity',
  'inventory',
  'skills',
  'dailyActivity',
]

function allSections(state: PlayerProfileSectionViewState): PlayerProfileSectionStates {
  return Object.freeze(Object.fromEntries(keys.map(key => [key, state])) as Record<PlayerProfileSectionKey, PlayerProfileSectionViewState>)
}

function sectionState(value: string | undefined): PlayerProfileSectionViewState {
  if (value === 'Available')
    return 'available'
  if (value === 'Partial')
    return 'partial'
  if (value === 'Forbidden')
    return 'forbidden'
  return 'unavailable'
}

function statesFromProfile(profile: PlayerProfile): PlayerProfileSectionStates {
  return Object.freeze({
    summary: sectionState(profile.summary?.state),
    sessions: sectionState(profile.sessions?.state),
    activity: sectionState(profile.activity?.state),
    inventory: sectionState(profile.inventory?.state),
    skills: sectionState(profile.skills?.state),
    dailyActivity: sectionState(profile.dailyActivity?.state),
  })
}

function aggregateState(states: PlayerProfileSectionStates): PlayerProfileViewState {
  const values = Object.values(states)
  if (values.every(value => value === 'forbidden'))
    return 'forbidden'
  if (values.some(value => value === 'partial' || value === 'unavailable'))
    return 'partial'
  return 'available'
}

export function usePlayerProfile(
  crossplatformId: MaybeRefOrGetter<string>,
  options: UsePlayerProfileOptions = {},
): PlayerProfileController {
  const auth = options.auth ?? useAuthStore()
  const requestProfile = options.fetchProfile ?? fetchPlayerProfile
  const profile = shallowRef<PlayerProfile | null>(null)
  const state = shallowRef<PlayerProfileViewState>('loading')
  const sectionStates = shallowRef<PlayerProfileSectionStates>(allSections('loading'))
  const isRefreshing = shallowRef(false)
  const errorCode = shallowRef<string | null>(null)
  let controller: AbortController | null = null
  let requestVersion = 0
  let disposed = false
  let sessionExpiryNotified = false

  function markUnavailable(error: unknown) {
    if (profile.value === null) {
      sectionStates.value = allSections('unavailable')
      state.value = 'unavailable'
    }
    else {
      sectionStates.value = Object.freeze(Object.fromEntries(
        keys.map(key => [key, sectionStates.value[key] === 'forbidden' ? 'forbidden' : 'stale']),
      ) as Record<PlayerProfileSectionKey, PlayerProfileSectionViewState>)
      state.value = 'stale'
    }
    errorCode.value = error instanceof HttpError ? (error.problemCode ?? error.code) : 'protocol_error'
  }

  function expireSession() {
    auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      options.onSessionExpired?.()
    }
  }

  async function load(id: string): Promise<void> {
    if (disposed)
      return
    controller?.abort()
    const version = ++requestVersion
    const nextController = new AbortController()
    controller = nextController
    const authorizationHeader = auth.authorizationHeader
    if (authorizationHeader === null) {
      expireSession()
      markUnavailable(new HttpError('http', 'Authentication required', { status: 401 }))
      return
    }
    if (profile.value === null) {
      state.value = 'loading'
      sectionStates.value = allSections('loading')
    }
    isRefreshing.value = true
    try {
      const next = await requestProfile(authorizationHeader, id, nextController.signal)
      if (disposed || version !== requestVersion)
        return
      profile.value = next
      sectionStates.value = statesFromProfile(next)
      state.value = aggregateState(sectionStates.value)
      errorCode.value = null
      sessionExpiryNotified = false
    }
    catch (error) {
      if (disposed || version !== requestVersion || (error instanceof HttpError && error.code === 'aborted'))
        return
      if (error instanceof HttpError && error.status === 401)
        expireSession()
      if (error instanceof HttpError && error.status === 403) {
        profile.value = null
        sectionStates.value = allSections('forbidden')
        state.value = 'forbidden'
        errorCode.value = error.problemCode ?? error.code
      }
      else {
        markUnavailable(error)
      }
    }
    finally {
      if (version === requestVersion) {
        controller = null
        isRefreshing.value = false
      }
    }
  }

  function refresh() {
    const id = toValue(crossplatformId).trim()
    if (id === '') {
      profile.value = null
      sectionStates.value = allSections('unavailable')
      state.value = 'unavailable'
      return Promise.resolve()
    }
    return load(id)
  }

  const stop = watch(
    () => toValue(crossplatformId),
    () => {
      profile.value = null
      sectionStates.value = allSections('loading')
      state.value = 'loading'
      void refresh()
    },
    { immediate: true },
  )

  function dispose() {
    if (disposed)
      return
    disposed = true
    requestVersion++
    stop()
    controller?.abort()
    controller = null
    isRefreshing.value = false
  }

  onScopeDispose(dispose, true)

  return {
    state: readonly(state),
    profile: readonly(profile),
    sectionStates: readonly(sectionStates),
    isRefreshing: readonly(isRefreshing),
    errorCode: readonly(errorCode),
    refresh,
    dispose,
  }
}
