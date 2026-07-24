<script setup lang="ts">
import type { CreateApiKeyInput } from '../api/apiKeys'
import type { ApiKeysFeedback } from '../model/useApiKeys'

import * as v from 'valibot'
import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  isCreating: boolean
  feedback: ApiKeysFeedback | null
}>()

const emit = defineEmits<{
  create: [input: CreateApiKeyInput]
}>()
const open = defineModel<boolean>('open', { required: true })
const { t } = useI18n()
const name = shallowRef('')
const expiresAtUtc = shallowRef('')
const ApiKeyNameSchema = v.pipe(
  v.string(),
  v.trim(),
  v.minLength(1),
  v.check(value => Array.from(value).length <= 80),
)

const normalizedName = computed(() => name.value.trim())
const nameLength = computed(() => Array.from(normalizedName.value).length)
const canCreate = computed(() => !props.isCreating && v.safeParse(ApiKeyNameSchema, name.value).success)
const feedbackMessage = computed(() => props.feedback === null
  ? ''
  : t(`apiKeys.feedback.${props.feedback.code}`))
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
    :title="t('apiKeys.createDialog.title')"
    :description="t('apiKeys.createDialog.description')"
    :dismissible="!isCreating"
    :close="isCreating ? false : undefined"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <div class="space-y-5">
        <UFormField
          :label="t('apiKeys.createDialog.name')"
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
            :placeholder="t('apiKeys.createDialog.namePlaceholder')"
          />
        </UFormField>

        <UFormField
          :label="t('apiKeys.createDialog.expiration')"
          name="api-key-expiration"
          :hint="t('apiKeys.createDialog.expirationHint')"
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
          {{ feedbackMessage }}
        </p>
      </div>
    </template>

    <template #footer>
      <UButton
        :label="t('common.cancel')"
        color="neutral"
        variant="outline"
        :disabled="isCreating"
        @click="controlledOpen = false"
      />
      <UButton
        data-testid="create-api-key-submit"
        :label="t('apiKeys.create')"
        icon="i-lucide-key-round"
        :loading="isCreating"
        :disabled="!canCreate"
        @click="create"
      />
    </template>
  </UModal>
</template>
