<script setup lang="ts">
import type { OnlinePlayersState } from '../model/useOnlinePlayers'
import { useI18n } from 'vue-i18n'

defineProps<{
  count: number
  isRefreshing: boolean
  state: OnlinePlayersState
}>()

defineEmits<{
  refresh: []
}>()

const { t } = useI18n()
</script>

<template>
  <UDashboardNavbar :title="t('players.title')">
    <template #leading>
      <UDashboardSidebarCollapse />
    </template>

    <template #right>
      <UTooltip :text="t('players.refresh')">
        <UButton
          :aria-label="t('players.refresh')"
          class="size-8"
          color="neutral"
          icon="i-lucide-refresh-cw"
          square
          variant="ghost"
          :disabled="isRefreshing"
          :ui="{ leadingIcon: isRefreshing ? 'animate-spin' : '' }"
          @click="$emit('refresh')"
        />
      </UTooltip>
    </template>
  </UDashboardNavbar>

  <UDashboardToolbar>
    <template #left>
      <div class="flex min-w-0 flex-wrap items-center gap-x-4 gap-y-2 text-sm">
        <span class="font-medium text-highlighted">{{ t('players.onlineCount', { count }) }}</span>
        <UBadge v-if="state === 'stale'" color="warning" variant="subtle">
          {{ t('players.refreshStale') }}
        </UBadge>
      </div>
    </template>
  </UDashboardToolbar>
</template>
