<script setup lang="ts">
import type { RestartServerErrorCode, RestartServerState } from '../model/useRestartServer'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import { operationStatus } from '../../../shared/model/operationStatus'

const props = defineProps<{
  state: RestartServerState
  errorCode: RestartServerErrorCode | null
  operationId?: string | null
}>()
const emit = defineEmits<{ cancel: [], confirm: [] }>()
const { t } = useI18n()
const open = computed(() => props.state === 'confirming' || props.state === 'submitting')
const status = computed(() => operationStatus(props.state))
function updateOpen(value: boolean) {
  if (!value && props.state === 'confirming')
    emit('cancel')
}
</script>

<template>
  <UModal
    :open="open"
    :portal="false"
    :dismissible="state !== 'submitting'"
    :title="t('overview.restartDialog.title')"
    :description="t('overview.restartDialog.description')"
    @update:open="updateOpen"
  >
    <template #body>
      <div data-testid="restart-dialog">
        <UAlert color="warning" variant="subtle" :description="t('overview.restartDialog.risk')" />
      </div>
    </template>
    <template #footer>
      <div class="flex justify-end gap-2">
        <UButton
          data-testid="restart-cancel"
          color="neutral"
          variant="outline"
          :disabled="state === 'submitting'"
          :label="t('common.cancel')"
          @click="emit('cancel')"
        /><UButton
          data-testid="restart-confirm"
          color="warning"
          icon="i-lucide-rotate-ccw"
          :loading="state === 'submitting'"
          :disabled="state === 'submitting'"
          :label="t('overview.restartDialog.confirm')"
          @click="emit('confirm')"
        />
      </div>
    </template>
  </UModal>
  <UAlert
    v-if="state === 'accepted' || state === 'queued' || state === 'running'"
    :color="status.tone"
    variant="subtle"
    :title="t(status.i18nKey)"
    :description="t('overview.restartDialog.accepted')"
  />
  <UAlert
    v-else-if="state === 'succeeded'"
    :color="status.tone"
    variant="subtle"
    :title="t(status.i18nKey)"
  />
  <UAlert
    v-else-if="state === 'failed' || state === 'cancelled' || state === 'result-unknown'"
    :color="status.tone"
    variant="subtle"
    :title="t(status.i18nKey)"
    :description="t('overview.restartDialog.failed')"
  />
  <p
    v-if="operationId && state !== 'idle' && state !== 'confirming' && state !== 'submitting'"
    class="mt-3 break-all text-xs text-muted"
    data-testid="restart-operation-receipt"
  >
    {{ t('overview.operationReceipt', { operationId }) }}
  </p>
  <p
    v-if="errorCode && state !== 'idle' && state !== 'confirming' && state !== 'submitting'"
    class="mt-2 break-all text-xs text-muted"
    data-testid="restart-error-code"
  >
    {{ t('common.errorCode', { code: errorCode }) }}
  </p>
</template>
