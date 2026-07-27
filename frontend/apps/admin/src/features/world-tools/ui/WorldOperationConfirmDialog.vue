<script setup lang="ts">
import type { WorldOperationReview } from '../model/worldOperationForm'

import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  open: boolean
  review: WorldOperationReview | null
  submitting: boolean
}>()
const emit = defineEmits<{
  cancel: []
  confirm: []
}>()
const { t } = useI18n()
const strongConfirmationText = shallowRef('')
const canConfirm = computed(() => props.review !== null
  && !props.submitting
  && (!props.review.strongConfirmation || strongConfirmationText.value === 'CONFIRM'))

watch(() => props.open, (open) => {
  if (open)
    strongConfirmationText.value = ''
})

function updateOpen(open: boolean) {
  if (!open && !props.submitting)
    emit('cancel')
}

function confirm() {
  if (canConfirm.value)
    emit('confirm')
}
</script>

<template>
  <UModal
    :open="props.open"
    :dismissible="!props.submitting"
    :title="props.review?.label ?? t('worldTools.confirm.title')"
    :description="t('worldTools.confirm.description')"
    :ui="{ footer: 'justify-end' }"
    @update:open="updateOpen"
  >
    <template #body>
      <div v-if="props.review" class="space-y-4">
        <UAlert
          color="warning"
          icon="i-lucide-triangle-alert"
          :title="t('worldTools.confirm.dangerTitle')"
          :description="t('worldTools.confirm.dangerDescription')"
          variant="subtle"
        />

        <dl class="grid gap-3 rounded-lg border border-default p-4 sm:grid-cols-2">
          <div><dt class="text-xs font-medium text-muted">{{ t('worldTools.confirm.target') }}</dt><dd class="break-words text-sm text-highlighted">{{ props.review.target }}</dd></div>
          <div><dt class="text-xs font-medium text-muted">{{ t('worldTools.confirm.world') }}</dt><dd class="break-all text-sm text-highlighted">{{ props.review.worldId }}</dd></div>
          <div class="sm:col-span-2"><dt class="text-xs font-medium text-muted">{{ t('worldTools.confirm.scope') }}</dt><dd class="break-words text-sm text-highlighted">{{ props.review.scope }}</dd></div>
          <div><dt class="text-xs font-medium text-muted">{{ t('worldTools.confirm.worldVersion') }}</dt><dd class="break-all text-sm text-highlighted">{{ props.review.worldVersion }}</dd></div>
          <div><dt class="text-xs font-medium text-muted">{{ t('worldTools.confirm.mapResourceVersion') }}</dt><dd class="break-all text-sm text-highlighted">{{ props.review.mapResourceVersion ?? '—' }}</dd></div>
          <div v-if="props.review.catalogVersion" class="sm:col-span-2"><dt class="text-xs font-medium text-muted">{{ t('worldTools.confirm.catalogVersion') }}</dt><dd class="break-all text-sm text-highlighted">{{ props.review.catalogVersion }}</dd></div>
          <div class="sm:col-span-2"><dt class="text-xs font-medium text-muted">{{ t('worldTools.confirm.expectedImpact') }}</dt><dd class="text-sm text-highlighted">{{ props.review.impact }}</dd></div>
          <div class="sm:col-span-2"><dt class="text-xs font-medium text-muted">{{ t('worldTools.confirm.reversibility') }}</dt><dd class="text-sm font-medium" :class="props.review.reversible ? 'text-success' : 'text-warning'">{{ props.review.reversible ? t('worldTools.confirm.reversible') : t('worldTools.confirm.notReversible') }}</dd></div>
        </dl>

        <UFormField
          v-if="props.review.strongConfirmation"
          :label="t('worldTools.confirm.strongConfirmation')"
          :description="t('worldTools.confirm.strongConfirmationDescription')"
          required
        >
          <UInput v-model="strongConfirmationText" autocomplete="off" class="w-full" placeholder="CONFIRM" />
        </UFormField>
      </div>
    </template>

    <template #footer>
      <UButton
        color="neutral"
        :label="t('worldTools.common.cancel')"
        variant="outline"
        :disabled="props.submitting"
        @click="emit('cancel')"
      />
      <UButton
        data-testid="confirm-world-operation"
        color="error"
        icon="i-lucide-triangle-alert"
        :label="t('worldTools.confirm.confirmOperation')"
        :disabled="!canConfirm"
        :loading="props.submitting"
        @click="confirm"
      />
    </template>
  </UModal>
</template>
