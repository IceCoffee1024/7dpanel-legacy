<script setup lang="ts">
import type { JobRecord } from '../model/useBackups'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ job: JobRecord }>()
const { d, t } = useI18n()

const progress = computed(() => {
  const current = props.job.progress?.current
  const total = props.job.progress?.total
  if (current === null || current === undefined || total === null || total === undefined || total <= 0)
    return null
  return Math.min(100, Math.max(0, Math.round(current / total * 100)))
})

const color = computed(() => props.job.status === 'Succeeded'
  ? 'success'
  : ['Failed', 'Cancelled', 'Interrupted', 'ResultUnknown'].includes(props.job.status) ? 'error' : 'primary')
</script>

<template>
  <UCard>
    <div class="space-y-3">
      <div class="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 class="font-semibold">
            {{ t('backups.job.title') }}
          </h2>
          <p class="text-sm text-muted">
            {{ job.kind }} · {{ job.id }}
          </p>
        </div>
        <UBadge :color="color" :label="t(`backups.job.status.${job.status}`)" variant="subtle" />
      </div>
      <UProgress v-if="progress !== null" :model-value="progress" />
      <p class="text-xs text-muted">
        {{ t('backups.job.createdAt', { time: d(new Date(job.createdAtUtc), 'medium') }) }}
      </p>
      <UAlert v-if="job.errorCode" color="error" :title="job.errorCode" />
    </div>
  </UCard>
</template>
