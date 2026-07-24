<route lang="json">
{
  "meta": {
    "requiresAuth": true
  }
}
</route>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import { useServerHealth } from '../composables/useServerHealth'

const { state, data, error, lastSuccessfulAt, refresh } = useServerHealth()
const { d, t } = useI18n()

const statusIcon = computed(() => {
  if (state.value === 'loading') {
    return 'i-lucide-loader-circle'
  }
  if (state.value === 'fresh') {
    return 'i-lucide-server'
  }
  if (state.value === 'stale') {
    return 'i-lucide-clock-alert'
  }
  return 'i-lucide-server-off'
})

const statusTitle = computed(() => {
  if (state.value === 'loading') {
    return t('overview.status.loadingTitle')
  }
  if (state.value === 'fresh') {
    return t('overview.status.freshTitle')
  }
  if (state.value === 'stale') {
    return t('overview.status.staleTitle')
  }
  return t('overview.status.offlineTitle')
})

const statusDescription = computed(() => {
  if (state.value === 'loading') {
    return t('overview.status.loadingDescription')
  }
  if (state.value === 'fresh') {
    return t('overview.status.freshDescription')
  }
  if (state.value === 'stale') {
    return t('overview.status.staleDescription')
  }
  return error.value?.code === 'http'
    ? t('overview.status.httpDescription')
    : t('overview.status.offlineDescription')
})

const lastSampleLabel = computed(() => {
  if (lastSuccessfulAt.value === null) {
    return ''
  }
  return d(new Date(lastSuccessfulAt.value), 'medium')
})
</script>

<template>
  <UDashboardPanel id="overview">
    <template #header>
      <UDashboardNavbar :title="t('overview.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <section class="mx-auto flex w-full max-w-5xl flex-1 items-center justify-center py-16">
        <div class="flex max-w-md flex-col items-center text-center">
          <span
            class="mb-5 flex size-12 items-center justify-center rounded-md bg-elevated text-muted"
            :class="{ 'animate-spin': state === 'loading' }"
          >
            <UIcon :name="statusIcon" class="size-6" />
          </span>
          <h2 class="text-base font-semibold text-highlighted">
            {{ statusTitle }}
          </h2>
          <p class="mt-2 text-sm text-muted">
            {{ statusDescription }}
          </p>

          <div v-if="data" class="mt-5 flex flex-wrap justify-center gap-2 text-sm text-muted">
            <UBadge color="neutral" variant="subtle">
              {{ data.product }}
            </UBadge>
            <UBadge color="neutral" variant="subtle">
              v{{ data.version }}
            </UBadge>
          </div>

          <p v-if="lastSampleLabel" class="mt-3 text-xs text-dimmed">
            {{ t('overview.status.lastSample', { time: lastSampleLabel }) }}
          </p>

          <UButton
            v-if="state === 'stale' || state === 'offline'"
            class="mt-6"
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('overview.status.retry')"
            variant="outline"
            @click="refresh"
          />
        </div>
      </section>
    </template>
  </UDashboardPanel>
</template>
