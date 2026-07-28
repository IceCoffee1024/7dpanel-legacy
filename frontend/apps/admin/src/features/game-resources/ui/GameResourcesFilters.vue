<script setup lang="ts">
import type { GameResourceFilters, GameResourceKindFilter } from '../model/gameResourceFilters'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

defineProps<{
  filters: GameResourceFilters
  count: number
  isOwner: boolean
  isRefreshing: boolean
}>()

const emit = defineEmits<{
  search: [value: string]
  kind: [value: GameResourceKindFilter]
  includeHidden: [value: boolean]
  refresh: []
}>()

const { t } = useI18n()
const kindItems = computed(() => [
  { label: t('gameResources.filters.all'), value: 'all' },
  { label: t('gameResources.kind.item'), value: 'item' },
  { label: t('gameResources.kind.block'), value: 'block' },
])

function updateKind(value: unknown) {
  if (value === 'all' || value === 'item' || value === 'block')
    emit('kind', value)
}

function updateHidden(value: boolean | 'indeterminate') {
  emit('includeHidden', value === true)
}
</script>

<template>
  <UDashboardToolbar>
    <template #left>
      <div class="flex w-full flex-wrap items-center gap-2">
        <UInput
          :model-value="filters.search"
          :aria-label="t('gameResources.filters.searchPlaceholder')"
          id="game-resource-search"
          class="min-w-56 flex-1 sm:max-w-sm"
          data-testid="game-resource-search"
          icon="i-lucide-search"
          name="game-resource-search"
          :placeholder="t('gameResources.filters.searchPlaceholder')"
          @update:model-value="emit('search', String($event))"
        />
        <USelect
          :items="kindItems"
          :model-value="filters.kind"
          value-key="value"
          @update:model-value="updateKind"
        />
        <UCheckbox
          v-if="isOwner"
          :label="t('gameResources.filters.includeHidden')"
          :model-value="filters.includeHidden"
          @update:model-value="updateHidden"
        />
      </div>
    </template>

    <template #right>
      <div class="flex items-center gap-2">
        <span class="whitespace-nowrap text-sm text-muted">
          {{ t('gameResources.results', { count }) }}
        </span>
        <UButton
          :aria-label="t('gameResources.refresh')"
          color="neutral"
          data-testid="game-resource-refresh"
          icon="i-lucide-refresh-cw"
          :loading="isRefreshing"
          variant="ghost"
          @click="emit('refresh')"
        />
      </div>
    </template>
  </UDashboardToolbar>
</template>
