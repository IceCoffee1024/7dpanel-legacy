<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type { WorldOperationReceipt, WorldOperationRecord } from '../api/worldTools'
import type { WorldOperationsErrorCode, WorldOperationsState } from '../model/useWorldOperations'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  operation: WorldOperationRecord | null
  receipt: WorldOperationReceipt | null
  state: WorldOperationsState
  errorCode: WorldOperationsErrorCode | null
}>()
const emit = defineEmits<{
  clear: []
  refresh: [operationId: string]
}>()
const { t } = useI18n()

const columns = computed<TableColumn<WorldOperationRecord>[]>(() => [
  { accessorKey: 'kind', header: t('worldTools.history.table.operation') },
  { accessorKey: 'status', header: t('worldTools.history.table.status') },
  { accessorKey: 'worldVersion', header: t('worldTools.history.table.worldVersion') },
  { accessorKey: 'createdAtUtc', header: t('worldTools.history.table.created') },
])
const rows = computed(() => props.operation === null ? [] : [props.operation])
const trackedOperationId = computed(() => props.operation?.operationId ?? props.receipt?.operationId ?? null)

function statusColor(status: WorldOperationRecord['status']): 'neutral' | 'info' | 'success' | 'warning' | 'error' {
  if (status === 'Succeeded')
    return 'success'
  if (status === 'Queued' || status === 'Running')
    return 'info'
  if (status === 'Interrupted' || status === 'ResultUnknown')
    return 'warning'
  if (status === 'Failed' || status === 'RollbackFailed')
    return 'error'
  return 'neutral'
}

function progressLabel(operation: WorldOperationRecord): string {
  if (operation.progress === null)
    return '—'
  return `${operation.progress.current ?? '—'} / ${operation.progress.total ?? '—'}`
}
</script>

<template>
  <section class="space-y-4" aria-labelledby="world-operation-history-title">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div>
        <h2 id="world-operation-history-title" class="font-semibold text-highlighted">
          {{ t('worldTools.history.title') }}
        </h2>
        <p class="text-sm text-muted">
          {{ t('worldTools.history.description') }}
        </p>
      </div>
      <div v-if="trackedOperationId" class="flex flex-wrap gap-2">
        <UButton
          color="neutral"
          icon="i-lucide-refresh-cw"
          :label="t('worldTools.history.refreshStatus')"
          variant="outline"
          :loading="props.state === 'polling'"
          @click="emit('refresh', trackedOperationId)"
        />
        <UButton
          color="neutral"
          :label="t('worldTools.common.clear')"
          variant="ghost"
          @click="emit('clear')"
        />
      </div>
    </div>

    <UAlert
      v-if="props.receipt && !props.operation"
      color="info"
      icon="i-lucide-clock-3"
      :title="t('worldTools.history.acceptedTitle')"
      :description="t('worldTools.history.acceptedDescription', { operationId: props.receipt.operationId })"
      variant="subtle"
    />
    <UAlert
      v-if="props.operation?.status === 'ResultUnknown'"
      color="error"
      icon="i-lucide-circle-help"
      :title="t('worldTools.history.resultUnknownTitle')"
      :description="t('worldTools.history.resultUnknownDescription')"
      variant="solid"
    />
    <UAlert
      v-else-if="props.operation?.status === 'RollbackFailed'"
      color="error"
      icon="i-lucide-shield-alert"
      :title="t('worldTools.history.rollbackFailedTitle')"
      :description="t('worldTools.history.rollbackFailedDescription')"
      variant="solid"
    />
    <UAlert
      v-if="props.errorCode"
      color="error"
      icon="i-lucide-circle-alert"
      :title="t('worldTools.history.statusUnavailable')"
      :description="t('worldTools.common.errorCode', { code: props.errorCode })"
      variant="subtle"
    />

    <div class="hidden md:block">
      <UTable :columns="columns" :data="rows">
        <template #status-cell="{ row }">
          <UBadge :color="statusColor(row.original.status)" variant="subtle">
            {{ row.original.status }}
          </UBadge>
        </template>
        <template #createdAtUtc-cell="{ row }">
          {{ new Date(row.original.createdAtUtc).toLocaleString() }}
        </template>
        <template #empty>
          <p class="p-4 text-sm text-muted">
            {{ t('worldTools.history.empty') }}
          </p>
        </template>
      </UTable>
    </div>

    <article v-if="props.operation" class="space-y-3 rounded-lg border border-default p-4 md:hidden">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <strong class="text-highlighted">{{ props.operation.kind }}</strong>
        <UBadge :color="statusColor(props.operation.status)" variant="subtle">
          {{ props.operation.status }}
        </UBadge>
      </div>
      <dl class="grid gap-2 text-sm">
        <div>
          <dt class="text-xs text-muted">
            {{ t('worldTools.history.operationId') }}
          </dt><dd class="break-all">
            {{ props.operation.operationId }}
          </dd>
        </div>
        <div>
          <dt class="text-xs text-muted">
            {{ t('worldTools.history.table.worldVersion') }}
          </dt><dd class="break-all">
            {{ props.operation.worldVersion }}
          </dd>
        </div>
        <div>
          <dt class="text-xs text-muted">
            {{ t('worldTools.history.progress') }}
          </dt><dd>{{ progressLabel(props.operation) }}</dd>
        </div>
        <div>
          <dt class="text-xs text-muted">
            {{ t('worldTools.history.error') }}
          </dt><dd>{{ props.operation.errorCode ?? '—' }}</dd>
        </div>
      </dl>
    </article>
    <p v-else-if="!props.receipt" class="text-sm text-muted md:hidden">
      {{ t('worldTools.history.empty') }}
    </p>
  </section>
</template>
