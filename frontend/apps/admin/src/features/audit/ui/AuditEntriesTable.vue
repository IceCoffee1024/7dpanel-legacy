<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'

import type { AuditEntry, AuditFilters } from '../model/audit'
import type { AuditWorkspaceController } from '../model/useAuditWorkspace'
import { computed, reactive, watch } from 'vue'

import { auditSourceKinds } from '../model/audit'

const props = defineProps<{
  controller: AuditWorkspaceController
}>()

const draft = reactive<AuditFilters>({ ...props.controller.filters.value })
const sourceKindItems = [
  { label: '全部来源', value: '' },
  ...auditSourceKinds.map(value => ({ label: value, value })),
]
const columns: TableColumn<AuditEntry>[] = [
  { accessorKey: 'occurredAtUtc', header: '发生时间（UTC）' },
  { accessorKey: 'sourceKind', header: '来源' },
  { accessorKey: 'actorSubject', header: '操作者' },
  { accessorKey: 'targetRef', header: '目标' },
  { accessorKey: 'action', header: '动作' },
  { accessorKey: 'status', header: '状态' },
  { id: 'details', header: '详情' },
]
const syntheticTotal = computed(() =>
  (props.controller.pageNumber.value + (props.controller.nextCursor.value === null ? 0 : 1)) * 50,
)
const tableData = computed(() => [...props.controller.entries.value])

watch(props.controller.filters, value => Object.assign(draft, value))

function applyFilters() {
  void props.controller.applyFilters({ ...draft })
}

function clearFilters() {
  Object.assign(draft, {
    fromUtc: '',
    toUtc: '',
    actor: '',
    target: '',
    action: '',
    sourceKind: '',
    status: '',
  })
  applyFilters()
}
</script>

<template>
  <section class="space-y-4" data-testid="audit-entries-panel">
    <UForm :state="draft" class="grid gap-3 lg:grid-cols-4" @submit="applyFilters">
      <UFormField label="起始时间（UTC）" name="fromUtc">
        <UInput v-model="draft.fromUtc" class="w-full" placeholder="2026-07-26T00:00:00Z" />
      </UFormField>
      <UFormField label="截止时间（UTC）" name="toUtc">
        <UInput v-model="draft.toUtc" class="w-full" placeholder="2026-07-26T23:59:59Z" />
      </UFormField>
      <UFormField label="操作者" name="actor">
        <UInput v-model="draft.actor" class="w-full" />
      </UFormField>
      <UFormField label="目标" name="target">
        <UInput v-model="draft.target" class="w-full" />
      </UFormField>
      <UFormField label="动作" name="action">
        <UInput v-model="draft.action" class="w-full" />
      </UFormField>
      <UFormField label="来源" name="sourceKind">
        <USelect v-model="draft.sourceKind" :items="sourceKindItems" class="w-full" />
      </UFormField>
      <UFormField label="状态" name="status">
        <UInput v-model="draft.status" class="w-full" />
      </UFormField>
      <div class="flex items-end gap-2">
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
      title="当前账号无权查看审计记录"
    />
    <UAlert
      v-else-if="controller.state.value === 'failed'"
      color="error"
      icon="i-lucide-circle-x"
      title="审计记录加载失败"
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

    <div v-if="controller.state.value === 'loading'" class="space-y-2" aria-label="正在加载审计记录">
      <USkeleton v-for="row in 5" :key="row" class="h-10 w-full" />
    </div>

    <UAlert
      v-for="gap in controller.sourceGaps.value"
      :key="`${gap.sourceKind}-${gap.startedAtUtc}-${gap.reason}`"
      color="warning"
      icon="i-lucide-database-zap"
      :title="`${gap.sourceKind} 证据缺口：${gap.affectedCount} 条`"
      :description="`${gap.startedAtUtc} — ${gap.endedAtUtc ?? '尚未结束'} · ${gap.reason}`"
      data-testid="audit-gap"
    />

    <UAlert
      v-if="controller.state.value === 'ready' && controller.entries.value.length === 0"
      color="neutral"
      title="没有符合条件的审计记录"
    />

    <div v-if="controller.entries.value.length > 0" class="overflow-x-auto rounded-lg border border-default">
      <UTable :columns="columns" :data="tableData">
        <template #occurredAtUtc-cell="{ row }">
          <time class="whitespace-nowrap">{{ row.original.occurredAtUtc }}</time>
        </template>
        <template #sourceKind-cell="{ row }">
          <UBadge color="neutral" :label="row.original.sourceKind" variant="subtle" />
        </template>
        <template #actorSubject-cell="{ row }">
          <code>{{ row.original.actorSubject ?? '—' }}</code>
        </template>
        <template #targetRef-cell="{ row }">
          <code>{{ row.original.targetRef ?? '—' }}</code>
        </template>
        <template #status-cell="{ row }">
          <UBadge :color="row.original.status === 'Failed' ? 'error' : 'neutral'" :label="row.original.status" variant="subtle" />
        </template>
        <template #details-cell="{ row }">
          <UButton
            v-if="row.original.hasDetails"
            color="neutral"
            label="查看详情"
            size="xs"
            :to="{ path: '/audit', query: { sourceKind: row.original.sourceKind, sourceId: row.original.sourceId } }"
            variant="outline"
          />
          <span v-else>—</span>
        </template>
      </UTable>
    </div>

    <div v-if="controller.entries.value.length > 0" class="flex justify-end">
      <UPagination
        :items-per-page="50"
        :page="controller.pageNumber.value"
        :total="syntheticTotal"
        @update:page="controller.goToPage"
      />
    </div>
  </section>
</template>
