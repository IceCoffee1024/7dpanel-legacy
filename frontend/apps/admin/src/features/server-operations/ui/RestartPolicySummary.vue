<script setup lang="ts">
import type { RestartPolicyOverview } from '../../server-status/model/overview'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ policy: RestartPolicyOverview }>()
const { d, t } = useI18n()
const parts = computed(() => (props.policy.scheduleDescription ?? '').split('·').map(value => value.trim()).filter(Boolean))
const rows = computed(() => [
  ['enabled', t(props.policy.isConfigured ? 'common.yes' : 'common.no')],
  ['expression', parts.value[0] || '—'],
  ['timezone', parts.value[1] || '—'],
  ['warning', parts.value.find(value => /warning|警告/i.test(value)) || '—'],
  ['saveWorld', parts.value.some(value => /save world|保存世界/i.test(value)) ? t('common.yes') : '—'],
  ['mode', parts.value.find(value => /graceful|force|优雅|强制/i.test(value)) || '—'],
  ['customCommand', parts.value.some(value => /custom command|自定义命令/i.test(value)) ? t('overview.restartPolicy.configured') : t('overview.restartPolicy.notConfigured')],
  ['bloodMoonDelay', parts.value.some(value => /blood moon|血月/i.test(value)) ? t('common.yes') : '—'],
  ['historyRetention', parts.value.find(value => /retain|保留/i.test(value)) || '—'],
  ['nextRestart', props.policy.nextRestartAtUtc ? d(new Date(props.policy.nextRestartAtUtc), 'medium') : '—'],
] as const)
</script>

<template>
  <UCard class="rounded-md">
    <template #header>
      <h2 class="font-semibold text-highlighted">
        {{ t('overview.restartPolicy.title') }}
      </h2>
    </template>
    <dl class="grid gap-x-6 gap-y-3 sm:grid-cols-2">
      <div v-for="row in rows" :key="row[0]">
        <dt class="text-xs text-muted">
          {{ t(`overview.restartPolicy.${row[0]}`) }}
        </dt><dd class="mt-1 text-sm text-highlighted">
          {{ row[1] }}
        </dd>
      </div>
    </dl>
  </UCard>
</template>
