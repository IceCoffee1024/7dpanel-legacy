<script setup lang="ts">
import type { AreaGeometry, AreaInvestigationController } from '../model/useAreaInvestigation'

import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import { MAX_AREA_INVESTIGATION_LIMIT } from '../model/useAreaInvestigation'

const props = defineProps<{
  investigation: AreaInvestigationController
  mode: AreaGeometry['kind']
  limit: number
}>()

const emit = defineEmits<{
  'update:mode': [mode: AreaGeometry['kind']]
  'update:limit': [limit: number]
  drawGeometry: [mode: AreaGeometry['kind']]
  modifyGeometry: [geometry: AreaGeometry]
  clearGeometry: []
  selectResult: [combinedId: string]
  openHistoryProfile: [combinedId: string]
  loadHistoryTrack: [combinedId: string]
}>()

const { locale } = useI18n()
const fromUtcInput = shallowRef('')
const toUtcInput = shallowRef('')
const localError = shallowRef<string | null>(null)

const copy = {
  en: {
    title: 'Area investigation',
    rectangle: 'Rectangle',
    circle: 'Circle',
    modify: 'Adjust area',
    noArea: 'Draw an area on the map to begin.',
    from: 'From (UTC)',
    to: 'To (UTC)',
    limit: 'Result limit',
    search: 'Search',
    cancel: 'Cancel',
    clear: 'Clear',
    required: 'An area and valid UTC time range are required.',
    idle: 'Choose an area and UTC time range.',
    loading: 'Searching retained observations…',
    empty: 'No matching players were found.',
    failed: 'Area investigation could not be loaded.',
    matches: 'Matching observations',
    candidates: 'Candidate observations',
    first: 'First match',
    last: 'Last match',
    position: 'Last matching position',
    profile: 'History profile',
    track: 'Load track',
    candidateTruncated: 'Candidate observations were truncated.',
    playerTruncated: 'Player results reached the limit.',
    caveat: 'A match means at least one retained observation was inside the area; it does not prove continuous presence, entry path, or position between samples.',
  },
  zh: {
    title: '区域调查',
    rectangle: '矩形',
    circle: '圆形',
    modify: '调整区域',
    noArea: '请先在地图上绘制调查区域。',
    from: '开始时间（UTC）',
    to: '结束时间（UTC）',
    limit: '结果上限',
    search: '查询',
    cancel: '取消',
    clear: '清除',
    required: '需要调查区域和有效的 UTC 时间范围。',
    idle: '请选择区域和 UTC 时间范围。',
    loading: '正在查询已保留的位置观察…',
    empty: '没有找到命中玩家。',
    failed: '无法加载区域调查结果。',
    matches: '命中观察',
    candidates: '候选观察',
    first: '首次命中',
    last: '最后命中',
    position: '最后命中坐标',
    profile: '历史资料',
    track: '载入轨迹',
    candidateTruncated: '候选观察已截断。',
    playerTruncated: '玩家结果已达到上限。',
    caveat: '命中仅表示至少一条已保留观察位于区域内；不证明持续停留、进入路径或采样间隔内的位置。',
  },
} as const

const labels = computed(() => locale.value === 'zh-CN' ? copy.zh : copy.en)
const geometry = computed(() => props.investigation.geometry.value)
const state = computed(() => props.investigation.state.value)
const players = computed(() => props.investigation.players.value)
const selectedCombinedId = computed(() => props.investigation.selectedCombinedId.value)
const error = computed(() => localError.value ?? props.investigation.error.value)
const isLoading = computed(() => state.value === 'loading')
const canSearch = computed(() => geometry.value !== null
  && fromUtcInput.value !== ''
  && toUtcInput.value !== ''
  && !isLoading.value)

watch(
  () => props.investigation.timeRange.value,
  (range) => {
    fromUtcInput.value = range === null ? '' : toUtcControlValue(range.fromUtc)
    toUtcInput.value = range === null ? '' : toUtcControlValue(range.toUtc)
  },
  { immediate: true },
)

function toUtcControlValue(value: string): string {
  return value.replace(/\.\d{3}Z$/, 'Z').replace(/Z$/, '').slice(0, 19)
}

function utcControlValue(value: string): string | null {
  if (value === '')
    return null
  const normalized = value.length === 16 ? `${value}:00` : value
  const candidate = `${normalized}.000Z`
  return Number.isNaN(Date.parse(candidate)) ? null : candidate
}

function chooseMode(mode: AreaGeometry['kind']) {
  emit('update:mode', mode)
  emit('drawGeometry', mode)
}

function emitLimit(event: Event) {
  const value = Number((event.target as HTMLInputElement).value)
  if (Number.isInteger(value) && value > 0 && value <= MAX_AREA_INVESTIGATION_LIMIT)
    emit('update:limit', value)
}

function modifyGeometry() {
  if (geometry.value !== null)
    emit('modifyGeometry', geometry.value)
}

async function search() {
  const fromUtc = utcControlValue(fromUtcInput.value)
  const toUtc = utcControlValue(toUtcInput.value)
  if (geometry.value === null || fromUtc === null || toUtc === null) {
    localError.value = labels.value.required
    return
  }

  try {
    props.investigation.setTimeRange(fromUtc, toUtc)
  }
  catch {
    localError.value = labels.value.required
    return
  }
  localError.value = null
  await props.investigation.search()
}

function cancel() {
  props.investigation.cancel()
}

function clear() {
  props.investigation.clear()
  localError.value = null
  emit('clearGeometry')
}

function selectResult(combinedId: string) {
  props.investigation.selectResult(combinedId)
  emit('selectResult', combinedId)
}

function formatUtc(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'medium',
    timeZone: 'UTC',
  }).format(new Date(value))
}

function geometryLabel(value: AreaGeometry): string {
  if (value.kind === 'circle')
    return `X ${value.centerX} · Z ${value.centerZ} · R ${value.radius}`
  return `X ${value.minimumX}…${value.maximumX} · Z ${value.minimumZ}…${value.maximumZ}`
}
</script>

<template>
  <section aria-labelledby="area-investigation-title" class="min-w-0 space-y-3 border-t border-default pt-3">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 id="area-investigation-title" class="text-sm font-semibold">
        {{ labels.title }}
      </h2>
      <div class="inline-flex rounded-md border border-default p-0.5" role="group" :aria-label="labels.title">
        <button
          :aria-pressed="props.mode === 'rectangle'"
          class="rounded px-2 py-1 text-xs focus-visible:outline-2 focus-visible:outline-primary"
          :class="props.mode === 'rectangle' ? 'bg-primary text-inverted' : 'text-muted hover:bg-elevated'"
          data-testid="area-mode-rectangle"
          type="button"
          @click="chooseMode('rectangle')"
        >
          {{ labels.rectangle }}
        </button>
        <button
          :aria-pressed="props.mode === 'circle'"
          class="rounded px-2 py-1 text-xs focus-visible:outline-2 focus-visible:outline-primary"
          :class="props.mode === 'circle' ? 'bg-primary text-inverted' : 'text-muted hover:bg-elevated'"
          data-testid="area-mode-circle"
          type="button"
          @click="chooseMode('circle')"
        >
          {{ labels.circle }}
        </button>
      </div>
    </div>

    <div class="flex min-w-0 items-center gap-2 text-xs">
      <span v-if="geometry" class="min-w-0 flex-1 truncate font-mono" data-testid="area-geometry">
        {{ geometryLabel(geometry) }}
      </span>
      <span v-else class="min-w-0 flex-1 text-muted">{{ labels.noArea }}</span>
      <button
        class="shrink-0 rounded px-2 py-1 text-primary hover:bg-primary/10 disabled:cursor-not-allowed disabled:opacity-50"
        data-testid="area-modify"
        :disabled="geometry === null || isLoading"
        type="button"
        @click="modifyGeometry"
      >
        {{ labels.modify }}
      </button>
    </div>

    <div class="grid grid-cols-1 gap-2 sm:grid-cols-2">
      <label class="min-w-0 space-y-1 text-xs text-muted">
        <span class="block">{{ labels.from }}</span>
        <input
          v-model="fromUtcInput"
          class="h-8 w-full rounded-md border border-default bg-default px-2 text-sm text-default"
          data-testid="area-from-utc"
          step="1"
          type="datetime-local"
        >
      </label>
      <label class="min-w-0 space-y-1 text-xs text-muted">
        <span class="block">{{ labels.to }}</span>
        <input
          v-model="toUtcInput"
          class="h-8 w-full rounded-md border border-default bg-default px-2 text-sm text-default"
          data-testid="area-to-utc"
          step="1"
          type="datetime-local"
        >
      </label>
    </div>

    <div class="flex flex-wrap items-end gap-2">
      <label class="w-28 space-y-1 text-xs text-muted">
        <span class="block">{{ labels.limit }}</span>
        <input
          class="h-8 w-full rounded-md border border-default bg-default px-2 text-sm text-default"
          data-testid="area-limit"
          :max="MAX_AREA_INVESTIGATION_LIMIT"
          min="1"
          :value="props.limit"
          type="number"
          @input="emitLimit"
        >
      </label>
      <div class="ml-auto flex flex-wrap gap-1.5">
        <button
          class="h-8 rounded-md border border-default px-2.5 text-xs hover:bg-elevated disabled:cursor-not-allowed disabled:opacity-50"
          data-testid="area-cancel"
          :disabled="!isLoading"
          type="button"
          @click="cancel"
        >
          {{ labels.cancel }}
        </button>
        <button
          class="h-8 rounded-md border border-default px-2.5 text-xs hover:bg-elevated disabled:cursor-not-allowed disabled:opacity-50"
          data-testid="area-clear"
          :disabled="geometry === null && state === 'idle'"
          type="button"
          @click="clear"
        >
          {{ labels.clear }}
        </button>
        <button
          class="h-8 rounded-md bg-primary px-3 text-xs font-medium text-inverted hover:bg-primary/90 disabled:cursor-not-allowed disabled:opacity-50"
          data-testid="area-search"
          :disabled="!canSearch"
          type="button"
          @click="search"
        >
          {{ labels.search }}
        </button>
      </div>
    </div>

    <p class="text-xs leading-5 text-muted">
      {{ labels.caveat }}
    </p>

    <p v-if="isLoading" aria-live="polite" class="text-sm text-muted">
      {{ labels.loading }}
    </p>
    <p v-else-if="error !== null" role="alert" class="text-sm text-error">
      {{ error || labels.failed }}
    </p>
    <p v-else-if="state === 'empty'" class="text-sm text-muted">
      {{ labels.empty }}
    </p>
    <p v-else-if="state === 'idle'" class="text-sm text-muted">
      {{ labels.idle }}
    </p>

    <template v-if="state === 'ready' || state === 'truncated'">
      <div class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted" data-testid="area-result-summary">
        <span>{{ labels.matches }}: <strong class="text-default">{{ props.investigation.matchingObservationCount.value }}</strong></span>
        <span>{{ labels.candidates }}: <strong class="text-default">{{ props.investigation.candidateObservationCount.value }}</strong></span>
      </div>
      <div v-if="props.investigation.truncated.value" class="space-y-1 text-xs text-warning" role="status">
        <p v-if="props.investigation.truncation.value.candidateObservations">
          {{ labels.candidateTruncated }}
        </p>
        <p v-if="props.investigation.truncation.value.playerResults">
          {{ labels.playerTruncated }}
        </p>
      </div>

      <ul class="max-h-80 divide-y divide-default overflow-y-auto" data-testid="area-results">
        <li v-for="player in players" :key="player.combinedId" class="grid min-w-0 grid-cols-[minmax(0,1fr)_auto] gap-2 py-2 first:pt-0 last:pb-0">
          <button
            :aria-pressed="player.combinedId === selectedCombinedId"
            class="min-w-0 rounded-md px-2 py-1.5 text-left text-sm hover:bg-elevated focus-visible:outline-2 focus-visible:outline-primary"
            :class="player.combinedId === selectedCombinedId ? 'bg-primary/10' : ''"
            :data-result-id="player.combinedId"
            type="button"
            @click="selectResult(player.combinedId)"
          >
            <span class="block truncate font-medium">{{ player.displayName }}</span>
            <span class="block truncate font-mono text-xs text-muted">{{ player.combinedId }}</span>
            <span class="mt-1 block text-xs text-muted">{{ labels.matches }}: {{ player.matchingObservationCount }}</span>
            <span class="block text-xs text-muted">{{ labels.first }}: {{ formatUtc(player.firstMatchingObservation.observedAtUtc) }}</span>
            <span class="block text-xs text-muted">{{ labels.last }}: {{ formatUtc(player.lastMatchingObservation.observedAtUtc) }}</span>
            <span class="block font-mono text-xs">{{ labels.position }}: X {{ player.lastMatchingObservation.position.x }} · Y {{ player.lastMatchingObservation.position.y }} · Z {{ player.lastMatchingObservation.position.z }}</span>
          </button>
          <div class="flex flex-col justify-center gap-1">
            <button
              class="rounded px-2 py-1 text-xs text-primary hover:bg-primary/10"
              :data-testid="`area-profile-${player.combinedId}`"
              type="button"
              @click="emit('openHistoryProfile', player.combinedId)"
            >
              {{ labels.profile }}
            </button>
            <button
              class="rounded px-2 py-1 text-xs text-primary hover:bg-primary/10"
              :data-testid="`area-track-${player.combinedId}`"
              type="button"
              @click="emit('loadHistoryTrack', player.combinedId)"
            >
              {{ labels.track }}
            </button>
          </div>
        </li>
      </ul>
    </template>
  </section>
</template>
