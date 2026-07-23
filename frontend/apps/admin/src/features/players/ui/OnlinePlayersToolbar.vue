<script setup lang="ts">
import type { OnlinePlayersState } from '../model/useOnlinePlayers'

defineProps<{
  count: number
  isRefreshing: boolean
  state: OnlinePlayersState
}>()

defineEmits<{
  refresh: []
}>()
</script>

<template>
  <UDashboardNavbar title="在线玩家">
    <template #leading>
      <UDashboardSidebarCollapse />
    </template>

    <template #right>
      <UTooltip text="刷新在线玩家">
        <UButton
          aria-label="刷新在线玩家"
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
        <span class="font-medium text-highlighted">在线 {{ count }} 人</span>
        <UBadge v-if="state === 'stale'" color="warning" variant="subtle">
          刷新失败，显示上次结果
        </UBadge>
      </div>
    </template>
  </UDashboardToolbar>
</template>
