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
import { useRouter } from 'vue-router'

import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { ChatHistoryView, useChatHistory } from '../../../features/game-chat'

const router = useRouter()
const { t } = useI18n()
const history = useChatHistory({
  onSessionExpired: () => router.replace({
    path: '/login',
    query: { redirect: '/community/chat/history' },
  }),
})
</script>

<template>
  <NavigationSectionTabs />
  <UDashboardPanel id="game-chat-history">
    <template #header>
      <UDashboardNavbar :title="t('gameChat.history.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>
    <div class="overflow-y-auto p-4 sm:p-6">
      <ChatHistoryView
        :state="history.state.value"
        :messages="history.messages.value"
        :filters="history.filters.value"
        :next-cursor="history.nextCursor.value"
        :is-loading-more="history.isLoadingMore.value"
        @apply-filters="history.applyFilters"
        @load-more="history.loadMore"
        @retry="history.retry"
      />
    </div>
  </UDashboardPanel>
</template>
