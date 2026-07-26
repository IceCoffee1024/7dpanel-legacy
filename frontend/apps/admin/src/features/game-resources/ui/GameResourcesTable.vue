<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type { GameResourceItem } from '../api/gameResources'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import GameResourceIcon from './GameResourceIcon.vue'

const props = defineProps<{
  items: readonly GameResourceItem[]
}>()

const emit = defineEmits<{
  copy: [internalName: string]
}>()

const { t } = useI18n()
const data = computed(() => [...props.items])
const columns = computed<TableColumn<GameResourceItem>[]>(() => [
  { id: 'icon', header: t('gameResources.table.icon') },
  { accessorKey: 'localizedName', header: t('gameResources.table.localizedName') },
  { accessorKey: 'internalName', header: t('gameResources.table.internalName') },
  { accessorKey: 'kind', header: t('gameResources.table.kind') },
  { accessorKey: 'maxStack', header: t('gameResources.table.maxStack') },
  { accessorKey: 'hasQuality', header: t('gameResources.table.hasQuality') },
  { accessorKey: 'visibility', header: t('gameResources.table.visibility') },
  { id: 'tint', header: t('gameResources.table.tint') },
  { id: 'copy', header: t('gameResources.table.actions') },
])

function qualityLabel(value: boolean | null): string {
  if (value === null)
    return t('gameResources.values.unavailable')
  return value ? t('gameResources.values.yes') : t('gameResources.values.no')
}
</script>

<template>
  <div class="hidden overflow-hidden rounded-md border border-default md:block">
    <UTable :columns="columns" :data="data">
      <template #icon-cell="{ row }">
        <GameResourceIcon
          :alt="row.original.localizedName ?? row.original.internalName"
          :icon-status="row.original.iconStatus"
          :resource-id="row.original.resourceId"
        />
      </template>

      <template #localizedName-cell="{ row }">
        <span class="font-medium text-highlighted">
          {{ row.original.localizedName ?? row.original.internalName }}
        </span>
      </template>

      <template #internalName-cell="{ row }">
        <code class="break-all text-xs text-default">{{ row.original.internalName }}</code>
      </template>

      <template #kind-cell="{ row }">
        <UBadge color="neutral" variant="subtle">
          {{ t(`gameResources.kind.${row.original.kind}`) }}
        </UBadge>
      </template>

      <template #maxStack-cell="{ row }">
        {{ row.original.maxStack ?? t('gameResources.values.unavailable') }}
      </template>

      <template #hasQuality-cell="{ row }">
        {{ qualityLabel(row.original.hasQuality) }}
      </template>

      <template #visibility-cell="{ row }">
        <UBadge :color="row.original.visibility === 'hidden' ? 'warning' : 'neutral'" variant="subtle">
          {{ t(`gameResources.visibility.${row.original.visibility}`) }}
        </UBadge>
      </template>

      <template #tint-cell="{ row }">
        <span v-if="row.original.iconTintHex" class="inline-flex items-center gap-2 whitespace-nowrap">
          <span
            :data-tint="row.original.iconTintHex"
            class="size-4 rounded-sm border border-default"
            :style="{ backgroundColor: `#${row.original.iconTintHex}` }"
          />
          <code class="text-xs">#{{ row.original.iconTintHex }}</code>
        </span>
        <span v-else class="text-muted">{{ t('gameResources.values.none') }}</span>
      </template>

      <template #copy-cell="{ row }">
        <UButton
          color="neutral"
          :data-testid="`copy-${row.original.internalName}`"
          icon="i-lucide-copy"
          :label="t('gameResources.copy.action')"
          size="xs"
          variant="ghost"
          @click="emit('copy', row.original.internalName)"
        />
      </template>
    </UTable>
  </div>
</template>
