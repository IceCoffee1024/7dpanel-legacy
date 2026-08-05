<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { onBeforeRouteLeave, useRouter } from 'vue-router'

import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { ColoredChatView, useColoredChat } from '../../../features/game-chat'

const router = useRouter()
const { t } = useI18n()
const colored = useColoredChat({
  onSessionExpired: () => router.replace({
    path: '/login',
    query: { redirect: '/community/chat/appearance' },
  }),
})

onBeforeRouteLeave(() => colored.canLeave())
</script>

<template>
  <NavigationSectionTabs />
  <UDashboardPanel id="game-chat-colored">
    <template #header>
      <UDashboardNavbar :title="t('gameChat.colored.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>
    <div class="overflow-y-auto p-4 sm:p-6">
      <UAlert
        v-if="colored.settingsState.value === 'failed' || colored.settingsState.value === 'forbidden'"
        class="mb-4"
        :color="colored.settingsState.value === 'forbidden' ? 'warning' : 'error'"
        :title="colored.settingsState.value === 'forbidden' ? t('gameChat.colored.forbidden') : t('gameChat.colored.settingsFailed')"
      />
      <ColoredChatView
        :profiles="colored.profiles.value"
        :profiles-state="colored.profilesState.value"
        :profile-filter="colored.profileFilter.value"
        :next-cursor="colored.nextCursor.value"
        :settings="colored.settings.value"
        :is-saving-settings="colored.isSavingSettings.value"
        :is-resetting-settings="colored.isResettingSettings.value"
        :is-mutating-profile="colored.isMutatingProfile.value"
        :settings-feedback-message="colored.settingsFeedbackMessage.value"
        :profile-feedback-message="colored.profileFeedbackMessage.value"
        @filter-profiles="colored.filterProfiles"
        @load-more-profiles="colored.loadMoreProfiles"
        @retry-profiles="colored.retryProfiles"
        @create-profile="colored.createProfile"
        @update-profile="colored.updateProfile"
        @delete-profile="colored.deleteProfile"
        @save-settings="colored.saveSettings"
        @reset-settings="colored.resetSettings"
        @settings-dirty-change="colored.setSettingsDirty"
      />
    </div>
  </UDashboardPanel>
</template>
