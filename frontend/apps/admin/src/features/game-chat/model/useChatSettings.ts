import type { DeepReadonly, ShallowRef } from 'vue'
import type { ChatSettings, GameChatManagementState } from './gameChatManagement'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import {
  chatGetSettingsQuery,
  chatResetSettingsMutation,
  chatUpdateSettingsMutation,
} from '../../../shared/api/generated/@pinia/colada.gen'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { parseChatSettings } from '../api/chat'

const defaultSettings: ChatSettings = Object.freeze({
  isEnabled: true,
  globalServerName: null,
  whisperServerName: null,
  commandPrefixes: Object.freeze(['/']) as string[],
  excludeCommandsFromHistory: true,
  historyRetentionDays: 0,
})

export interface ChatSettingsController {
  state: DeepReadonly<ShallowRef<GameChatManagementState>>
  settings: DeepReadonly<ShallowRef<ChatSettings>>
  isSaving: DeepReadonly<ShallowRef<boolean>>
  isResetting: DeepReadonly<ShallowRef<boolean>>
  feedbackMessage: DeepReadonly<ShallowRef<string | null>>
  isDirty: DeepReadonly<ShallowRef<boolean>>
  load: () => Promise<void>
  save: (settings: ChatSettings) => Promise<boolean>
  reset: () => Promise<boolean>
  setDirty: (dirty: boolean) => void
  canLeave: (confirmLeave?: () => boolean) => boolean
  dispose: () => void
}

export interface UseChatSettingsOptions {
  auth?: { authorizationHeader: string | null, expireSession: () => void }
  fetchSettings?: (authorizationHeader: string, signal?: AbortSignal) => Promise<ChatSettings>
  saveSettings?: (authorizationHeader: string, settings: ChatSettings, signal?: AbortSignal) => Promise<ChatSettings>
  resetSettings?: (authorizationHeader: string, signal?: AbortSignal) => Promise<ChatSettings>
  invalidateSettings?: (filter: { exact: true }) => Promise<unknown>
  onSessionExpired?: () => void
}

async function fetchSettingsDefault(authorizationHeader: string, signal?: AbortSignal) {
  const definition = chatGetSettingsQuery({ headers: { Authorization: authorizationHeader } })
  return parseChatSettings(await definition.query({
    signal,
  } as Parameters<typeof definition.query>[0]))
}

async function saveSettingsDefault(authorizationHeader: string, settings: ChatSettings, signal?: AbortSignal) {
  const definition = chatUpdateSettingsMutation({ headers: { Authorization: authorizationHeader } })
  return parseChatSettings(await definition.mutation({
    body: settings,
    signal,
  }, {} as Parameters<typeof definition.mutation>[1]))
}

async function resetSettingsDefault(authorizationHeader: string, signal?: AbortSignal) {
  const definition = chatResetSettingsMutation({ headers: { Authorization: authorizationHeader } })
  return parseChatSettings(await definition.mutation({
    signal,
  }, {} as Parameters<typeof definition.mutation>[1]))
}

export function useChatSettings(options: UseChatSettingsOptions = {}): ChatSettingsController {
  const auth = options.auth ?? useAuthStore()
  const fetchSettings = options.fetchSettings ?? fetchSettingsDefault
  const saveSettings = options.saveSettings ?? saveSettingsDefault
  const resetSettings = options.resetSettings ?? resetSettingsDefault
  const invalidateSettings = options.invalidateSettings ?? (() => Promise.resolve())
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<GameChatManagementState>('loading')
  const settings = shallowRef<ChatSettings>(defaultSettings)
  const isSaving = shallowRef(false)
  const isResetting = shallowRef(false)
  const feedbackMessage = shallowRef<string | null>(null)
  const isDirty = shallowRef(false)
  let controller: AbortController | null = null
  let operationVersion = 0
  let disposed = false
  let sessionExpiryNotified = false

  function expireSession() {
    if (auth.authorizationHeader !== null)
      auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  function failure(error: unknown, hadValue: boolean) {
    if (error instanceof HttpError && error.status === 401)
      expireSession()
    if (error instanceof HttpError && error.status === 403) {
      state.value = 'forbidden'
      return
    }
    state.value = hadValue ? 'stale' : 'failed'
    feedbackMessage.value = '设置操作失败，请重试。'
  }

  function authorization(): string | null {
    const header = auth.authorizationHeader
    if (header === null)
      expireSession()
    return header
  }

  async function load(): Promise<void> {
    if (disposed)
      return
    controller?.abort()
    const version = ++operationVersion
    const current = new AbortController()
    controller = current
    const hadValue = state.value === 'ready' || state.value === 'stale'
    if (!hadValue)
      state.value = 'loading'
    const header = authorization()
    if (header === null) {
      failure(new HttpError('http', 'Authentication required', { status: 401 }), hadValue)
      return
    }
    try {
      const authoritative = await fetchSettings(header, current.signal)
      if (disposed || version !== operationVersion)
        return
      settings.value = authoritative
      state.value = 'ready'
      feedbackMessage.value = null
      isDirty.value = false
      sessionExpiryNotified = false
    }
    catch (error) {
      if (!(error instanceof HttpError && error.code === 'aborted'))
        failure(error, hadValue)
    }
    finally {
      if (version === operationVersion)
        controller = null
    }
  }

  async function mutate(kind: 'save' | 'reset', draft?: ChatSettings): Promise<boolean> {
    if (disposed || isSaving.value || isResetting.value)
      return false
    const header = authorization()
    if (header === null)
      return false
    const current = new AbortController()
    controller = current
    const version = ++operationVersion
    isSaving.value = kind === 'save'
    isResetting.value = kind === 'reset'
    feedbackMessage.value = null
    try {
      const authoritative = kind === 'save'
        ? await saveSettings(header, draft!, current.signal)
        : await resetSettings(header, current.signal)
      if (disposed || version !== operationVersion)
        return false
      settings.value = authoritative
      state.value = 'ready'
      isDirty.value = false
      await invalidateSettings({ exact: true })
      return true
    }
    catch (error) {
      if (!(error instanceof HttpError && error.code === 'aborted'))
        failure(error, true)
      return false
    }
    finally {
      if (version === operationVersion) {
        controller = null
        isSaving.value = false
        isResetting.value = false
      }
    }
  }

  function setDirty(dirty: boolean) {
    isDirty.value = dirty
  }

  function canLeave(confirmLeave: () => boolean = () => false) {
    return !isDirty.value || confirmLeave()
  }

  function dispose() {
    disposed = true
    operationVersion++
    controller?.abort()
    controller = null
  }

  onMounted(() => void load())
  onUnmounted(dispose)

  return {
    state: readonly(state),
    settings: readonly(settings),
    isSaving: readonly(isSaving),
    isResetting: readonly(isResetting),
    feedbackMessage: readonly(feedbackMessage),
    isDirty: readonly(isDirty),
    load,
    save: draft => mutate('save', draft),
    reset: () => mutate('reset'),
    setDirty,
    canLeave,
    dispose,
  }
}
