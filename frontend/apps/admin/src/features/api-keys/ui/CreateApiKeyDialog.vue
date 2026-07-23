<script setup lang="ts">
import type { CreateApiKeyInput } from '../api/apiKeys'
import type { ApiKeysFeedback } from '../model/useApiKeys'

import { computed, shallowRef, watch } from 'vue'

const props = defineProps<{
  isCreating: boolean
  feedback: ApiKeysFeedback | null
}>()

const emit = defineEmits<{
  create: [input: CreateApiKeyInput]
}>()
const open = defineModel<boolean>('open', { required: true })
const name = shallowRef('')
const expiresAtUtc = shallowRef('')

const normalizedName = computed(() => name.value.trim())
const nameLength = computed(() => Array.from(normalizedName.value).length)
const canCreate = computed(() => !props.isCreating && nameLength.value >= 1 && nameLength.value <= 80)
const controlledOpen = computed({
  get: () => open.value,
  set: (value: boolean) => {
    if (!value && props.isCreating)
      return
    open.value = value
  },
})

watch(open, (isOpen) => {
  if (!isOpen)
    return
  name.value = ''
  expiresAtUtc.value = ''
}, { immediate: true })

function create() {
  if (!canCreate.value)
    return

  emit('create', {
    name: normalizedName.value,
    ...(expiresAtUtc.value.trim() === '' ? {} : { expiresAtUtc: expiresAtUtc.value.trim() }),
  })
}
</script>

<template>
  <UModal
    v-model:open="controlledOpen"
    title="创建 API Key"
    description="完整 API Key 只会显示一次。"
    :dismissible="!isCreating"
    :close="isCreating ? false : undefined"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <div class="space-y-5">
        <UFormField
          label="名称"
          name="api-key-name"
          required
          :hint="`${nameLength}/80`"
        >
          <UInput
            v-model="name"
            :disabled="isCreating"
            :maxlength="80"
            autocomplete="off"
            class="w-full"
            placeholder="例如：夜间备份"
          />
        </UFormField>

        <UFormField
          label="到期时间"
          name="api-key-expiration"
          hint="可选，使用 UTC 时间戳"
        >
          <UInput
            v-model="expiresAtUtc"
            :disabled="isCreating"
            autocomplete="off"
            class="w-full"
            placeholder="2026-08-23T08:00:00Z"
          />
        </UFormField>

        <p
          v-if="feedback"
          role="status"
          aria-live="polite"
          class="text-sm text-error"
        >
          {{ feedback.message }}
        </p>
      </div>
    </template>

    <template #footer>
      <UButton
        label="取消"
        color="neutral"
        variant="outline"
        :disabled="isCreating"
        @click="controlledOpen = false"
      />
      <UButton
        data-testid="create-api-key-submit"
        label="创建 API Key"
        icon="i-lucide-key-round"
        :loading="isCreating"
        :disabled="!canCreate"
        @click="create"
      />
    </template>
  </UModal>
</template>
