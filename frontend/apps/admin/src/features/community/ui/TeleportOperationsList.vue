<script setup lang="ts">
import type { TeleportOperation } from '../api/community'
import type { CommunityViewState } from '../model/useCommunity'

import { useI18n } from 'vue-i18n'

import CommunityStateAlert from './CommunityStateAlert.vue'

defineProps<{
  operations: readonly TeleportOperation[]
  state: CommunityViewState
}>()

defineEmits<{ retry: [] }>()

const { t } = useI18n()

function operationColor(state: TeleportOperation['state']) {
  if (state === 'Completed')
    return 'success' as const
  if (state === 'Failed' || state === 'Refunded')
    return 'error' as const
  if (state === 'PendingReconciliation')
    return 'warning' as const
  return 'neutral' as const
}
</script>

<template>
  <section class="space-y-3" aria-labelledby="teleport-operations-heading">
    <div>
      <h2 id="teleport-operations-heading" class="text-base font-semibold text-highlighted">{{ t('community.teleport.operationsTitle') }}</h2>
      <p class="text-sm text-muted">{{ t('community.teleport.operationsDescription') }}</p>
    </div>
    <CommunityStateAlert :state="state" :subject="t('community.teleport.operationsSubject')" @retry="$emit('retry')" />
    <div v-if="state === 'loading' && operations.length === 0" class="space-y-3"><USkeleton v-for="row in 2" :key="row" class="h-44 w-full" /></div>
    <UCard v-else-if="state === 'empty'"><p class="text-sm text-muted">{{ t('community.teleport.operationsEmpty') }}</p></UCard>
    <div v-else-if="state !== 'forbidden' && state !== 'unavailable'" class="grid gap-3 xl:grid-cols-2">
      <UCard v-for="operation in operations" :key="operation.operationId">
        <template #header>
          <div class="flex min-w-0 flex-wrap items-start justify-between gap-2">
            <div class="min-w-0"><h3 class="break-all font-semibold text-highlighted">{{ operation.operationId }}</h3><p class="text-xs text-muted">{{ operation.kind }}</p></div>
            <UBadge :color="operationColor(operation.state)" variant="subtle">{{ operation.state === 'PendingReconciliation' ? t('community.teleport.pendingReconciliation') : operation.state }}</UBadge>
          </div>
        </template>
        <dl class="grid gap-3 text-sm sm:grid-cols-2 xl:grid-cols-4">
          <div><dt class="text-muted">{{ t('community.teleport.player') }}</dt><dd class="mt-1 break-all">{{ operation.crossplatformId }}</dd></div>
          <div><dt class="text-muted">{{ t('community.teleport.targetPlayer') }}</dt><dd class="mt-1 break-all">{{ operation.targetCrossplatformId ?? t('community.teleport.noTargetPlayer') }}</dd></div>
          <div><dt class="text-muted">{{ t('community.teleport.updatedAt') }}</dt><dd class="mt-1 break-all">{{ operation.updatedAtUtc }}</dd></div>
          <div><dt class="text-muted">{{ t('community.teleport.destinationWorld') }}</dt><dd class="mt-1">{{ operation.destination.worldId }}</dd></div>
          <div><dt class="text-muted">{{ t('community.teleport.destinationCoordinates') }}</dt><dd class="mt-1">{{ t('community.common.coordinates', { x: operation.destination.x, y: operation.destination.y, z: operation.destination.z }) }}</dd></div>
        </dl>
      </UCard>
    </div>
  </section>
</template>
