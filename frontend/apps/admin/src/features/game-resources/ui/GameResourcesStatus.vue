<script setup lang="ts">
import type { GameResourceViewState } from '../api/gameResources'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  state: GameResourceViewState
}>()

const emit = defineEmits<{
  retry: []
  clear: []
}>()

const { t } = useI18n()
const alert = computed(() => {
  switch (props.state) {
    case 'building':
      return { color: 'warning' as const, title: t('gameResources.state.buildingTitle'), description: t('gameResources.state.buildingDescription'), action: 'retry' as const }
    case 'unavailable':
      return { color: 'error' as const, title: t('gameResources.state.unavailableTitle'), description: t('gameResources.state.unavailableDescription'), action: 'retry' as const }
    case 'forbidden':
      return { color: 'error' as const, title: t('gameResources.state.forbiddenTitle'), description: t('gameResources.state.forbiddenDescription'), action: null }
    case 'empty':
      return { color: 'neutral' as const, title: t('gameResources.state.emptyTitle'), description: t('gameResources.state.emptyDescription'), action: 'clear' as const }
    case 'stale':
      return { color: 'warning' as const, title: t('gameResources.state.staleTitle'), description: t('gameResources.state.staleDescription'), action: 'retry' as const }
    case 'partial':
      return { color: 'warning' as const, title: t('gameResources.state.partialTitle'), description: t('gameResources.state.partialDescription'), action: null }
    default:
      return null
  }
})
</script>

<template>
  <div v-if="state === 'loading'" class="space-y-3" :aria-label="t('gameResources.state.loading')">
    <span class="sr-only">{{ t('gameResources.state.loading') }}</span>
    <USkeleton class="h-16 w-full" />
    <USkeleton class="h-16 w-full" />
    <USkeleton class="h-16 w-full" />
  </div>

  <UAlert
    v-else-if="alert"
    :color="alert.color"
    :description="alert.description"
    :title="alert.title"
    variant="subtle"
  >
    <template v-if="alert.action" #actions>
      <UButton
        color="neutral"
        :label="alert.action === 'clear' ? t('gameResources.clearFilters') : t('gameResources.retry')"
        size="sm"
        variant="soft"
        @click="alert.action === 'clear' ? emit('clear') : emit('retry')"
      />
    </template>
  </UAlert>
</template>
