import type { DeepReadonly, ShallowRef } from 'vue'
import type { BackupPolicy, BackupPolicyKind, BackupPolicyUpdate } from '../api/backupPolicies'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import {
  fetchBackupPolicies as requestBackupPolicies,
  saveBackupPolicy as requestSaveBackupPolicy,
} from '../api/backupPolicies'

export type BackupPoliciesViewState = 'loading' | 'ready' | 'stale' | 'failed' | 'forbidden' | 'protocol-error'
export type BackupPolicySaveErrorCode = 'conflict' | 'invalid' | 'unavailable'

export interface BackupPolicySaveError {
  readonly kind: BackupPolicyKind
  readonly code: BackupPolicySaveErrorCode
}

export interface BackupPoliciesController {
  state: DeepReadonly<ShallowRef<BackupPoliciesViewState>>
  policies: DeepReadonly<ShallowRef<readonly BackupPolicy[]>>
  drafts: DeepReadonly<ShallowRef<readonly BackupPolicyUpdate[]>>
  isSaving: DeepReadonly<ShallowRef<boolean>>
  pendingKind: DeepReadonly<ShallowRef<BackupPolicyKind | null>>
  errorCode: DeepReadonly<ShallowRef<string | null>>
  saveError: DeepReadonly<ShallowRef<BackupPolicySaveError | null>>
  refresh: () => Promise<void>
  updateDraft: (draft: BackupPolicyUpdate) => void
  save: (kind: BackupPolicyKind) => Promise<boolean>
  dispose: () => void
}

export interface UseBackupPoliciesOptions {
  auth?: { authorizationHeader: string | null, expireSession: () => void }
  fetchPolicies?: typeof requestBackupPolicies
  savePolicy?: typeof requestSaveBackupPolicy
  onSessionExpired?: () => void
}

function copyPolicy(policy: BackupPolicy): BackupPolicyUpdate {
  return Object.freeze({ ...policy })
}

function isAbortError(cause: unknown): boolean {
  return (cause instanceof HttpError && cause.code === 'aborted')
    || (cause instanceof Error && cause.name === 'AbortError')
}

function failureCode(cause: unknown): string {
  return cause instanceof HttpError ? (cause.problemCode ?? cause.code) : 'protocol_error'
}

function saveFailureCode(cause: unknown): BackupPolicySaveErrorCode {
  if (cause instanceof HttpError && cause.status === 409)
    return 'conflict'
  if (cause instanceof HttpError && cause.status === 400 && cause.problemCode === 'backup_policy_invalid')
    return 'invalid'
  return 'unavailable'
}

export function useBackupPolicies(options: UseBackupPoliciesOptions = {}): BackupPoliciesController {
  const auth = options.auth ?? useAuthStore()
  const fetchPolicies = options.fetchPolicies ?? requestBackupPolicies
  const savePolicy = options.savePolicy ?? requestSaveBackupPolicy
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<BackupPoliciesViewState>('loading')
  const policies = shallowRef<readonly BackupPolicy[]>(Object.freeze([]))
  const drafts = shallowRef<readonly BackupPolicyUpdate[]>(Object.freeze([]))
  const isSaving = shallowRef(false)
  const pendingKind = shallowRef<BackupPolicyKind | null>(null)
  const errorCode = shallowRef<string | null>(null)
  const saveError = shallowRef<BackupPolicySaveError | null>(null)
  let loadController: AbortController | null = null
  let saveController: AbortController | null = null
  let loadVersion = 0
  let disposed = false
  let sessionExpiryNotified = false

  function expireSession() {
    auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  function handleLoadFailure(cause: unknown) {
    if (isAbortError(cause) || disposed)
      return
    if (cause instanceof HttpError && cause.status === 401)
      expireSession()
    if (cause instanceof HttpError && cause.status === 403) {
      policies.value = Object.freeze([])
      state.value = 'forbidden'
      return
    }
    errorCode.value = failureCode(cause)
    state.value = errorCode.value === 'protocol_error'
      ? 'protocol-error'
      : policies.value.length === 0 ? 'failed' : 'stale'
  }

  async function refresh() {
    if (disposed)
      return
    loadController?.abort()
    const controller = new AbortController()
    loadController = controller
    const version = ++loadVersion
    if (policies.value.length === 0)
      state.value = 'loading'
    const authorizationHeader = auth.authorizationHeader
    if (authorizationHeader === null) {
      handleLoadFailure(new HttpError('http', 'Authentication required', { status: 401 }))
      return
    }
    try {
      const nextPolicies = await fetchPolicies(authorizationHeader, controller.signal)
      if (disposed || version !== loadVersion)
        return
      policies.value = Object.freeze([...nextPolicies])
      if (drafts.value.length === 0)
        drafts.value = Object.freeze(nextPolicies.map(copyPolicy))
      errorCode.value = null
      state.value = 'ready'
      sessionExpiryNotified = false
    }
    catch (cause) {
      if (version === loadVersion)
        handleLoadFailure(cause)
    }
    finally {
      if (loadController === controller)
        loadController = null
    }
  }

  function updateDraft(draft: BackupPolicyUpdate) {
    if (disposed || !drafts.value.some(item => item.kind === draft.kind))
      return
    drafts.value = Object.freeze(drafts.value.map(item => item.kind === draft.kind ? Object.freeze({ ...draft }) : item))
    if (saveError.value?.kind === draft.kind)
      saveError.value = null
  }

  async function save(kind: BackupPolicyKind): Promise<boolean> {
    if (disposed || isSaving.value)
      return false
    const draft = drafts.value.find(item => item.kind === kind)
    if (draft === undefined)
      return false
    const authorizationHeader = auth.authorizationHeader
    if (authorizationHeader === null) {
      handleLoadFailure(new HttpError('http', 'Authentication required', { status: 401 }))
      return false
    }

    const controller = new AbortController()
    saveController?.abort()
    saveController = controller
    isSaving.value = true
    pendingKind.value = kind
    saveError.value = null
    try {
      const saved = await savePolicy(authorizationHeader, draft, controller.signal)
      if (disposed || saveController !== controller)
        return false
      policies.value = Object.freeze(policies.value.map(item => item.kind === kind ? saved : item))
      drafts.value = Object.freeze(drafts.value.map(item => item.kind === kind ? copyPolicy(saved) : item))
      errorCode.value = null
      state.value = 'ready'
      sessionExpiryNotified = false
      return true
    }
    catch (cause) {
      if (disposed || saveController !== controller || isAbortError(cause))
        return false
      const code = saveFailureCode(cause)
      saveError.value = Object.freeze({ kind, code })
      errorCode.value = failureCode(cause)
      if (cause instanceof HttpError && cause.status === 401) {
        expireSession()
      }
      else if (cause instanceof HttpError && cause.status === 403) {
        policies.value = Object.freeze([])
        state.value = 'forbidden'
      }
      else if (code === 'conflict') {
        state.value = 'stale'
      }
      else if (errorCode.value === 'protocol_error') {
        state.value = 'protocol-error'
      }
      return false
    }
    finally {
      if (saveController === controller) {
        saveController = null
        isSaving.value = false
        pendingKind.value = null
      }
    }
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    loadVersion++
    loadController?.abort()
    saveController?.abort()
    loadController = null
    saveController = null
    isSaving.value = false
    pendingKind.value = null
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)

  return {
    state: readonly(state),
    policies: readonly(policies),
    drafts: readonly(drafts),
    isSaving: readonly(isSaving),
    pendingKind: readonly(pendingKind),
    errorCode: readonly(errorCode),
    saveError: readonly(saveError),
    refresh,
    updateDraft,
    save,
    dispose,
  }
}
