<script setup lang="ts">
import type { HostOverview } from '../model/overview'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ host: HostOverview, isOwner: boolean }>()
const { t } = useI18n()
const rows = computed(() => [
  ['operatingSystem', [props.host.operatingSystem, props.host.operatingSystemVersion].filter(Boolean).join(' ') || null],
  ['osFamily', props.host.osFamily],
  ['architecture', props.host.operatingSystemArchitecture],
  ['runtime', props.host.runtimeVersion],
  ['cpu', props.host.cpuModel],
  ['cores', props.host.logicalCoreCount ?? props.host.processorCount],
  ['device', [props.host.deviceName, props.host.deviceModel, props.host.deviceType].filter(Boolean).join(' · ') || null],
] as const)
const sensitiveRows = computed(() => props.isOwner
  ? [
      ...(props.host.publicNetwork.ipv4 ? [['publicIpv4', props.host.publicNetwork.ipv4] as const] : []),
      ...(props.host.publicNetwork.ipv6 ? [['publicIpv6', props.host.publicNetwork.ipv6] as const] : []),
      ...(props.host.currentSystemUser ? [['systemUser', props.host.currentSystemUser] as const] : []),
      ...(props.host.deviceId ? [['deviceId', props.host.deviceId] as const] : []),
    ]
  : [])
</script>

<template>
  <UCard class="rounded-md">
    <template #header>
      <h2 class="font-semibold text-highlighted">
        {{ t('overview.hostPlatform.title') }}
      </h2>
    </template>
    <dl class="grid gap-x-6 gap-y-3 sm:grid-cols-2">
      <div v-for="row in [...rows, ...sensitiveRows]" :key="row[0]" class="min-w-0">
        <dt class="text-xs text-muted">
          {{ t(`overview.hostPlatform.${row[0]}`) }}
        </dt><dd class="mt-1 break-all text-sm text-highlighted">
          {{ row[1] ?? '—' }}
        </dd>
      </div>
    </dl>
  </UCard>
</template>
