<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'

import type { GameEventFilters, GameEventRecord, GameEventSubject } from '../api/gameEvents'
import type { GameEventsController } from '../model/useGameEvents'
import { computed, reactive, watch } from 'vue'

import { gameEventTypes } from '../api/gameEvents'

const props = defineProps<{
  controller: GameEventsController
}>()

const draft = reactive<GameEventFilters>({ ...props.controller.filters.value })
const eventTypeItems = [
  { label: '全部事件', value: '' },
  ...gameEventTypes.map(value => ({ label: value, value })),
]
const columns: TableColumn<GameEventRecord>[] = [
  { accessorKey: 'occurredAtUtc', header: '发生时间（UTC）' },
  { accessorKey: 'eventType', header: '事件类型' },
  { id: 'actor', header: '主体' },
  { id: 'target', header: '目标' },
  { accessorKey: 'observedAtUtc', header: '采集时间（UTC）' },
]
const syntheticTotal = computed(() =>
  (props.controller.pageNumber.value + (props.controller.nextCursor.value === null ? 0 : 1)) * 50,
)
const tableData = computed(() => [...props.controller.events.value])

watch(props.controller.filters, value => Object.assign(draft, value))

function subjectLabel(subject: GameEventSubject | null): string {
  if (subject === null)
    return '—'
  return subject.displayName
    ?? subject.crossplatformId
    ?? subject.platformId
    ?? (subject.entityId === null ? '—' : `#${subject.entityId}`)
}

function applyFilters() {
  void props.controller.applyFilters({ ...draft })
}

function clearFilters() {
  Object.assign(draft, { fromUtc: '', toUtc: '', eventType: '', crossplatformId: '' })
  applyFilters()
}
</script>

<template>
  <section class="space-y-4" data-testid="game-events-panel">
    <UForm :state="draft" class="grid gap-3 lg:grid-cols-4" @submit="applyFilters">
      <UFormField label="起始时间（UTC）" name="fromUtc">
        <UInput v-model="draft.fromUtc" class="w-full" placeholder="2026-07-26T00:00:00Z" />
      </UFormField>
      <UFormField label="截止时间（UTC）" name="toUtc">
        <UInput v-model="draft.toUtc" class="w-full" placeholder="2026-07-26T23:59:59Z" />
      </UFormField>
      <UFormField label="事件类型" name="eventType">
        <USelect v-model="draft.eventType" :items="eventTypeItems" class="w-full" />
      </UFormField>
      <UFormField label="跨平台身份" name="crossplatformId">
        <UInput v-model="draft.crossplatformId" class="w-full" />
      </UFormField>
      <div class="flex gap-2 lg:col-span-4">
        <UButton label="筛选" type="submit" />
        <UButton
          color="neutral"
          label="清除"
          variant="outline"
          @click="clearFilters"
        />
      </div>
    </UForm>

    <UAlert
      v-if="controller.state.value === 'stale'"
      color="warning"
      icon="i-lucide-triangle-alert"
      title="刷新失败，当前显示上一次成功结果"
    />
    <UAlert
      v-else-if="controller.state.value === 'forbidden'"
      color="warning"
      icon="i-lucide-shield-alert"
      title="当前账号无权查看游戏事件"
    />
    <UAlert
      v-else-if="controller.state.value === 'failed'"
      color="error"
      icon="i-lucide-circle-x"
      title="游戏事件加载失败"
    >
      <template #actions>
        <UButton
          color="neutral"
          label="重试"
          variant="outline"
          @click="controller.retry"
        />
      </template>
    </UAlert>

    <div v-if="controller.state.value === 'loading'" class="space-y-2" aria-label="正在加载游戏事件">
      <USkeleton v-for="row in 5" :key="row" class="h-10 w-full" />
    </div>

    <div v-if="controller.gaps.value.length > 0" class="space-y-2" data-testid="game-event-gaps">
      <UAlert
        v-for="gap in controller.gaps.value"
        :key="gap.gapId"
        color="warning"
        icon="i-lucide-database-zap"
        :title="`游戏事件证据缺口：${gap.affectedCount} 条 · ${gap.reason}`"
        :description="`${gap.startedAtUtc} — ${gap.endedAtUtc ?? '尚未结束'}`"
      />
    </div>

    <UAlert
      v-if="controller.state.value === 'ready' && controller.events.value.length === 0"
      color="neutral"
      title="没有符合条件的游戏事件"
    />

    <div v-if="controller.events.value.length > 0" class="overflow-x-auto rounded-lg border border-default">
      <UTable :columns="columns" :data="tableData">
        <template #occurredAtUtc-cell="{ row }">
          <time class="whitespace-nowrap">{{ row.original.occurredAtUtc }}</time>
        </template>
        <template #eventType-cell="{ row }">
          <UBadge color="neutral" :label="row.original.eventType" variant="subtle" />
        </template>
        <template #actor-cell="{ row }">
          <span>{{ subjectLabel(row.original.actor) }}</span>
          <code v-if="row.original.actor?.crossplatformId" class="block text-xs text-muted">{{ row.original.actor.crossplatformId }}</code>
        </template>
        <template #target-cell="{ row }">
          <span>{{ subjectLabel(row.original.target) }}</span>
          <code v-if="row.original.target?.crossplatformId" class="block text-xs text-muted">{{ row.original.target.crossplatformId }}</code>
        </template>
        <template #observedAtUtc-cell="{ row }">
          <time class="whitespace-nowrap">{{ row.original.observedAtUtc }}</time>
        </template>
      </UTable>
    </div>

    <div v-if="controller.events.value.length > 0" class="flex justify-end">
      <UPagination
        :items-per-page="50"
        :page="controller.pageNumber.value"
        :total="syntheticTotal"
        @update:page="controller.goToPage"
      />
    </div>
  </section>
</template>
