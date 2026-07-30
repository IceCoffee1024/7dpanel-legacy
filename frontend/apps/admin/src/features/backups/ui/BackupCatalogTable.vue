<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type { BackupRecord } from '../model/useBackups'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  backups: readonly BackupRecord[]
  disabled: boolean
}>()
const emit = defineEmits<{
  download: [backup: BackupRecord]
  remove: [backup: BackupRecord]
  restore: [backup: BackupRecord]
}>()
const { t } = useI18n()

const columns = computed<TableColumn<BackupRecord>[]>(() => [
  { accessorKey: 'kind', header: t('backups.table.kind') },
  { accessorKey: 'createdAtUtc', header: t('backups.table.createdAt') },
  { accessorKey: 'sizeBytes', header: t('backups.table.size') },
  { accessorKey: 'validationStatus', header: t('backups.table.validation') },
  { id: 'actions', header: t('backups.table.actions') },
])

function formatBytes(value: number): string {
  if (value < 1024)
    return `${value} B`
  if (value < 1024 * 1024)
    return `${(value / 1024).toFixed(1)} KiB`
  if (value < 1024 * 1024 * 1024)
    return `${(value / 1024 / 1024).toFixed(1)} MiB`
  return `${(value / 1024 / 1024 / 1024).toFixed(1)} GiB`
}
</script>

<template>
  <UCard>
    <template #header>
      <h2 class="font-semibold">
        {{ t('backups.table.title') }}
      </h2>
    </template>

    <UTable :columns="columns" :data="[...props.backups]">
      <template #kind-cell="{ row }">
        <div>
          <p class="font-medium">
            {{ t(`backups.kind.${row.original.kind}`) }}
          </p>
          <p v-if="row.original.worldId" class="text-xs text-muted">
            {{ row.original.worldId }}
          </p>
        </div>
      </template>
      <template #createdAtUtc-cell="{ row }">
        {{ new Date(row.original.createdAtUtc).toLocaleString() }}
      </template>
      <template #sizeBytes-cell="{ row }">
        {{ formatBytes(row.original.sizeBytes) }}
      </template>
      <template #validationStatus-cell="{ row }">
        <UBadge :color="row.original.validationStatus === 'Verified' ? 'success' : 'warning'" :label="row.original.validationStatus" variant="subtle" />
      </template>
      <template #actions-cell="{ row }">
        <div class="flex flex-wrap justify-end gap-2">
          <UButton
            :disabled="disabled"
            icon="i-lucide-download"
            :label="t('backups.action.download')"
            size="sm"
            variant="outline"
            @click="emit('download', row.original)"
          />
          <UButton
            :disabled="disabled"
            icon="i-lucide-rotate-ccw"
            :label="t('backups.action.restore')"
            size="sm"
            variant="outline"
            @click="emit('restore', row.original)"
          />
          <UButton
            color="error"
            :disabled="disabled"
            icon="i-lucide-trash-2"
            :label="t('backups.action.delete')"
            size="sm"
            variant="soft"
            @click="emit('remove', row.original)"
          />
        </div>
      </template>
      <template #empty>
        <div class="py-8 text-center text-sm text-muted">
          {{ t('backups.state.empty') }}
        </div>
      </template>
    </UTable>
  </UCard>
</template>
