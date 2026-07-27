<script setup lang="ts">
import type {
  ColoredChatProfile,
  ColoredChatProfileDraft,
  ColoredChatSettings,
  GameChatManagementState,
  PlayerColorTagPermission,
} from '../model/gameChatManagement'

import { computed, reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import {
  normalizeChatColor,
  playerColorTagPermissionOptions,
  toChatColorPickerValue,
} from '../model/gameChatManagement'
import ColoredChatPreview from './ColoredChatPreview.vue'
import ColoredChatProfileDialog from './ColoredChatProfileDialog.vue'

const props = defineProps<{
  profiles: readonly ColoredChatProfile[]
  profilesState: GameChatManagementState
  profileFilter: string
  nextCursor: string | null
  settings: ColoredChatSettings
  isSavingSettings: boolean
  isResettingSettings: boolean
  isMutatingProfile: boolean
  settingsFeedbackMessage?: string | null
  profileFeedbackMessage?: string | null
}>()

const emit = defineEmits<{
  filterProfiles: [filter: string]
  loadMoreProfiles: []
  retryProfiles: []
  createProfile: [profile: ColoredChatProfileDraft]
  updateProfile: [profile: ColoredChatProfileDraft]
  deleteProfile: [crossplatformId: string]
  saveSettings: [settings: ColoredChatSettings]
  resetSettings: []
  settingsDirtyChange: [dirty: boolean]
}>()
const { t } = useI18n()

const tabs = computed(() => [
  { label: t('gameChat.colored.tabs.profiles'), value: 'profiles', slot: 'profiles' as const, icon: 'i-lucide-users' },
  { label: t('gameChat.colored.tabs.defaults'), value: 'defaults', slot: 'defaults' as const, icon: 'i-lucide-palette' },
])
const playerColorTagPermissionSelectItems = computed(() => playerColorTagPermissionOptions.map(value => ({
  label: t(`gameChat.permissions.${value}`),
  value,
})))
const activeTab = shallowRef('profiles')
const filterDraft = shallowRef('')
const profileDialogOpen = shallowRef(false)
const profileDialogMode = shallowRef<'create' | 'edit'>('create')
const selectedProfile = shallowRef<ColoredChatProfile | null>(null)
const deleteTarget = shallowRef<ColoredChatProfile | null>(null)
const settingsDraft = reactive({
  isEnabled: true,
  globalDefaultColor: '',
  whisperDefaultColor: '',
  friendsDefaultColor: '',
  partyDefaultColor: '',
  adminDefaultColor: '',
  systemDefaultColor: '',
  playerColorTagPermission: 'None' as PlayerColorTagPermission,
})
let syncingSettings = false

watch(() => props.profileFilter, filter => {
  filterDraft.value = filter
}, { immediate: true })

watch(() => props.settings, (settings) => {
  syncingSettings = true
  Object.assign(settingsDraft, {
    ...settings,
    globalDefaultColor: settings.globalDefaultColor ?? '',
    whisperDefaultColor: settings.whisperDefaultColor ?? '',
    friendsDefaultColor: settings.friendsDefaultColor ?? '',
    partyDefaultColor: settings.partyDefaultColor ?? '',
    adminDefaultColor: settings.adminDefaultColor ?? '',
    systemDefaultColor: settings.systemDefaultColor ?? '',
  })
  queueMicrotask(() => {
    syncingSettings = false
    emit('settingsDirtyChange', false)
  })
}, { immediate: true, deep: true })

watch(settingsDraft, () => {
  if (!syncingSettings)
    emit('settingsDirtyChange', true)
}, { deep: true })

watch(() => props.profiles, (profiles) => {
  if (deleteTarget.value && !profiles.some(profile => profile.crossplatformId === deleteTarget.value?.crossplatformId))
    deleteTarget.value = null
  if (selectedProfile.value && !profiles.some(profile => profile.crossplatformId === selectedProfile.value?.crossplatformId)) {
    selectedProfile.value = null
    profileDialogOpen.value = false
  }
}, { deep: true })

function openCreateProfile() {
  profileDialogMode.value = 'create'
  selectedProfile.value = null
  profileDialogOpen.value = true
}

function openEditProfile(profile: ColoredChatProfile) {
  profileDialogMode.value = 'edit'
  selectedProfile.value = profile
  profileDialogOpen.value = true
}

function submitProfile(profile: ColoredChatProfileDraft) {
  if (profileDialogMode.value === 'create')
    emit('createProfile', profile)
  else
    emit('updateProfile', profile)
}

function saveSettings() {
  const colors = {
    globalDefaultColor: normalizeChatColor(settingsDraft.globalDefaultColor),
    whisperDefaultColor: normalizeChatColor(settingsDraft.whisperDefaultColor),
    friendsDefaultColor: normalizeChatColor(settingsDraft.friendsDefaultColor),
    partyDefaultColor: normalizeChatColor(settingsDraft.partyDefaultColor),
    adminDefaultColor: normalizeChatColor(settingsDraft.adminDefaultColor),
    systemDefaultColor: normalizeChatColor(settingsDraft.systemDefaultColor),
  }
  if (Object.values(colors).some(color => color === undefined))
    return

  emit('saveSettings', {
    isEnabled: settingsDraft.isEnabled,
    globalDefaultColor: colors.globalDefaultColor ?? null,
    whisperDefaultColor: colors.whisperDefaultColor ?? null,
    friendsDefaultColor: colors.friendsDefaultColor ?? null,
    partyDefaultColor: colors.partyDefaultColor ?? null,
    adminDefaultColor: colors.adminDefaultColor ?? null,
    systemDefaultColor: colors.systemDefaultColor ?? null,
    playerColorTagPermission: settingsDraft.playerColorTagPermission,
  })
}

function confirmDeleteProfile() {
  if (deleteTarget.value)
    emit('deleteProfile', deleteTarget.value.crossplatformId)
}
</script>

<template>
  <section class="space-y-4" aria-labelledby="colored-chat-title">
    <header>
      <h1 id="colored-chat-title" class="text-lg font-semibold text-highlighted">
        {{ t('gameChat.colored.title') }}
      </h1>
      <p class="text-sm text-muted">
        {{ t('gameChat.colored.description') }}
      </p>
    </header>

    <UTabs v-model="activeTab" :items="tabs" class="w-full">
      <template #profiles>
        <div class="space-y-4 pt-4">
          <div class="flex flex-wrap items-end justify-between gap-3">
            <UFormField :label="t('gameChat.colored.profiles.filterLabel')" name="profileFilter" class="min-w-64 flex-1">
              <div class="flex gap-2">
                <UInput v-model="filterDraft" data-testid="profile-filter" class="w-full" :placeholder="t('gameChat.colored.profiles.filterPlaceholder')" />
                <UButton
                  data-testid="apply-profile-filter"
                  color="neutral"
                  variant="outline"
                  icon="i-lucide-search"
                  :label="t('gameChat.colored.profiles.filter')"
                  @click="emit('filterProfiles', filterDraft.trim())"
                />
              </div>
            </UFormField>
            <UButton data-testid="create-profile" icon="i-lucide-plus" :label="t('gameChat.colored.profiles.create')" @click="openCreateProfile" />
          </div>

          <div v-if="profilesState === 'loading'" class="space-y-3" :aria-label="t('gameChat.colored.profiles.loading')">
            <USkeleton v-for="row in 4" :key="row" class="h-20 w-full" />
          </div>
          <UAlert
            v-else-if="profilesState === 'failed' || profilesState === 'forbidden'"
            :color="profilesState === 'forbidden' ? 'warning' : 'error'"
            :title="profilesState === 'forbidden' ? t('gameChat.colored.forbidden') : t('gameChat.colored.profiles.failed')"
          >
            <template #actions>
              <UButton v-if="profilesState === 'failed'" :label="t('gameChat.common.retry')" color="neutral" variant="outline" @click="emit('retryProfiles')" />
            </template>
          </UAlert>
          <div v-else-if="profilesState === 'empty'" class="rounded-lg border border-dashed border-default py-12 text-center text-sm text-muted">
            {{ t('gameChat.colored.profiles.empty') }}
          </div>
          <template v-else>
            <UAlert v-if="profilesState === 'stale'" color="warning" :title="t('gameChat.common.staleTitle')" />
            <ul class="divide-y divide-default rounded-lg border border-default px-4">
              <li v-for="profile in profiles" :key="profile.crossplatformId" class="flex flex-wrap items-start justify-between gap-4 py-4">
                <div class="min-w-0 flex-1 space-y-2">
                  <code class="block break-all text-sm font-medium text-highlighted">{{ profile.crossplatformId }}</code>
                  <p class="whitespace-pre-wrap wrap-break-word text-sm">{{ profile.customName ?? '{playerName}' }}</p>
                  <p v-if="profile.description" class="whitespace-pre-wrap wrap-break-word text-xs text-muted">{{ profile.description }}</p>
                  <div class="flex flex-wrap gap-2 text-xs text-muted">
                    <span>{{ t('gameChat.colored.profiles.nameColor') }}：{{ profile.nameColor ?? t('gameChat.common.defaultValue') }}</span>
                    <span>{{ t('gameChat.colored.profiles.textColor') }}：{{ profile.textColor ?? t('gameChat.common.defaultValue') }}</span>
                    <span>{{ t('gameChat.colored.profiles.updated') }}：{{ profile.updatedAtUtc }}</span>
                  </div>
                </div>
                <div class="flex shrink-0 gap-2">
                  <UButton
                    :data-testid="`edit-profile-${profile.crossplatformId}`"
                    color="neutral"
                    variant="outline"
                    icon="i-lucide-pencil"
                    :label="t('gameChat.common.edit')"
                    @click="openEditProfile(profile)"
                  />
                  <UButton
                    :data-testid="`delete-profile-${profile.crossplatformId}`"
                    color="error"
                    variant="outline"
                    icon="i-lucide-trash-2"
                    :label="t('gameChat.common.delete')"
                    @click="deleteTarget = profile"
                  />
                </div>
              </li>
            </ul>
            <div v-if="nextCursor" class="flex justify-center">
              <UButton
                data-testid="profiles-load-more"
                color="neutral"
                variant="outline"
                icon="i-lucide-chevron-down"
                :label="t('gameChat.common.loadMore')"
                :disabled="isMutatingProfile"
                @click="emit('loadMoreProfiles')"
              />
            </div>
          </template>
        </div>
      </template>

      <template #defaults>
        <UForm
          data-testid="colored-settings-form"
          :state="settingsDraft"
          class="space-y-5 pt-4"
          @submit="saveSettings"
        >
          <section class="space-y-4 rounded-lg border border-default p-4">
            <UFormField :label="t('gameChat.colored.defaults.enabled')" name="isEnabled">
              <USwitch v-model="settingsDraft.isEnabled" :label="t('gameChat.colored.defaults.enabledDescription')" :disabled="isSavingSettings || isResettingSettings" />
            </UFormField>
            <UFormField :label="t('gameChat.colored.defaults.permission')" name="playerColorTagPermission">
              <USelect
                v-model="settingsDraft.playerColorTagPermission"
                :items="playerColorTagPermissionSelectItems"
                class="w-full md:w-72"
                :disabled="isSavingSettings || isResettingSettings"
              />
            </UFormField>
          </section>

          <section class="grid gap-4 rounded-lg border border-default p-4 md:grid-cols-2 xl:grid-cols-3">
            <UFormField :label="t('gameChat.colored.defaults.globalColor')" name="globalDefaultColor" :hint="t('gameChat.common.mayBeEmpty')">
              <div class="space-y-2"><UColorPicker :model-value="toChatColorPickerValue(settingsDraft.globalDefaultColor)" format="hex" @update:model-value="settingsDraft.globalDefaultColor = $event ?? ''" /><UInput v-model="settingsDraft.globalDefaultColor" data-testid="global-default-color-input" class="w-full font-mono" placeholder="RRGGBB" /></div>
            </UFormField>
            <UFormField :label="t('gameChat.colored.defaults.whisperColor')" name="whisperDefaultColor" :hint="t('gameChat.common.mayBeEmpty')">
              <div class="space-y-2"><UColorPicker :model-value="toChatColorPickerValue(settingsDraft.whisperDefaultColor)" format="hex" @update:model-value="settingsDraft.whisperDefaultColor = $event ?? ''" /><UInput v-model="settingsDraft.whisperDefaultColor" class="w-full font-mono" placeholder="RRGGBB" /></div>
            </UFormField>
            <UFormField :label="t('gameChat.colored.defaults.friendsColor')" name="friendsDefaultColor" :hint="t('gameChat.common.mayBeEmpty')">
              <div class="space-y-2"><UColorPicker :model-value="toChatColorPickerValue(settingsDraft.friendsDefaultColor)" format="hex" @update:model-value="settingsDraft.friendsDefaultColor = $event ?? ''" /><UInput v-model="settingsDraft.friendsDefaultColor" class="w-full font-mono" placeholder="RRGGBB" /></div>
            </UFormField>
            <UFormField :label="t('gameChat.colored.defaults.partyColor')" name="partyDefaultColor" :hint="t('gameChat.common.mayBeEmpty')">
              <div class="space-y-2"><UColorPicker :model-value="toChatColorPickerValue(settingsDraft.partyDefaultColor)" format="hex" @update:model-value="settingsDraft.partyDefaultColor = $event ?? ''" /><UInput v-model="settingsDraft.partyDefaultColor" class="w-full font-mono" placeholder="RRGGBB" /></div>
            </UFormField>
            <UFormField :label="t('gameChat.colored.defaults.adminColor')" name="adminDefaultColor" :hint="t('gameChat.common.mayBeEmpty')">
              <div class="space-y-2"><UColorPicker :model-value="toChatColorPickerValue(settingsDraft.adminDefaultColor)" format="hex" @update:model-value="settingsDraft.adminDefaultColor = $event ?? ''" /><UInput v-model="settingsDraft.adminDefaultColor" class="w-full font-mono" placeholder="RRGGBB" /></div>
            </UFormField>
            <UFormField :label="t('gameChat.colored.defaults.systemColor')" name="systemDefaultColor" :hint="t('gameChat.common.mayBeEmpty')">
              <div class="space-y-2"><UColorPicker :model-value="toChatColorPickerValue(settingsDraft.systemDefaultColor)" format="hex" @update:model-value="settingsDraft.systemDefaultColor = $event ?? ''" /><UInput v-model="settingsDraft.systemDefaultColor" class="w-full font-mono" placeholder="RRGGBB" /></div>
            </UFormField>
          </section>

          <ColoredChatPreview
            :name-color="settingsDraft.globalDefaultColor"
            :text-color="settingsDraft.globalDefaultColor"
          />
          <p v-if="settingsFeedbackMessage" role="status" class="text-sm text-error">{{ t(settingsFeedbackMessage) }}</p>
          <div class="flex justify-end gap-2">
            <UButton
              type="button"
              color="neutral"
              variant="outline"
              :label="t('gameChat.common.resetDefaults')"
              :loading="isResettingSettings"
              :disabled="isSavingSettings || isResettingSettings"
              @click="emit('resetSettings')"
            />
            <UButton
              type="submit"
              icon="i-lucide-save"
              :label="t('gameChat.colored.defaults.save')"
              :loading="isSavingSettings"
              :disabled="isSavingSettings || isResettingSettings"
            />
          </div>
        </UForm>
      </template>
    </UTabs>

    <ColoredChatProfileDialog
      v-model:open="profileDialogOpen"
      :mode="profileDialogMode"
      :profile="selectedProfile"
      :is-submitting="isMutatingProfile"
      :feedback-message="profileFeedbackMessage"
      @submit="submitProfile"
    />

    <UModal
      :open="deleteTarget !== null"
      :title="t('gameChat.colored.dialog.deleteTitle')"
      :description="t('gameChat.colored.dialog.deleteDescription')"
      :dismissible="!isMutatingProfile"
      :close="isMutatingProfile ? false : undefined"
      @update:open="open => { if (!open && !isMutatingProfile) deleteTarget = null }"
    >
      <template #body>
        <p class="break-all text-sm text-default">{{ deleteTarget?.crossplatformId }}</p>
      </template>
      <template #footer>
        <UButton type="button" color="neutral" variant="outline" :label="t('gameChat.common.cancel')" :disabled="isMutatingProfile" @click="deleteTarget = null" />
        <UButton
          data-testid="confirm-delete-profile"
          type="button"
          color="error"
          icon="i-lucide-trash-2"
          :label="t('gameChat.colored.dialog.deleteConfirm')"
          :loading="isMutatingProfile"
          :disabled="isMutatingProfile"
          @click="confirmDeleteProfile"
        />
      </template>
    </UModal>
  </section>
</template>
