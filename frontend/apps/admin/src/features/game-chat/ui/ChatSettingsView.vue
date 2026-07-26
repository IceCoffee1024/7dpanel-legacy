<script setup lang="ts">
import type { ChatSettings } from '../model/gameChatManagement'

import { reactive, watch } from 'vue'

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

const draft = reactive({
  isEnabled: true,
  globalServerName: '',
  whisperServerName: '',
  commandPrefixes: [] as string[],
  excludeCommandsFromHistory: true,
  historyRetentionDays: 0,
})
let syncing = false

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
  if (prefixes === undefined || prefixes.length === 0)
    return
  if (!Number.isInteger(draft.historyRetentionDays)
    || draft.historyRetentionDays < 0
    || draft.historyRetentionDays > 3650)
    return

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
        聊天设置
      </h1>
      <p class="text-sm text-muted">
        关闭聊天功能不会删除已有历史；重新启用后才继续捕获和允许面板发送。
      </p>
    </header>

    <UForm :state="draft" class="space-y-5" @submit="submit">
      <section class="space-y-4 rounded-lg border border-default p-4">
        <div>
          <h2 class="font-medium text-highlighted">功能与发送名称</h2>
          <p class="text-sm text-muted">控制捕获和面板发送，并设置服务端显示名称。</p>
        </div>
        <UFormField label="启用聊天功能" name="isEnabled">
          <USwitch v-model="draft.isEnabled" label="允许捕获聊天和从面板发送消息" :disabled="isSaving || isResetting" />
        </UFormField>
        <div class="grid gap-4 md:grid-cols-2">
          <UFormField label="全局消息服务端名称" name="globalServerName" hint="可选">
            <UInput v-model="draft.globalServerName" class="w-full" :disabled="isSaving || isResetting" />
          </UFormField>
          <UFormField label="私聊消息服务端名称" name="whisperServerName" hint="可选">
            <UInput v-model="draft.whisperServerName" class="w-full" :disabled="isSaving || isResetting" />
          </UFormField>
        </div>
      </section>

      <section class="space-y-4 rounded-lg border border-default p-4">
        <div>
          <h2 class="font-medium text-highlighted">命令与历史</h2>
          <p class="text-sm text-muted">每个命令前缀必须是一个非空白字符；0 表示不自动清理。</p>
        </div>
        <UFormField label="命令前缀" name="commandPrefixes" description="输入一个或多个单字符前缀。">
          <UInputTags
            v-model="draft.commandPrefixes"
            data-testid="command-prefixes"
            class="w-full"
            :disabled="isSaving || isResetting"
          />
        </UFormField>
        <UFormField label="排除命令历史" name="excludeCommandsFromHistory">
          <UCheckbox
            v-model="draft.excludeCommandsFromHistory"
            label="命令消息不写入历史记录"
            :disabled="isSaving || isResetting"
          />
        </UFormField>
        <UFormField label="历史保留天数" name="historyRetentionDays" description="范围 0..3650；0 表示不自动清理。">
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

      <p v-if="feedbackMessage" role="status" class="text-sm text-error">
        {{ feedbackMessage }}
      </p>

      <div class="flex flex-wrap justify-end gap-2">
        <UButton
          data-testid="reset-chat-settings"
          type="button"
          color="neutral"
          variant="outline"
          label="恢复默认值"
          :loading="isResetting"
          :disabled="isSaving || isResetting"
          @click="emit('reset')"
        />
        <UButton
          type="submit"
          icon="i-lucide-save"
          label="保存聊天设置"
          :loading="isSaving"
          :disabled="isSaving || isResetting"
        />
      </div>
    </UForm>
  </section>
</template>
