<script setup lang="ts">
import type { CreatedApiKey } from '../api/apiKeys'

import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  createdApiKey: CreatedApiKey | null
}>()

const open = defineModel<boolean>('open', { required: true })
const { t } = useI18n()
const copyFeedback = shallowRef<string | null>(null)

async function copyApiKey() {
  const apiKey = props.createdApiKey?.apiKey
  if (apiKey === undefined)
    return

  try {
    if (!navigator.clipboard)
      throw new Error('Clipboard API unavailable')
    await navigator.clipboard.writeText(apiKey)
    copyFeedback.value = t('apiKeys.createdDialog.copySuccess')
  }
  catch {
    copyFeedback.value = t('apiKeys.createdDialog.copyFailure')
  }
}
</script>

<template>
  <UModal
    v-model:open="open"
    :title="t('apiKeys.createdDialog.title')"
    :description="t('apiKeys.createdDialog.description')"
    :dismissible="false"
    :close="false"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <div v-if="createdApiKey" class="space-y-4">
        <p class="text-sm text-muted">
          {{ createdApiKey.name }}
        </p>
        <code
          data-testid="one-time-api-key"
          class="block overflow-wrap-anywhere rounded-md border border-default bg-elevated p-3 font-mono text-sm text-highlighted"
        >{{ createdApiKey.apiKey }}</code>
        <p
          v-if="copyFeedback"
          role="status"
          aria-live="polite"
          class="text-sm text-muted"
        >
          {{ copyFeedback }}
        </p>
      </div>
    </template>

    <template #footer>
      <UButton
        data-testid="copy-api-key"
        :label="t('apiKeys.createdDialog.copy')"
        icon="i-lucide-copy"
        variant="outline"
        @click="copyApiKey"
      />
      <UButton
        data-testid="close-created-api-key"
        :label="t('apiKeys.createdDialog.saved')"
        icon="i-lucide-check"
        @click="open = false"
      />
    </template>
  </UModal>
</template>
