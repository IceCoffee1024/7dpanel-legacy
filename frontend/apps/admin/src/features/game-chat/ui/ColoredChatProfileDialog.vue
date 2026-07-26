<script setup lang="ts">
import type { ColoredChatProfile, ColoredChatProfileDraft } from '../model/gameChatManagement'

import { computed, reactive, watch } from 'vue'

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

const draft = reactive({
  crossplatformId: '',
  customName: '',
  nameColor: '',
  textColor: '',
  description: '',
})

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
  if (crossplatformId === '' || /\s/.test(crossplatformId))
    return
  if (nameColor === undefined || textColor === undefined)
    return

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
    :title="mode === 'create' ? '新增玩家 Profile' : '编辑玩家 Profile'"
    description="Profile 使用稳定跨平台 ID；编辑时业务键不可修改。"
    :dismissible="!isSubmitting"
    :close="isSubmitting ? false : undefined"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <UForm :state="draft" class="space-y-4" @submit="submit">
        <UFormField label="跨平台 ID" name="crossplatformId" required>
          <UInput
            v-model="draft.crossplatformId"
            data-testid="profile-id"
            class="w-full"
            :disabled="mode === 'edit' || isSubmitting"
          />
        </UFormField>

        <UFormField
          label="名称模板"
          name="customName"
          hint="可选"
          description="只替换下列四个变量；未知变量保持普通文本。"
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
          <UFormField label="名称颜色" name="nameColor" hint="可留空">
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
          <UFormField label="正文颜色" name="textColor" hint="可留空">
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

        <UFormField label="运营备注" name="description" hint="只在面板显示">
          <UTextarea v-model="draft.description" class="w-full" :rows="3" :disabled="isSubmitting" />
        </UFormField>

        <ColoredChatPreview
          :custom-name="draft.customName"
          :name-color="draft.nameColor"
          :text-color="draft.textColor"
        />

        <p v-if="feedbackMessage" role="status" class="text-sm text-error">
          {{ feedbackMessage }}
        </p>

        <button class="sr-only" type="submit">提交 Profile</button>
      </UForm>
    </template>

    <template #footer>
      <UButton
        type="button"
        color="neutral"
        variant="outline"
        label="取消"
        :disabled="isSubmitting"
        @click="controlledOpen = false"
      />
      <UButton
        type="button"
        icon="i-lucide-save"
        :label="mode === 'create' ? '创建 Profile' : '保存 Profile'"
        :loading="isSubmitting"
        :disabled="isSubmitting"
        @click="submit"
      />
    </template>
  </UModal>
</template>
