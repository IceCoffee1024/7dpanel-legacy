<script setup lang="ts">
import type { ChatSettings } from '../model/gameChatManagement'

import { reactive, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import { normalizeCommandPrefixes } from '../model/gameChatManagement'

const props = defineProps<{
  settings: ChatSettings
  isSaving: boolean
  isResetting: boolean
  feedbackMessage?: string | null
}>()

const emit = defineEmits<{
  save: [settings: ChatSettings]
  reset: []
  dirtyChange: [dirty: boolean]
}>()
const { t } = useI18n()

const draft = reactive({
  isEnabled: true,
  globalServerName: '',
  whisperServerName: '',
  commandPrefixes: [] as string[],
  excludeCommandsFromHistory: true,
  historyRetentionDays: 0,
})
let syncing = false
const validationError = shallowRef<'commandPrefixes' | 'historyRetentionDays' | null>(null)

watch(() => props.settings, (settings) => {
  syncing = true
  Object.assign(draft, {
    ...settings,
    globalServerName: settings.globalServerName ?? '',
    whisperServerName: settings.whisperServerName ?? '',
    commandPrefixes: [...settings.commandPrefixes],
  })
  queueMicrotask(() => {
    syncing = false
    emit('dirtyChange', false)
  })
}, { immediate: true, deep: true })

watch(draft, () => {
  if (!syncing)
    emit('dirtyChange', true)
}, { deep: true })

function submit() {
  const prefixes = normalizeCommandPrefixes(draft.commandPrefixes)
  if (prefixes === undefined || prefixes.length === 0) {
    validationError.value = 'commandPrefixes'
    return
  }
  if (!Number.isInteger(draft.historyRetentionDays)
    || draft.historyRetentionDays < 0
    || draft.historyRetentionDays > 3650) {
    validationError.value = 'historyRetentionDays'
    return
  }

  validationError.value = null
  emit('save', {
    isEnabled: draft.isEnabled,
    globalServerName: draft.globalServerName.trim() || null,
    whisperServerName: draft.whisperServerName.trim() || null,
    commandPrefixes: prefixes,
    excludeCommandsFromHistory: draft.excludeCommandsFromHistory,
    historyRetentionDays: draft.historyRetentionDays,
  })
}
</script>

<template>
  <section class="space-y-5" aria-labelledby="chat-settings-title">
    <header>
      <h1 id="chat-settings-title" class="text-lg font-semibold text-highlighted">
        {{ t('gameChat.settings.title') }}
      </h1>
      <p class="text-sm text-muted">
        {{ t('gameChat.settings.description') }}
      </p>
    </header>

    <UForm :state="draft" class="space-y-5" @submit="submit">
      <section class="space-y-4 rounded-lg border border-default p-4">
        <div>
          <h2 class="font-medium text-highlighted">{{ t('gameChat.settings.sections.featureTitle') }}</h2>
          <p class="text-sm text-muted">{{ t('gameChat.settings.sections.featureDescription') }}</p>
        </div>
        <UFormField :label="t('gameChat.settings.fields.enabled')" name="isEnabled">
          <USwitch v-model="draft.isEnabled" :label="t('gameChat.settings.fields.enabledDescription')" :disabled="isSaving || isResetting" />
        </UFormField>
        <div class="grid gap-4 md:grid-cols-2">
          <UFormField :label="t('gameChat.settings.fields.globalServerName')" name="globalServerName" :hint="t('gameChat.common.optional')">
            <UInput v-model="draft.globalServerName" class="w-full" :disabled="isSaving || isResetting" />
          </UFormField>
          <UFormField :label="t('gameChat.settings.fields.whisperServerName')" name="whisperServerName" :hint="t('gameChat.common.optional')">
            <UInput v-model="draft.whisperServerName" class="w-full" :disabled="isSaving || isResetting" />
          </UFormField>
        </div>
      </section>

      <section class="space-y-4 rounded-lg border border-default p-4">
        <div>
          <h2 class="font-medium text-highlighted">{{ t('gameChat.settings.sections.commandTitle') }}</h2>
          <p class="text-sm text-muted">{{ t('gameChat.settings.sections.commandDescription') }}</p>
        </div>
        <UFormField :label="t('gameChat.settings.fields.commandPrefixes')" name="commandPrefixes" :description="t('gameChat.settings.fields.commandPrefixesDescription')">
          <UInputTags
            v-model="draft.commandPrefixes"
            data-testid="command-prefixes"
            class="w-full"
            :disabled="isSaving || isResetting"
          />
        </UFormField>
        <UFormField :label="t('gameChat.settings.fields.excludeCommands')" name="excludeCommandsFromHistory">
          <UCheckbox
            v-model="draft.excludeCommandsFromHistory"
            :label="t('gameChat.settings.fields.excludeCommandsDescription')"
            :disabled="isSaving || isResetting"
          />
        </UFormField>
        <UFormField :label="t('gameChat.settings.fields.retentionDays')" name="historyRetentionDays" :description="t('gameChat.settings.fields.retentionDaysDescription')">
          <UInputNumber
            v-model="draft.historyRetentionDays"
            data-testid="history-retention-days"
            class="w-full md:w-64"
            :min="0"
            :max="3650"
            :disabled="isSaving || isResetting"
          />
        </UFormField>
      </section>

      <p v-if="validationError" role="alert" class="text-sm text-error">
        {{ t(`gameChat.settings.validation.${validationError}`) }}
      </p>

      <p v-if="feedbackMessage" role="status" class="text-sm text-error">
        {{ t(feedbackMessage) }}
      </p>

      <div class="flex flex-wrap justify-end gap-2">
        <UButton
          data-testid="reset-chat-settings"
          type="button"
          color="neutral"
          variant="outline"
          :label="t('gameChat.common.resetDefaults')"
          :loading="isResetting"
          :disabled="isSaving || isResetting"
          @click="emit('reset')"
        />
        <UButton
          type="submit"
          icon="i-lucide-save"
          :label="t('gameChat.settings.save')"
          :loading="isSaving"
          :disabled="isSaving || isResetting"
        />
      </div>
    </UForm>
  </section>
</template>
