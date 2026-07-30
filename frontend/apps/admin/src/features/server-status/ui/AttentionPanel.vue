<script setup lang="ts">
import type { OverviewAttention } from '../model/overview'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ attention: readonly OverviewAttention[] }>()
const { te, t } = useI18n()
const messages = computed(() => props.attention.map(({ code }) => {
  const key = `overview.attention.codes.${code}`
  return te(key) ? t(key) : t('overview.attention.unknown')
}))
</script>

<template>
  <UCard class="rounded-md">
    <template #header>
      <h2 class="font-semibold text-highlighted">
        {{ t('overview.attention.title') }}
      </h2>
    </template>
    <p v-if="messages.length === 0" class="text-sm text-muted">
      {{ t('overview.attention.none') }}
    </p>
    <div v-else class="space-y-2">
      <UAlert
        v-for="(message, index) in messages"
        :key="index"
        color="warning"
        variant="subtle"
        :description="message"
      />
    </div>
  </UCard>
</template>
