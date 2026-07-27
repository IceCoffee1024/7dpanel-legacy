<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type { ScheduleRecord } from '../model/useSchedules'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ disabled: boolean, schedules: readonly ScheduleRecord[] }>()
const emit = defineEmits<{
  edit: [schedule: ScheduleRecord]
  remove: [schedule: ScheduleRecord]
  setEnabled: [schedule: ScheduleRecord, enabled: boolean]
}>()
const { t } = useI18n()

const columns = computed<TableColumn<ScheduleRecord>[]>(() => [
  { accessorKey: 'name', header: t('schedules.table.name') },
  { accessorKey: 'kind', header: t('schedules.table.kind') },
  { accessorKey: 'cronExpression', header: t('schedules.table.schedule') },
  { accessorKey: 'nextOccurrenceUtc', header: t('schedules.table.next') },
  { accessorKey: 'enabled', header: t('schedules.table.status') },
  { id: 'actions', header: t('schedules.table.actions') },
])
</script>

<template>
  <UTable :columns="columns" :data="[...props.schedules]">
    <template #kind-cell="{ row }">
      {{ t(`schedules.kind.${row.original.kind}`) }}
    </template>
    <template #cronExpression-cell="{ row }">
      <div>
        <p class="font-mono text-xs">{{ row.original.cronExpression }}</p>
        <p class="text-xs text-muted">{{ row.original.timeZoneId }} · {{ t(`schedules.policy.${row.original.concurrencyPolicy}`) }}</p>
      </div>
    </template>
    <template #nextOccurrenceUtc-cell="{ row }">
      {{ row.original.nextOccurrenceUtc ? new Date(row.original.nextOccurrenceUtc).toLocaleString() : t('schedules.table.none') }}
    </template>
    <template #enabled-cell="{ row }">
      <UBadge :color="row.original.enabled ? 'success' : 'neutral'" :label="row.original.enabled ? t('schedules.table.enabled') : t('schedules.table.disabled')" variant="subtle" />
    </template>
    <template #actions-cell="{ row }">
      <div class="flex flex-wrap justify-end gap-2">
        <UButton :disabled="disabled" :label="t('common.edit')" size="sm" variant="outline" @click="emit('edit', row.original)" />
        <UButton :disabled="disabled" :label="row.original.enabled ? t('schedules.action.disable') : t('schedules.action.enable')" size="sm" variant="outline" @click="emit('setEnabled', row.original, !row.original.enabled)" />
        <UButton color="error" :disabled="disabled" :label="t('schedules.action.delete')" size="sm" variant="soft" @click="emit('remove', row.original)" />
      </div>
    </template>
    <template #empty>
      <div class="py-8 text-center text-sm text-muted">
        {{ t('schedules.state.empty') }}
      </div>
    </template>
  </UTable>
</template>
