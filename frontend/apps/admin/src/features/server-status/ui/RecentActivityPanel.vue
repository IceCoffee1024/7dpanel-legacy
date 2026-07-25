<script setup lang="ts">
import type { RecentActivityOverview } from '../model/overview'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ activity: RecentActivityOverview }>()
const { d, locale, te, t } = useI18n()
const items = computed(() => props.activity.items.slice(0, 8).map(item => {
  const key = `overview.activity.messages.${item.messageKey}`
  const message = te(key) ? t(key, { player: item.messageArguments.player ?? t('overview.activity.someone') }) : t('overview.activity.unknown')
  const date = new Date(item.occurredAtUtc)
  const difference = date.getTime() - Date.now()
  const relative = new Intl.RelativeTimeFormat(locale.value, { numeric: 'auto' }).format(Math.round(difference / 60_000), 'minute')
  return { ...item, absolute: d(date, 'medium'), message, relative }
}))
</script>

<template>
  <UCard class="rounded-md">
    <template #header><h2 class="font-semibold text-highlighted">{{ t('overview.activity.title') }}</h2></template>
    <p v-if="items.length === 0" class="text-sm text-muted">{{ t('overview.activity.empty') }}</p>
    <ul v-else class="divide-y divide-muted">
      <li v-for="item in items" :key="`${item.occurredAtUtc}-${item.messageKey}`" data-testid="activity-item" class="flex items-start justify-between gap-4 py-3 first:pt-0 last:pb-0">
        <span class="text-sm text-highlighted">{{ item.message }}</span><time class="shrink-0 text-xs text-dimmed" :datetime="item.occurredAtUtc" :title="item.absolute">{{ item.relative }}</time>
      </li>
    </ul>
  </UCard>
</template>
