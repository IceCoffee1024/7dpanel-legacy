import type { DeepReadonly, ShallowRef } from 'vue'
import type { ColoredChatProfilePage } from '../api/chat'
import type {
  ColoredChatProfile,
  ColoredChatProfileDraft,
  ColoredChatSettings,
  GameChatManagementState,
} from './gameChatManagement'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import {
  chatCreateColoredProfileMutation,
  chatDeleteColoredProfileMutation,
  chatGetColoredProfilesQuery,
  chatGetColoredSettingsQuery,
  chatResetColoredSettingsMutation,
  chatUpdateColoredProfileMutation,
  chatUpdateColoredSettingsMutation,
} from '../../../shared/api/generated/@pinia/colada.gen'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import {
  parseColoredChatProfile,
  parseColoredChatProfilePage,
  parseColoredChatSettings,
} from '../api/chat'

const defaultColoredSettings: ColoredChatSettings = Object.freeze({
  isEnabled: false,
  globalDefaultColor: null,
  whisperDefaultColor: null,
  friendsDefaultColor: null,
  partyDefaultColor: null,
  adminDefaultColor: null,
  systemDefaultColor: null,
  playerColorTagPermission: 'None',
})

export interface ColoredChatController {
  settings: DeepReadonly<ShallowRef<ColoredChatSettings>>
  settingsState: DeepReadonly<ShallowRef<GameChatManagementState>>
  profiles: DeepReadonly<ShallowRef<readonly ColoredChatProfile[]>>
  profilesState: DeepReadonly<ShallowRef<GameChatManagementState>>
  profileFilter: DeepReadonly<ShallowRef<string>>
  nextCursor: DeepReadonly<ShallowRef<string | null>>
  isSavingSettings: DeepReadonly<ShallowRef<boolean>>
  isResettingSettings: DeepReadonly<ShallowRef<boolean>>
  isMutatingProfile: DeepReadonly<ShallowRef<boolean>>
  settingsFeedbackMessage: DeepReadonly<ShallowRef<string | null>>
  profileFeedbackMessage: DeepReadonly<ShallowRef<string | null>>
  isSettingsDirty: DeepReadonly<ShallowRef<boolean>>
  filterProfiles: (filter: string) => Promise<void>
  loadMoreProfiles: () => Promise<void>
  retryProfiles: () => Promise<void>
  createProfile: (profile: ColoredChatProfileDraft) => Promise<boolean>
  updateProfile: (profile: ColoredChatProfileDraft) => Promise<boolean>
  deleteProfile: (crossplatformId: string) => Promise<boolean>
  saveSettings: (settings: ColoredChatSettings) => Promise<boolean>
  resetSettings: () => Promise<boolean>
  setSettingsDirty: (dirty: boolean) => void
  canLeave: (confirmLeave?: () => boolean) => boolean
  dispose: () => void
}

export interface UseColoredChatOptions {
  auth?: { authorizationHeader: string | null, expireSession: () => void }
  fetchSettings?: (authorizationHeader: string, signal?: AbortSignal) => Promise<ColoredChatSettings>
  saveSettings?: (authorizationHeader: string, settings: ColoredChatSettings, signal?: AbortSignal) => Promise<ColoredChatSettings>
  resetSettings?: (authorizationHeader: string, signal?: AbortSignal) => Promise<ColoredChatSettings>
  fetchProfiles?: (authorizationHeader: string, filter: string, cursor: string | null, limit: number, signal?: AbortSignal) => Promise<ColoredChatProfilePage>
  createProfile?: (authorizationHeader: string, profile: ColoredChatProfileDraft, signal?: AbortSignal) => Promise<ColoredChatProfile>
  updateProfile?: (authorizationHeader: string, profile: ColoredChatProfileDraft, signal?: AbortSignal) => Promise<ColoredChatProfile>
  deleteProfile?: (authorizationHeader: string, crossplatformId: string, signal?: AbortSignal) => Promise<void>
  invalidateSettings?: (filter: { exact: true }) => Promise<unknown>
  invalidateProfiles?: (filter: { exact: true }) => Promise<unknown>
  onSessionExpired?: () => void
}

async function fetchColoredSettingsDefault(header: string, signal?: AbortSignal) {
  const definition = chatGetColoredSettingsQuery({ headers: { Authorization: header } })
  return parseColoredChatSettings(await definition.query({
    signal,
  } as Parameters<typeof definition.query>[0]))
}

async function saveColoredSettingsDefault(header: string, body: ColoredChatSettings, signal?: AbortSignal) {
  const definition = chatUpdateColoredSettingsMutation({ headers: { Authorization: header } })
  return parseColoredChatSettings(await definition.mutation({
    body,
    signal,
  }, {} as Parameters<typeof definition.mutation>[1]))
}

async function resetColoredSettingsDefault(header: string, signal?: AbortSignal) {
  const definition = chatResetColoredSettingsMutation({ headers: { Authorization: header } })
  return parseColoredChatSettings(await definition.mutation({
    signal,
  }, {} as Parameters<typeof definition.mutation>[1]))
}

async function fetchProfilesDefault(header: string, filter: string, cursor: string | null, limit: number, signal?: AbortSignal) {
  const definition = chatGetColoredProfilesQuery({
    headers: { Authorization: header },
    query: {
      limit,
      ...(filter === '' ? {} : { crossplatformId: filter }),
      ...(cursor === null ? {} : { cursor }),
    },
  })
  return parseColoredChatProfilePage(await definition.query({
    signal,
  } as Parameters<typeof definition.query>[0]))
}

async function profileRequest(method: 'POST' | 'PUT', header: string, profile: ColoredChatProfileDraft, signal?: AbortSignal) {
  const body = {
    customName: profile.customName,
    nameColor: profile.nameColor,
    textColor: profile.textColor,
    description: profile.description,
  }
  if (method === 'POST') {
    const definition = chatCreateColoredProfileMutation({ headers: { Authorization: header } })
    return parseColoredChatProfile(await definition.mutation({
      body: { ...body, crossplatformId: profile.crossplatformId },
      signal,
    }, {} as Parameters<typeof definition.mutation>[1]))
  }
  const definition = chatUpdateColoredProfileMutation({ headers: { Authorization: header } })
  return parseColoredChatProfile(await definition.mutation({
    body,
    path: { crossplatformId: profile.crossplatformId },
    signal,
  }, {} as Parameters<typeof definition.mutation>[1]))
}

async function deleteProfileDefault(header: string, crossplatformId: string, signal?: AbortSignal) {
  const definition = chatDeleteColoredProfileMutation({ headers: { Authorization: header } })
  await definition.mutation({
    path: { crossplatformId },
    signal,
  }, {} as Parameters<typeof definition.mutation>[1])
}

function uniqueProfiles(profiles: readonly ColoredChatProfile[]): readonly ColoredChatProfile[] {
  const ids = new Set<string>()
  return Object.freeze(profiles.filter(profile => !ids.has(profile.crossplatformId) && ids.add(profile.crossplatformId)))
}

export function useColoredChat(options: UseColoredChatOptions = {}): ColoredChatController {
  const auth = options.auth ?? useAuthStore()
  const fetchSettings = options.fetchSettings ?? fetchColoredSettingsDefault
  const saveSettingsRequest = options.saveSettings ?? saveColoredSettingsDefault
  const resetSettingsRequest = options.resetSettings ?? resetColoredSettingsDefault
  const fetchProfiles = options.fetchProfiles ?? fetchProfilesDefault
  const createProfileRequest = options.createProfile ?? ((header, profile, signal) => profileRequest('POST', header, profile, signal))
  const updateProfileRequest = options.updateProfile ?? ((header, profile, signal) => profileRequest('PUT', header, profile, signal))
  const deleteProfileRequest = options.deleteProfile ?? deleteProfileDefault
  const invalidateSettings = options.invalidateSettings ?? (() => Promise.resolve())
  const invalidateProfiles = options.invalidateProfiles ?? (() => Promise.resolve())
  const onSessionExpired = options.onSessionExpired ?? (() => {})

  const settings = shallowRef<ColoredChatSettings>(defaultColoredSettings)
  const settingsState = shallowRef<GameChatManagementState>('loading')
  const profiles = shallowRef<readonly ColoredChatProfile[]>(Object.freeze([]))
  const profilesState = shallowRef<GameChatManagementState>('loading')
  const profileFilter = shallowRef('')
  const nextCursor = shallowRef<string | null>(null)
  const isSavingSettings = shallowRef(false)
  const isResettingSettings = shallowRef(false)
  const isMutatingProfile = shallowRef(false)
  const settingsFeedbackMessage = shallowRef<string | null>(null)
  const profileFeedbackMessage = shallowRef<string | null>(null)
  const isSettingsDirty = shallowRef(false)
  let settingsController: AbortController | null = null
  let profilesController: AbortController | null = null
  let mutationController: AbortController | null = null
  let profilesVersion = 0
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

  function authorization(): string | null {
    const header = auth.authorizationHeader
    if (header === null)
      expireSession()
    return header
  }

  function forbidden(error: unknown): boolean {
    if (error instanceof HttpError && error.status === 401)
      expireSession()
    return error instanceof HttpError && error.status === 403
  }

  async function loadSettings() {
    if (disposed)
      return
    settingsController?.abort()
    const current = new AbortController()
    settingsController = current
    const hadValue = settingsState.value === 'ready' || settingsState.value === 'stale'
    const header = authorization()
    if (header === null) {
      settingsState.value = hadValue ? 'stale' : 'failed'
      return
    }
    if (!hadValue)
      settingsState.value = 'loading'
    try {
      settings.value = await fetchSettings(header, current.signal)
      if (disposed || current.signal.aborted)
        return
      settingsState.value = 'ready'
      settingsFeedbackMessage.value = null
      isSettingsDirty.value = false
    }
    catch (error) {
      if (error instanceof HttpError && error.code === 'aborted')
        return
      settingsState.value = forbidden(error) ? 'forbidden' : hadValue ? 'stale' : 'failed'
      settingsFeedbackMessage.value = '彩色聊天设置加载失败。'
    }
    finally {
      if (settingsController === current)
        settingsController = null
    }
  }

  async function loadProfiles(cursor: string | null, append: boolean) {
    if (disposed)
      return
    profilesController?.abort()
    const current = new AbortController()
    profilesController = current
    const version = ++profilesVersion
    if (!append && profiles.value.length === 0)
      profilesState.value = 'loading'
    const header = authorization()
    if (header === null) {
      profilesState.value = profiles.value.length === 0 ? 'failed' : 'stale'
      return
    }
    try {
      const page = await fetchProfiles(header, profileFilter.value, cursor, 50, current.signal)
      if (disposed || version !== profilesVersion)
        return
      profiles.value = uniqueProfiles(append ? [...profiles.value, ...page.profiles] : page.profiles)
      nextCursor.value = page.nextCursor
      profilesState.value = profiles.value.length === 0 ? 'empty' : 'ready'
      profileFeedbackMessage.value = null
    }
    catch (error) {
      if (error instanceof HttpError && error.code === 'aborted')
        return
      profilesState.value = forbidden(error) ? 'forbidden' : profiles.value.length === 0 ? 'failed' : 'stale'
      profileFeedbackMessage.value = '玩家 Profile 加载失败。'
    }
    finally {
      if (profilesController === current)
        profilesController = null
    }
  }

  async function mutateSettings(kind: 'save' | 'reset', draft?: ColoredChatSettings): Promise<boolean> {
    if (disposed || isSavingSettings.value || isResettingSettings.value)
      return false
    const header = authorization()
    if (header === null)
      return false
    const current = new AbortController()
    mutationController = current
    isSavingSettings.value = kind === 'save'
    isResettingSettings.value = kind === 'reset'
    settingsFeedbackMessage.value = null
    try {
      const authoritative = kind === 'save'
        ? await saveSettingsRequest(header, draft!, current.signal)
        : await resetSettingsRequest(header, current.signal)
      if (disposed || current.signal.aborted)
        return false
      settings.value = authoritative
      settingsState.value = 'ready'
      isSettingsDirty.value = false
      await invalidateSettings({ exact: true })
      return true
    }
    catch (error) {
      if (!(error instanceof HttpError && error.code === 'aborted')) {
        settingsState.value = forbidden(error) ? 'forbidden' : 'stale'
        settingsFeedbackMessage.value = '彩色聊天设置未保存，请重试。'
      }
      return false
    }
    finally {
      if (mutationController === current)
        mutationController = null
      isSavingSettings.value = false
      isResettingSettings.value = false
    }
  }

  async function mutateProfile(
    request: (header: string, signal: AbortSignal) => Promise<ColoredChatProfile | void>,
  ): Promise<boolean> {
    if (disposed || isMutatingProfile.value)
      return false
    const header = authorization()
    if (header === null)
      return false
    const current = new AbortController()
    mutationController = current
    isMutatingProfile.value = true
    profileFeedbackMessage.value = null
    try {
      await request(header, current.signal)
      if (disposed || current.signal.aborted)
        return false
      await invalidateProfiles({ exact: true })
      await loadProfiles(null, false)
      return profilesState.value === 'ready' || profilesState.value === 'empty'
    }
    catch (error) {
      if (!(error instanceof HttpError && error.code === 'aborted')) {
        if (forbidden(error))
          profilesState.value = 'forbidden'
        profileFeedbackMessage.value = '玩家 Profile 操作失败，列表未作乐观修改。'
      }
      return false
    }
    finally {
      if (mutationController === current)
        mutationController = null
      isMutatingProfile.value = false
    }
  }

  async function filterProfiles(filter: string) {
    profileFilter.value = filter.trim()
    profiles.value = Object.freeze([])
    nextCursor.value = null
    await loadProfiles(null, false)
  }

  function loadMoreProfiles() {
    return nextCursor.value === null ? Promise.resolve() : loadProfiles(nextCursor.value, true)
  }

  function setSettingsDirty(dirty: boolean) {
    isSettingsDirty.value = dirty
  }

  function canLeave(confirmLeave: () => boolean = () => false) {
    return !isSettingsDirty.value || confirmLeave()
  }

  function dispose() {
    disposed = true
    profilesVersion++
    settingsController?.abort()
    profilesController?.abort()
    mutationController?.abort()
  }

  onMounted(() => {
    void loadSettings()
    void loadProfiles(null, false)
  })
  onUnmounted(dispose)

  return {
    settings: readonly(settings),
    settingsState: readonly(settingsState),
    profiles: readonly(profiles),
    profilesState: readonly(profilesState),
    profileFilter: readonly(profileFilter),
    nextCursor: readonly(nextCursor),
    isSavingSettings: readonly(isSavingSettings),
    isResettingSettings: readonly(isResettingSettings),
    isMutatingProfile: readonly(isMutatingProfile),
    settingsFeedbackMessage: readonly(settingsFeedbackMessage),
    profileFeedbackMessage: readonly(profileFeedbackMessage),
    isSettingsDirty: readonly(isSettingsDirty),
    filterProfiles,
    loadMoreProfiles,
    retryProfiles: () => loadProfiles(null, false),
    createProfile: profile => mutateProfile((header, signal) => createProfileRequest(header, profile, signal)),
    updateProfile: profile => mutateProfile((header, signal) => updateProfileRequest(header, profile, signal)),
    deleteProfile: crossplatformId => mutateProfile((header, signal) => deleteProfileRequest(header, crossplatformId, signal)),
    saveSettings: draft => mutateSettings('save', draft),
    resetSettings: () => mutateSettings('reset'),
    setSettingsDirty,
    canLeave,
    dispose,
  }
}
