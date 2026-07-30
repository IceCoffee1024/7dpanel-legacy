import type { DeepReadonly, Ref, ShallowRef } from 'vue'
import type { AuthRole } from '../../auth/model/authSession'
import type { BanEntry, BanInput, WhitelistEntry, WhitelistInput } from '../api/accessLists'

import { computed, readonly, shallowRef, unref } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth/model/authStore'
import * as api from '../api/accessLists'

export type AccessListState = 'loading' | 'empty' | 'fresh' | 'stale' | 'failed' | 'forbidden' | 'game-not-ready'
export interface MutationTarget { list: 'ban' | 'whitelist', playerId: string }

type MaybeRef<T> = T | Ref<T>
interface AccessListsAuth {
  authorizationHeader: MaybeRef<string | null>
  role: MaybeRef<AuthRole | null>
  expireSession: () => void
}

export interface UseAccessListsOptions {
  auth?: AccessListsAuth
  fetchBans?: typeof api.fetchBans
  fetchWhitelist?: typeof api.fetchWhitelist
  upsertBan?: typeof api.upsertBan
  removeBan?: typeof api.removeBan
  upsertWhitelist?: typeof api.upsertWhitelist
  removeWhitelist?: typeof api.removeWhitelist
}

export interface AccessListsController {
  banState: DeepReadonly<ShallowRef<AccessListState>>
  whitelistState: DeepReadonly<ShallowRef<AccessListState>>
  bans: DeepReadonly<ShallowRef<readonly BanEntry[]>>
  whitelist: DeepReadonly<ShallowRef<readonly WhitelistEntry[]>>
  canMutate: Readonly<Ref<boolean>>
  mutationTarget: DeepReadonly<ShallowRef<MutationTarget | null>>
  refreshBans: () => Promise<void>
  refreshWhitelist: () => Promise<void>
  saveBan: (input: BanInput) => Promise<boolean>
  removeBan: (playerId: string) => Promise<boolean>
  saveWhitelist: (input: WhitelistInput) => Promise<boolean>
  removeWhitelist: (playerId: string) => Promise<boolean>
  dispose: () => void
}

export function useAccessLists(options: UseAccessListsOptions = {}): AccessListsController {
  const auth = options.auth ?? useAuthStore()
  const banState = shallowRef<AccessListState>('loading')
  const whitelistState = shallowRef<AccessListState>('loading')
  const bans = shallowRef<readonly BanEntry[]>(Object.freeze([]))
  const whitelist = shallowRef<readonly WhitelistEntry[]>(Object.freeze([]))
  const mutationTarget = shallowRef<MutationTarget | null>(null)
  const canMutate = computed(() => unref(auth.role) === 'Owner' || unref(auth.role) === 'Admin')
  let disposed = false

  function authorization() {
    return unref(auth.authorizationHeader)
  }

  async function load<T>(
    current: ShallowRef<readonly T[]>,
    state: ShallowRef<AccessListState>,
    request: (authorizationHeader: string) => Promise<readonly T[]>,
  ) {
    const token = authorization()
    if (token === null) {
      state.value = 'failed'
      return
    }
    if (current.value.length === 0)
      state.value = 'loading'
    try {
      const next = await request(token)
      if (disposed)
        return
      current.value = next
      state.value = next.length === 0 ? 'empty' : 'fresh'
    }
    catch (error) {
      if (disposed)
        return
      if (error instanceof HttpError && error.status === 401) {
        auth.expireSession()
        state.value = 'failed'
        return
      }
      if (error instanceof HttpError && error.status === 403) {
        state.value = 'forbidden'
        return
      }
      if (error instanceof HttpError && (error.status === 503 || error.problemCode === 'game_not_ready')) {
        state.value = 'game-not-ready'
        return
      }
      state.value = current.value.length === 0 ? 'failed' : 'stale'
    }
  }

  const refreshBans = () => load(bans, banState, options.fetchBans ?? api.fetchBans)
  const refreshWhitelist = () => load(whitelist, whitelistState, options.fetchWhitelist ?? api.fetchWhitelist)

  async function mutate(
    target: MutationTarget,
    request: (authorizationHeader: string) => Promise<void>,
    refresh: () => Promise<void>,
  ) {
    if (!canMutate.value || mutationTarget.value !== null || disposed)
      return false
    const token = authorization()
    if (token === null)
      return false
    mutationTarget.value = target
    try {
      await request(token)
      if (disposed)
        return false
      await refresh()
      return true
    }
    catch (error) {
      if (error instanceof HttpError && error.status === 401)
        auth.expireSession()
      if (error instanceof HttpError && (error.status ?? 0) >= 500)
        await refresh()
      return false
    }
    finally {
      mutationTarget.value = null
    }
  }

  return {
    banState: readonly(banState),
    whitelistState: readonly(whitelistState),
    bans: readonly(bans),
    whitelist: readonly(whitelist),
    canMutate,
    mutationTarget: readonly(mutationTarget),
    refreshBans,
    refreshWhitelist,
    saveBan: input => mutate({ list: 'ban', playerId: input.playerId }, token => (options.upsertBan ?? api.upsertBan)(token, input), refreshBans),
    removeBan: playerId => mutate({ list: 'ban', playerId }, token => (options.removeBan ?? api.removeBan)(token, playerId), refreshBans),
    saveWhitelist: input => mutate({ list: 'whitelist', playerId: input.playerId }, token => (options.upsertWhitelist ?? api.upsertWhitelist)(token, input), refreshWhitelist),
    removeWhitelist: playerId => mutate({ list: 'whitelist', playerId }, token => (options.removeWhitelist ?? api.removeWhitelist)(token, playerId), refreshWhitelist),
    dispose: () => {
      disposed = true
    },
  }
}
