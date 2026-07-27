<script setup lang="ts">
import type { GameOverview, HostOverview } from '../model/overview'
import type { OverviewStatus } from '../model/useOverview'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { formatDuration, formatNumber } from './formatOverview'
import GameRuntimeMetricsPanel from './GameRuntimeMetricsPanel.vue'

const props = defineProps<{
  status: OverviewStatus
  game: GameOverview | null
  host: HostOverview | null
}>()
const emit = defineEmits<{ refresh: [] }>()
const { d, locale, t } = useI18n()

const statusColor = computed(() => ({ fresh: 'success', partial: 'warning', stale: 'warning', offline: 'error', loading: 'neutral' } as const)[props.status])
const sampledAt = computed(() => props.game?.sampledAtUtc ?? props.host?.sampledAtUtc ?? null)
const sampleLabel = computed(() => sampledAt.value === null ? '' : d(new Date(sampledAt.value), 'medium'))
const players = computed(() => {
  const online = props.game?.runtimeMetrics?.onlinePlayerCount.value ?? props.game?.onlinePlayerCount
  const maximum = props.game?.maximumPlayerCount
  return online === null || online === undefined ? '—' : maximum === null || maximum === undefined ? String(online) : `${online} / ${maximum}`
})
const framesPerSecond = computed(() => props.game?.runtimeMetrics?.framesPerSecond.value ?? props.game?.framesPerSecond ?? null)
const gameDayTime = computed(() => props.game?.runtimeMetrics?.gameDayTime.value ?? props.game?.gameTime ?? null)
function uptime(seconds: number | null): string {
  return formatDuration(seconds, {
    day: count => t('overview.duration.day', { count }),
    hour: count => t('overview.duration.hour', { count }),
    minute: count => t('overview.duration.minute', { count }),
  })
}
</script>

<template>
  <UCard class="rounded-md">
    <template #header>
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div class="flex items-center gap-2">
          <h2 class="font-semibold text-highlighted">{{ t('overview.status.title') }}</h2>
          <UBadge :color="statusColor" variant="subtle">{{ t(`overview.status.${status}Title`) }}</UBadge>
        </div>
        <UButton data-testid="overview-refresh" color="neutral" variant="outline" icon="i-lucide-refresh-cw" :label="t('overview.status.refresh')" @click="emit('refresh')" />
      </div>
    </template>

    <div v-if="status === 'loading'" class="grid gap-3 sm:grid-cols-3 lg:grid-cols-5">
      <div v-for="index in 5" :key="index" data-testid="skeleton"><USkeleton class="h-14 rounded-md" /></div>
    </div>
    <template v-else>
      <UAlert v-if="status !== 'fresh'" class="mb-4" :color="status === 'offline' ? 'error' : 'warning'" variant="subtle" :title="t(`overview.status.${status}Title`)" :description="t(`overview.status.${status}Description`)" />
      <p v-if="status === 'stale' && sampleLabel" class="mb-4 text-xs text-dimmed">{{ t('overview.status.lastSample', { time: sampleLabel }) }}</p>
      <dl class="grid gap-x-6 gap-y-4 sm:grid-cols-3 lg:grid-cols-5">
        <div><dt class="text-xs text-muted">{{ t('overview.metrics.players') }}</dt><dd class="mt-1 font-medium text-highlighted">{{ players }}</dd></div>
        <div><dt class="text-xs text-muted">{{ t('overview.metrics.fps') }}</dt><dd class="mt-1 font-medium text-highlighted">{{ formatNumber(framesPerSecond, locale) }} FPS</dd></div>
        <div><dt class="text-xs text-muted">{{ t('overview.metrics.worldSession') }}</dt><dd class="mt-1 font-medium text-highlighted">{{ uptime(game?.worldSessionUptimeSeconds ?? null) }}</dd></div>
        <div><dt class="text-xs text-muted">{{ t('overview.metrics.process') }}</dt><dd class="mt-1 font-medium text-highlighted">{{ uptime(host?.processUptimeSeconds ?? null) }}</dd></div>
        <div><dt class="text-xs text-muted">{{ t('overview.metrics.gameTime') }}</dt><dd class="mt-1 font-medium text-highlighted">{{ gameDayTime ?? '—' }}</dd></div>
      </dl>
    </template>
  </UCard>

  <GameRuntimeMetricsPanel
    v-if="status !== 'loading'"
    :availability="game?.availability ?? 'unavailable'"
    :metrics="game?.runtimeMetrics ?? null"
    :stale="status === 'stale'"
  />
</template>
