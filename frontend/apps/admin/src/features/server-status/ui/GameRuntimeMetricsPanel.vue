<script setup lang="ts">
import type {
  Availability,
  GameRuntimeMetrics,
  ObservedRuntimeMetric,
} from '../model/overview'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import { formatRuntimeMetricValue } from './formatOverview'

const props = defineProps<{
  metrics: GameRuntimeMetrics | null
  availability: Availability
  stale: boolean
}>()

const { d, locale, t } = useI18n()

const definitions = [
  ['gameDayTime', 'gameDayTime'],
  ['isBloodMoon', 'isBloodMoon'],
  ['framesPerSecond', 'framesPerSecond'],
  ['onlinePlayerCount', 'onlinePlayerCount'],
  ['historicalPlayerCount', 'historicalPlayerCount'],
  ['animalCount', 'animalCount'],
  ['hostileEntityCount', 'hostileEntityCount'],
  ['activeEntityCount', 'activeEntityCount'],
  ['chunkCount', 'chunkCount'],
  ['droppedItemCount', 'droppedItemCount'],
  ['gameMemoryBytes', 'gameMemoryBytes'],
] as const

type RuntimeMetricKey = (typeof definitions)[number][0]
type RuntimeMetric = ObservedRuntimeMetric<string | number | boolean>

const rows = computed(() => props.metrics === null
  ? []
  : definitions.map(([key, labelKey]) => ({
      key,
      labelKey,
      metric: props.metrics?.[key] as RuntimeMetric,
    })))
const isStale = computed(() => props.stale || props.availability === 'stale')

const unitKeys: Record<string, string> = {
  'game-clock': 'gameClock',
  'boolean': 'boolean',
  'frames/second': 'framesPerSecond',
  'count': 'count',
  'bytes': 'bytes',
}

function valueLabel(metric: RuntimeMetric): string {
  return formatRuntimeMetricValue(metric.value, locale.value, {
    falseLabel: t('overview.runtimeMetrics.boolean.false'),
    trueLabel: t('overview.runtimeMetrics.boolean.true'),
  })
}

function unitLabel(unit: string): string {
  const key = unitKeys[unit]
  return key === undefined ? unit : t(`overview.runtimeMetrics.units.${key}`)
}

function warningLabel(metric: RuntimeMetric): string | null {
  return metric.warning === null
    ? null
    : t(`overview.runtimeMetrics.warning.${metric.warning}`)
}

function observedAtLabel(metric: RuntimeMetric): string {
  return d(new Date(metric.observedAtUtc), 'medium')
}

function testId(key: RuntimeMetricKey): string {
  return `runtime-metric-${key}`
}
</script>

<template>
  <UCard class="rounded-md">
    <template #header>
      <h2 class="font-semibold text-highlighted">
        {{ t('overview.runtimeMetrics.title') }}
      </h2>
    </template>

    <UAlert
      v-if="metrics === null"
      color="warning"
      data-testid="runtime-metrics-unavailable"
      variant="subtle"
      :title="t('overview.runtimeMetrics.unavailableTitle')"
      :description="t('overview.runtimeMetrics.unavailableDescription')"
    />

    <template v-else>
      <UAlert
        v-if="isStale"
        class="mb-4"
        color="warning"
        data-testid="runtime-metrics-stale"
        variant="subtle"
        :title="t('overview.runtimeMetrics.staleTitle')"
        :description="t('overview.runtimeMetrics.staleDescription')"
      />

      <div
        class="grid min-w-0 grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-3"
        data-testid="runtime-metrics-grid"
      >
        <article
          v-for="row in rows"
          :key="row.key"
          class="min-w-0 rounded-lg border border-muted bg-elevated/50 p-3"
          :data-testid="testId(row.key)"
        >
          <h3 class="text-xs font-medium text-muted">
            {{ t(`overview.runtimeMetrics.metrics.${row.labelKey}`) }}
          </h3>
          <div class="mt-1 flex min-w-0 flex-wrap items-baseline gap-2">
            <strong class="break-words text-lg text-highlighted">{{ valueLabel(row.metric) }}</strong>
            <span class="text-xs text-muted" data-testid="runtime-unit">{{ unitLabel(row.metric.unit) }}</span>
            <UBadge
              v-if="row.metric.warning"
              color="warning"
              size="xs"
              variant="subtle"
            >
              {{ warningLabel(row.metric) }}
            </UBadge>
          </div>
          <dl class="mt-3 min-w-0 space-y-1 text-xs text-dimmed">
            <div class="min-w-0">
              <dt class="inline">
                {{ t('overview.runtimeMetrics.source') }}：
              </dt>
              <dd class="inline break-all font-mono">
                {{ row.metric.source }}
              </dd>
            </div>
            <div class="min-w-0">
              <dt class="inline">
                {{ t('overview.runtimeMetrics.observedAt') }}：
              </dt>
              <dd class="inline break-words">
                <time :datetime="row.metric.observedAtUtc">{{ observedAtLabel(row.metric) }}</time>
              </dd>
            </div>
          </dl>
        </article>
      </div>
    </template>
  </UCard>
</template>
