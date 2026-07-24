<script setup lang="ts">
import type { ApiKeyMetadata } from '../api/apiKeys'
import type { ApiKeysFeedback } from '../model/useApiKeys'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  apiKey: ApiKeyMetadata | null
  isSubmitting: boolean
  feedback: ApiKeysFeedback | null
}>()

const emit = defineEmits<{
  confirm: []
}>()
const open = defineModel<boolean>('open', { required: true })
const { t } = useI18n()
const feedbackMessage = computed(() => props.feedback === null
  ? ''
  : t(`apiKeys.feedback.${props.feedback.code}`))
const controlledOpen = computed({
  get: () => open.value,
  set: (value: boolean) => {
    if (!value && props.isSubmitting)
      return
    open.value = value
  },
})

function confirm() {
  if (props.apiKey === null || props.isSubmitting)
    return
  emit('confirm')
}
</script>

<template>
  <UModal
    v-model:open="controlledOpen"
    :title="t('apiKeys.revokeDialog.title')"
    :description="t('apiKeys.revokeDialog.description')"
    :dismissible="!isSubmitting"
    :close="isSubmitting ? false : undefined"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <div v-if="apiKey" class="space-y-4">
        <div class="rounded-md border border-default bg-elevated p-3">
          <p class="font-medium text-highlighted">
            {{ apiKey.name }}
          </p>
          <code class="mt-1 block overflow-wrap-anywhere text-xs text-muted">
            {{ apiKey.displayPrefix }}
          </code>
        </div>
        <p
          v-if="feedback"
          role="status"
          aria-live="polite"
          class="text-sm text-error"
        >
          {{ feedbackMessage }}
        </p>
      </div>
    </template>

    <template #footer>
      <UButton
        data-testid="cancel-revoke-api-key"
        :label="t('common.cancel')"
        color="neutral"
        variant="outline"
        :disabled="isSubmitting"
        @click="controlledOpen = false"
      />
      <UButton
        data-testid="confirm-revoke-api-key"
        :label="t('apiKeys.revokeDialog.confirm')"
        icon="i-lucide-trash-2"
        color="error"
        :loading="isSubmitting"
        :disabled="apiKey === null || isSubmitting"
        @click="confirm"
      />
    </template>
  </UModal>
</template>
