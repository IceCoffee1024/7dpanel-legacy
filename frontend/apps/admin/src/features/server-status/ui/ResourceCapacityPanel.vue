<script setup lang="ts">
import type { HostOverview } from '../model/overview'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { formatBytes, usedPercent } from './formatOverview'

const props = defineProps<{ host: HostOverview, isOwner: boolean }>()
const { locale, t } = useI18n()
const physicalUsed = computed(() => props.host.memoryTotalBytes !== null && props.host.memoryAvailableBytes !== null ? Math.max(0, props.host.memoryTotalBytes - props.host.memoryAvailableBytes) : null)
const physicalPercent = computed(() => usedPercent(physicalUsed.value, props.host.memoryTotalBytes))
const additionalPercent = computed(() => usedPercent(props.host.additionalMemory?.usedBytes ?? null, props.host.additionalMemory?.totalBytes ?? null))
const volumes = computed(() => props.host.storageVolumes.map(volume => ({
  ...volume,
  usedBytes: volume.totalBytes !== null && volume.availableBytes !== null ? Math.max(0, volume.totalBytes - volume.availableBytes) : null,
  percent: usedPercent(volume.totalBytes !== null && volume.availableBytes !== null ? Math.max(0, volume.totalBytes - volume.availableBytes) : null, volume.totalBytes),
})))
</script>

<template>
  <UCard class="rounded-md">
    <template #header>
      <h2 class="font-semibold text-highlighted">
        {{ t('overview.resources.title') }}
      </h2>
    </template>
    <div class="space-y-5">
      <div>
        <div class="flex justify-between gap-3 text-sm">
          <span>{{ t('overview.resources.physicalMemory') }}</span><span>{{ formatBytes(physicalUsed, locale) }} / {{ formatBytes(host.memoryTotalBytes, locale) }}</span>
        </div>
        <UProgress
          v-if="physicalPercent !== null"
          class="mt-2"
          :model-value="physicalPercent"
          :max="100"
        />
      </div>
      <div v-if="host.additionalMemory">
        <div class="flex justify-between gap-3 text-sm">
          <span>{{ t(`overview.resources.${host.additionalMemory.kind}`) }}</span><span>{{ formatBytes(host.additionalMemory.usedBytes, locale) }} / {{ formatBytes(host.additionalMemory.totalBytes, locale) }}</span>
        </div>
        <UProgress
          v-if="additionalPercent !== null"
          class="mt-2"
          :model-value="additionalPercent"
          :max="100"
        />
      </div>
      <dl class="grid gap-3 sm:grid-cols-3">
        <div>
          <dt class="text-xs text-muted">
            {{ t('overview.resources.rss') }}
          </dt><dd class="mt-1 text-sm">
            {{ formatBytes(host.residentSetBytes, locale) }}
          </dd>
        </div>
        <div>
          <dt class="text-xs text-muted">
            {{ t('overview.resources.managedHeap') }}
          </dt><dd class="mt-1 text-sm">
            {{ formatBytes(host.managedHeapBytes, locale) }}
          </dd>
        </div>
        <div>
          <dt class="text-xs text-muted">
            {{ t('overview.resources.otherMemory') }}
          </dt><dd class="mt-1 text-sm">
            {{ formatBytes(host.otherMemoryBytes, locale) }}
          </dd>
        </div>
      </dl>
      <div class="border-t border-muted pt-4">
        <h3 class="mb-3 text-sm font-medium">
          {{ t('overview.resources.storage') }}
        </h3>
        <div class="space-y-4">
          <div v-for="volume in volumes" :key="volume.name">
            <div class="flex justify-between gap-3 text-sm">
              <span>{{ volume.name }}<span v-if="isOwner && volume.rootPath"> · {{ volume.rootPath }}</span></span><span>{{ formatBytes(volume.usedBytes ?? volume.availableBytes, locale) }}<template v-if="volume.totalBytes !== null"> / {{ formatBytes(volume.totalBytes, locale) }}</template></span>
            </div>
            <UProgress
              v-if="volume.percent !== null"
              class="mt-2"
              :model-value="volume.percent"
              :max="100"
            />
          </div>
        </div>
      </div>
    </div>
  </UCard>
</template>
