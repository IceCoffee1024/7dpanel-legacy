<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const emit = defineEmits<{ restart: [], shutdown: [] }>()
const { t } = useI18n()
</script>

<template>
  <UCard class="rounded-md border-warning/40">
    <template #header>
      <h2 class="font-semibold text-highlighted">
        {{ t('overview.quickActions.title') }}
      </h2>
    </template>
    <UAlert
      class="mb-4"
      color="warning"
      variant="subtle"
      :description="t('overview.quickActions.warning')"
    />
    <div class="flex flex-wrap gap-3">
      <UButton
        data-testid="restart-action"
        color="warning"
        variant="outline"
        icon="i-lucide-rotate-ccw"
        :label="t('overview.quickActions.restart')"
        @click="emit('restart')"
      />
      <UButton
        data-testid="shutdown-action"
        color="error"
        variant="outline"
        icon="i-lucide-power"
        :label="t('overview.quickActions.shutdown')"
        @click="emit('shutdown')"
      />
    </div>
  </UCard>
</template>
