import type { DeepReadonly, ShallowRef } from 'vue'
import type { AuthRole } from '../../auth/model/authSession'
import type { ModMetadata } from '../api/mods'

import { computed, onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { fetchMods as requestMods, setModEnabled as requestSetModEnabled } from '../api/mods'

export type ModsState = 'loading' | 'empty' | 'fresh' | 'failed'
export type ModsFeedbackCode = 'session-expired' | 'forbidden' | 'conflict' | 'load-failed' | 'change-failed' | 'restart-required'

export interface ModsController {
  state: DeepReadonly<ShallowRef<ModsState>>
  mods: DeepReadonly<ShallowRef<readonly ModMetadata[]>>
  feedback: DeepReadonly<ShallowRef<{ code: ModsFeedbackCode } | null>>
  canMutate: Readonly<import('vue').ComputedRef<boolean>>
  changingDirectoryId: DeepReadonly<ShallowRef<string | null>>
  refresh: () => Promise<void>
  changeNextStart: (mod: ModMetadata, enabled: boolean) => Promise<boolean>
  clearFeedback: () => void
  dispose: () => void
}

export interface UseModsOptions {
  auth?: { authorizationHeader: string | null, role: AuthRole | null, expireSession: () => void }
  fetchMods?: typeof requestMods
  setModEnabled?: typeof requestSetModEnabled
  onSessionExpired?: () => void
}

export function useMods(options: UseModsOptions = {}): ModsController {
  const auth = options.auth ?? useAuthStore()
  const fetchMods = options.fetchMods ?? requestMods
  const setModEnabled = options.setModEnabled ?? requestSetModEnabled
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<ModsState>('loading')
  const mods = shallowRef<readonly ModMetadata[]>(Object.freeze([]))
  const feedback = shallowRef<{ code: ModsFeedbackCode } | null>(null)
  const changingDirectoryId = shallowRef<string | null>(null)
  const canMutate = computed(() => auth.role === 'Owner')
  let refreshPromise: Promise<void> | null = null
  let changePromise: Promise<boolean> | null = null
  let refreshController: AbortController | null = null
  let changeController: AbortController | null = null
  let disposed = false

  function clearFeedback() {
    feedback.value = null
  }

  function sessionExpired() {
    auth.expireSession()
    state.value = 'failed'
    feedback.value = { code: 'session-expired' }
    onSessionExpired()
  }

  function refresh(): Promise<void> {
    if (refreshPromise !== null)
      return refreshPromise
    if (disposed || auth.authorizationHeader === null)
      return Promise.resolve()

    refreshController = new AbortController()
    const request = fetchMods(auth.authorizationHeader, refreshController.signal)
      .then((next) => {
        if (disposed)
          return
        mods.value = Object.freeze([...next])
        state.value = next.length === 0 ? 'empty' : 'fresh'
        if (feedback.value?.code === 'load-failed')
          feedback.value = null
      })
      .catch((cause: unknown) => {
        if (disposed || (cause instanceof HttpError && cause.code === 'aborted'))
          return
        if (cause instanceof HttpError && cause.status === 401) {
          sessionExpired()
        }
        else {
          state.value = 'failed'
          feedback.value = { code: cause instanceof HttpError && cause.status === 403 ? 'forbidden' : 'load-failed' }
        }
      })
      .finally(() => {
        refreshController = null
        refreshPromise = null
      })
    refreshPromise = request
    return request
  }

  function changeNextStart(mod: ModMetadata, enabled: boolean): Promise<boolean> {
    if (changePromise !== null)
      return changePromise
    if (disposed || !canMutate.value || mod.isProtected || auth.authorizationHeader === null)
      return Promise.resolve(false)

    changeController = new AbortController()
    changingDirectoryId.value = mod.directoryId
    feedback.value = null
    const request = setModEnabled(auth.authorizationHeader, mod.directoryId, enabled, changeController.signal)
      .then(async () => {
        if (disposed)
          return false
        await refresh()
        feedback.value = { code: 'restart-required' }
        return true
      })
      .catch(async (cause: unknown) => {
        if (disposed || (cause instanceof HttpError && cause.code === 'aborted'))
          return false
        if (cause instanceof HttpError && cause.status === 401) {
          sessionExpired()
        }
        else if (cause instanceof HttpError && cause.status === 409) {
          await refresh()
          feedback.value = { code: 'conflict' }
        }
        else {
          feedback.value = { code: cause instanceof HttpError && cause.status === 403 ? 'forbidden' : 'change-failed' }
        }
        return false
      })
      .finally(() => {
        changingDirectoryId.value = null
        changeController = null
        changePromise = null
      })
    changePromise = request
    return request
  }

  function dispose() {
    disposed = true
    refreshController?.abort()
    changeController?.abort()
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)
  return {
    state: readonly(state),
    mods: readonly(mods),
    feedback: readonly(feedback),
    canMutate,
    changingDirectoryId: readonly(changingDirectoryId),
    refresh,
    changeNextStart,
    clearFeedback,
    dispose,
  }
}
