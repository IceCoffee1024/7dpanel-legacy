<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { onBeforeRouteLeave, useRouter } from 'vue-router'

import { ChatSettingsView, useChatSettings } from '../../features/game-chat'

const router = useRouter()
const { t } = useI18n()
const settings = useChatSettings({
  onSessionExpired: () => router.replace({
    path: '/login',
    query: { redirect: '/game-chat/settings' },
  }),
})
const editableSettings = computed(() => ({
  ...settings.settings.value,
  commandPrefixes: [...settings.settings.value.commandPrefixes],
}))

onBeforeRouteLeave(() => settings.canLeave())
</script>

<template>
  <UDashboardPanel id="game-chat-settings">
    <template #header>
      <UDashboardNavbar :title="t('gameChat.settings.title')">
        <template #leading><UDashboardSidebarCollapse /></template>
      </UDashboardNavbar>
    </template>
    <div class="overflow-y-auto p-4 sm:p-6">
      <div v-if="settings.state.value === 'loading'" class="space-y-4" :aria-label="t('gameChat.settings.loading')">
        <USkeleton class="h-28 w-full" />
        <USkeleton class="h-52 w-full" />
      </div>
      <UAlert
        v-else-if="settings.state.value === 'failed' || settings.state.value === 'forbidden'"
        :color="settings.state.value === 'forbidden' ? 'warning' : 'error'"
        :title="settings.state.value === 'forbidden' ? t('gameChat.settings.forbidden') : t('gameChat.settings.failed')"
      >
        <template #actions>
          <UButton v-if="settings.state.value === 'failed'" :label="t('common.reload')" color="neutral" variant="outline" @click="settings.load" />
        </template>
      </UAlert>
      <ChatSettingsView
        v-else
        :settings="editableSettings"
        :is-saving="settings.isSaving.value"
        :is-resetting="settings.isResetting.value"
        :feedback-message="settings.feedbackMessage.value"
        @save="settings.save"
        @reset="settings.reset"
        @dirty-change="settings.setDirty"
      />
    </div>
  </UDashboardPanel>
</template>
