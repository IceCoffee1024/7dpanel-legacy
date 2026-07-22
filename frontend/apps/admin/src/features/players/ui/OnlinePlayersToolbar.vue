<script setup lang="ts">
import type { OnlinePlayersState } from '../model/useOnlinePlayers'

import { computed } from 'vue'

const props = defineProps<{
  count: number
  capturedAtUtc?: string
  isRefreshing: boolean
  state: OnlinePlayersState
}>()

defineEmits<{
  refresh: []
}>()

const capturedAtLabel = computed(() => props.capturedAtUtc === undefined
  ? ''
  : new Intl.DateTimeFormat('zh-CN', {
      dateStyle: 'medium',
      timeStyle: 'medium',
    }).format(new Date(props.capturedAtUtc)))

const freshness = computed(() => props.state === 'stale'
  ? { icon: 'i-lucide-clock-alert', label: '数据已过期', color: 'warning' as const }
  : { icon: 'i-lucide-circle-check', label: '数据为最新', color: 'success' as const })
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

  <UDashboardToolbar v-if="capturedAtLabel">
    <template #left>
      <div class="flex min-w-0 flex-wrap items-center gap-x-4 gap-y-2 text-sm">
        <span class="font-medium text-highlighted">在线 {{ count }} 人</span>
        <span class="text-muted">捕获于 {{ capturedAtLabel }}</span>
        <UBadge :color="freshness.color" variant="subtle">
          <UIcon :name="freshness.icon" class="size-4" />
          {{ freshness.label }}
        </UBadge>
      </div>
    </template>
  </UDashboardToolbar>
</template>
