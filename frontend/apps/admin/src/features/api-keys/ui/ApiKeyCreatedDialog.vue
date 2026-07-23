<script setup lang="ts">
import type { CreatedApiKey } from '../api/apiKeys'

import { shallowRef } from 'vue'

const props = defineProps<{
  createdApiKey: CreatedApiKey | null
}>()

const open = defineModel<boolean>('open', { required: true })
const copyFeedback = shallowRef<string | null>(null)

async function copyApiKey() {
  const apiKey = props.createdApiKey?.apiKey
  if (apiKey === undefined)
    return

  try {
    if (!navigator.clipboard)
      throw new Error('Clipboard API unavailable')
    await navigator.clipboard.writeText(apiKey)
    copyFeedback.value = 'API Key 已复制'
  }
  catch {
    copyFeedback.value = '复制失败，请手动保存 API Key'
  }
}
</script>

<template>
  <UModal
    v-model:open="open"
    title="API Key 已创建"
    description="请立即复制并保存。关闭此窗口后将无法再次查看完整 API Key。"
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
        label="复制 API Key"
        icon="i-lucide-copy"
        variant="outline"
        @click="copyApiKey"
      />
      <UButton
        data-testid="close-created-api-key"
        label="我已安全保存"
        icon="i-lucide-check"
        @click="open = false"
      />
    </template>
  </UModal>
</template>
