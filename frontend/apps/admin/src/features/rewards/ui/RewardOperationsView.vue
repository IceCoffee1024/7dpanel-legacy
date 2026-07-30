<script setup lang="ts">
import type { GrantOperation, GrantRewardInput } from '../api/rewards'
import type { RewardOperationsController } from '../model/useRewards'

import { computed, reactive } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ controller: RewardOperationsController }>()
const emit = defineEmits<{ refresh: [], grant: [input: GrantRewardInput], confirm: [operation: GrantOperation], refund: [operation: GrantOperation], compensate: [operation: GrantOperation] }>()
const { t } = useI18n()
const form = reactive({ packageId: '', crossplatformId: '', expectedEntityId: 0, expectedWorldId: '' })
const valid = computed(() => form.packageId.trim() !== '' && form.crossplatformId.trim() !== '' && form.expectedEntityId >= 0 && form.expectedWorldId.trim() !== '')
function submit() {
  if (valid.value)
    emit('grant', { ...form, clientRequestKey: crypto.randomUUID() })
}
function stateColor(state: GrantOperation['state']) {
  if (state === 'Completed' || state === 'Compensated')
    return 'success' as const
  if (state === 'PendingReconciliation')
    return 'warning' as const
  if (state === 'Failed')
    return 'error' as const
  return 'neutral' as const
}
</script>

<template>
  <UDashboardPanel id="reward-operations">
    <template #header>
      <UDashboardNavbar :title="t('rewards.operations.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template><template #right>
          <UButton
            color="neutral"
            :label="t('rewards.common.refresh')"
            variant="outline"
            @click="emit('refresh')"
          />
        </template>
      </UDashboardNavbar>
    </template>
    <template #body>
      <UContainer class="space-y-5 py-5">
        <UAlert
          color="warning"
          icon="i-lucide-triangle-alert"
          :title="t('rewards.operations.reconciliation.title')"
          :description="t('rewards.operations.reconciliation.description')"
        />
        <UAlert
          v-if="props.controller.errorCode.value"
          color="error"
          :title="t('rewards.operations.operationIncomplete')"
          :description="props.controller.errorCode.value"
        />
        <UCard>
          <template #header>
            <div>
              <h2 class="font-semibold">
                {{ t('rewards.operations.manualGrant.title') }}
              </h2><p class="text-sm text-muted">
                {{ t('rewards.operations.manualGrant.description') }}
              </p>
            </div>
          </template>
          <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
            <UFormField :label="t('rewards.operations.manualGrant.packageId')">
              <UInput v-model="form.packageId" class="w-full" />
            </UFormField>
            <UFormField :label="t('rewards.operations.manualGrant.playerId')">
              <UInput v-model="form.crossplatformId" class="w-full" />
            </UFormField>
            <UFormField :label="t('rewards.operations.manualGrant.entityId')">
              <UInput
                v-model.number="form.expectedEntityId"
                class="w-full"
                min="0"
                type="number"
              />
            </UFormField>
            <UFormField :label="t('rewards.operations.manualGrant.worldId')">
              <UInput v-model="form.expectedWorldId" class="w-full" />
            </UFormField>
          </div>
          <template #footer>
            <div class="flex justify-end">
              <UButton :label="t('rewards.operations.manualGrant.submit')" :disabled="!valid || props.controller.mutatingOperationId.value !== null" @click="submit" />
            </div>
          </template>
        </UCard>
        <div v-if="props.controller.state.value === 'loading'" class="space-y-3">
          <USkeleton v-for="row in 4" :key="row" class="h-32 w-full" />
        </div>
        <UCard v-else-if="props.controller.operations.value.length === 0">
          <p class="text-sm text-muted">
            {{ t('rewards.operations.empty') }}
          </p>
        </UCard>
        <div v-else class="space-y-3">
          <UCard v-for="operation in props.controller.operations.value" :key="operation.operationId">
            <template #header>
              <div class="flex flex-wrap items-start justify-between gap-2">
                <div>
                  <p class="font-semibold">
                    {{ operation.crossplatformId }} · {{ operation.packageId }}
                  </p><p class="text-xs text-muted">
                    {{ operation.operationId }}
                  </p>
                </div><UBadge :color="stateColor(operation.state)" variant="subtle">
                  {{ operation.state === 'PendingReconciliation' ? 'PendingReconciliation / ResultUnknown' : operation.state }}
                </UBadge>
              </div>
            </template>
            <p v-if="operation.errorCode" class="text-sm text-error">
              {{ operation.errorCode }}
            </p>
            <div class="mt-3 grid gap-2 md:grid-cols-2">
              <div v-for="entry in operation.entries" :key="entry.operationEntryId" class="rounded-md border border-default p-2 text-xs">
                <span>{{ entry.kind }}</span><span class="float-right">{{ entry.state }}</span><p v-if="entry.errorCode" class="mt-1 text-error">
                  {{ entry.errorCode }}
                </p>
              </div>
            </div>
            <template #footer>
              <div class="flex flex-wrap justify-end gap-2">
                <UButton
                  v-if="operation.state === 'PendingReconciliation'"
                  color="success"
                  :label="t('rewards.operations.action.confirmCompleted')"
                  variant="outline"
                  :loading="props.controller.mutatingOperationId.value === operation.operationId"
                  @click="emit('confirm', operation)"
                /><UButton
                  v-if="operation.state === 'Completed'"
                  color="warning"
                  :label="t('rewards.operations.action.refund')"
                  variant="outline"
                  @click="emit('refund', operation)"
                /><UButton
                  v-if="operation.state === 'Failed' || operation.state === 'Refunded'"
                  :label="t('rewards.operations.action.compensate')"
                  variant="outline"
                  @click="emit('compensate', operation)"
                />
              </div>
            </template>
          </UCard>
        </div>
      </UContainer>
    </template>
  </UDashboardPanel>
</template>
