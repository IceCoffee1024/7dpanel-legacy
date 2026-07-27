<script setup lang="ts">
import type { ColoredChatProfile, ColoredChatProfileDraft } from '../model/gameChatManagement'

import { computed, reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import { coloredChatTemplateVariables, normalizeChatColor, toChatColorPickerValue } from '../model/gameChatManagement'
import ColoredChatPreview from './ColoredChatPreview.vue'

const props = defineProps<{
  open: boolean
  mode: 'create' | 'edit'
  profile: ColoredChatProfile | null
  isSubmitting: boolean
  feedbackMessage?: string | null
}>()

const emit = defineEmits<{
  'update:open': [open: boolean]
  'submit': [profile: ColoredChatProfileDraft]
  'cancel': []
}>()
const { t } = useI18n()

const draft = reactive({
  crossplatformId: '',
  customName: '',
  nameColor: '',
  textColor: '',
  description: '',
})
const validationError = shallowRef<'crossplatformId' | 'color' | null>(null)

const controlledOpen = computed({
  get: () => props.open,
  set: (open: boolean) => {
    if (!open && props.isSubmitting)
      return
    emit('update:open', open)
    if (!open)
      emit('cancel')
  },
})

watch([() => props.open, () => props.profile, () => props.mode], ([open]) => {
  if (!open)
    return
  Object.assign(draft, {
    crossplatformId: props.profile?.crossplatformId ?? '',
    customName: props.profile?.customName ?? '',
    nameColor: props.profile?.nameColor ?? '',
    textColor: props.profile?.textColor ?? '',
    description: props.profile?.description ?? '',
  })
}, { immediate: true, deep: true })

function insertVariable(variable: (typeof coloredChatTemplateVariables)[number]) {
  draft.customName += `{${variable}}`
}

function submit() {
  const crossplatformId = draft.crossplatformId.trim()
  const nameColor = normalizeChatColor(draft.nameColor)
  const textColor = normalizeChatColor(draft.textColor)
  if (crossplatformId === '' || /\s/.test(crossplatformId)) {
    validationError.value = 'crossplatformId'
    return
  }
  if (nameColor === undefined || textColor === undefined) {
    validationError.value = 'color'
    return
  }

  validationError.value = null
  emit('submit', {
    crossplatformId,
    customName: draft.customName.trim() || null,
    nameColor,
    textColor,
    description: draft.description.trim() || null,
  })
}
</script>

<template>
  <UModal
    v-model:open="controlledOpen"
    :title="mode === 'create' ? t('gameChat.colored.profileDialog.createTitle') : t('gameChat.colored.profileDialog.editTitle')"
    :description="t('gameChat.colored.profileDialog.description')"
    :dismissible="!isSubmitting"
    :close="isSubmitting ? false : undefined"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <UForm :state="draft" class="space-y-4" @submit="submit">
        <UFormField :label="t('gameChat.common.crossplatformId')" name="crossplatformId" required>
          <UInput
            v-model="draft.crossplatformId"
            data-testid="profile-id"
            class="w-full"
            :disabled="mode === 'edit' || isSubmitting"
          />
        </UFormField>

        <UFormField
          :label="t('gameChat.colored.profileDialog.nameTemplate')"
          name="customName"
          :hint="t('gameChat.common.optional')"
          :description="t('gameChat.colored.profileDialog.nameTemplateDescription')"
        >
          <UInput v-model="draft.customName" data-testid="profile-custom-name" class="w-full" :disabled="isSubmitting" />
          <div class="mt-2 flex flex-wrap gap-2">
            <UButton
              v-for="variable in coloredChatTemplateVariables"
              :key="variable"
              :data-testid="`insert-${variable}`"
              type="button"
              size="xs"
              color="neutral"
              variant="outline"
              :label="`{${variable}}`"
              :disabled="isSubmitting"
              @click="insertVariable(variable)"
            />
          </div>
        </UFormField>

        <div class="grid gap-4 md:grid-cols-2">
          <UFormField :label="t('gameChat.colored.profiles.nameColor')" name="nameColor" :hint="t('gameChat.common.mayBeEmpty')">
            <div class="space-y-2">
              <UColorPicker :model-value="toChatColorPickerValue(draft.nameColor)" format="hex" :disabled="isSubmitting" @update:model-value="draft.nameColor = $event ?? ''" />
              <UInput
                v-model="draft.nameColor"
                data-testid="profile-name-color"
                class="w-full font-mono"
                placeholder="RRGGBB"
                :disabled="isSubmitting"
              />
            </div>
          </UFormField>
          <UFormField :label="t('gameChat.colored.profiles.textColor')" name="textColor" :hint="t('gameChat.common.mayBeEmpty')">
            <div class="space-y-2">
              <UColorPicker :model-value="toChatColorPickerValue(draft.textColor)" format="hex" :disabled="isSubmitting" @update:model-value="draft.textColor = $event ?? ''" />
              <UInput
                v-model="draft.textColor"
                data-testid="profile-text-color"
                class="w-full font-mono"
                placeholder="RRGGBB"
                :disabled="isSubmitting"
              />
            </div>
          </UFormField>
        </div>

        <UFormField :label="t('gameChat.colored.profileDialog.notes')" name="description" :hint="t('gameChat.colored.profileDialog.notesHint')">
          <UTextarea v-model="draft.description" class="w-full" :rows="3" :disabled="isSubmitting" />
        </UFormField>

        <ColoredChatPreview
          :custom-name="draft.customName"
          :name-color="draft.nameColor"
          :text-color="draft.textColor"
        />

        <p v-if="validationError" role="alert" class="text-sm text-error">
          {{ t(`gameChat.colored.profileDialog.validation.${validationError}`) }}
        </p>

        <p v-if="feedbackMessage" role="status" class="text-sm text-error">
          {{ t(feedbackMessage) }}
        </p>

        <button class="sr-only" type="submit">{{ t('gameChat.colored.profileDialog.submit') }}</button>
      </UForm>
    </template>

    <template #footer>
      <UButton
        type="button"
        color="neutral"
        variant="outline"
        :label="t('gameChat.common.cancel')"
        :disabled="isSubmitting"
        @click="controlledOpen = false"
      />
      <UButton
        type="button"
        icon="i-lucide-save"
        :label="mode === 'create' ? t('gameChat.colored.profileDialog.create') : t('gameChat.colored.profileDialog.save')"
        :loading="isSubmitting"
        :disabled="isSubmitting"
        @click="submit"
      />
    </template>
  </UModal>
</template>
