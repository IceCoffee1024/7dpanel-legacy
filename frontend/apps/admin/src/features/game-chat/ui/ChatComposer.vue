<script setup lang="ts">
import type { OnlinePlayer } from '../../players/api/onlinePlayers'

import { useI18n } from 'vue-i18n'

const props = withDefaults(defineProps<{
  draft: string
  selectedTarget: OnlinePlayer | null
  isSubmitting: boolean
  sendError?: string | null
  sendHistory: readonly string[]
}>(), {
  sendError: null,
})

const emit = defineEmits<{
  updateDraft: [value: string]
  clearTarget: []
  navigateHistory: [direction: -1 | 1]
  submit: []
}>()
const { t } = useI18n()

function updateDraft(value: string | number) {
  emit('updateDraft', String(value))
}

function handleKeydown(event: KeyboardEvent) {
  if ((event.key === 'ArrowUp' || event.key === 'ArrowDown') && props.sendHistory.length > 0) {
    event.preventDefault()
    emit('navigateHistory', event.key === 'ArrowUp' ? -1 : 1)
    return
  }

  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    if (!props.isSubmitting && props.draft.trim() !== '')
      emit('submit')
  }
}
</script>

<template>
  <div class="shrink-0 border-t border-default bg-default p-3">
    <div class="mb-2 flex min-w-0 items-center gap-2 text-xs">
      <UBadge :color="selectedTarget ? 'warning' : 'info'" variant="subtle">
        {{ selectedTarget ? t('gameChat.live.composer.private') : t('gameChat.live.composer.global') }}
      </UBadge>
      <span v-if="selectedTarget" class="min-w-0 truncate font-medium text-highlighted">
        {{ selectedTarget.name }} · {{ selectedTarget.crossplatformIdentity?.combinedId }}
      </span>
      <UButton
        v-if="selectedTarget"
        class="ml-auto"
        color="neutral"
        data-testid="clear-private-target"
        icon="i-lucide-x"
        :label="t('gameChat.live.composer.clearTarget')"
        size="xs"
        variant="ghost"
        @click="emit('clearTarget')"
      />
    </div>

    <p
      v-if="sendError"
      class="mb-2 text-sm text-error"
      data-testid="chat-send-error"
      role="alert"
    >
      {{ sendError }}
    </p>

    <div class="flex min-w-0 items-end gap-2">
      <UTextarea
        :aria-label="t('gameChat.live.composer.messageAria')"
        id="live-chat-message"
        autoresize
        class="min-w-0 flex-1"
        data-testid="chat-composer-input"
        :disabled="isSubmitting"
        :maxrows="6"
        :model-value="draft"
        name="live-chat-message"
        :placeholder="t('gameChat.live.composer.placeholder')"
        :rows="2"
        @keydown="handleKeydown"
        @update:model-value="updateDraft"
      />
      <UButton
        class="shrink-0"
        :disabled="isSubmitting || draft.trim() === ''"
        icon="i-lucide-send-horizontal"
        :label="t('gameChat.live.composer.send')"
        :loading="isSubmitting"
        size="lg"
        @click="emit('submit')"
      />
    </div>
    <p class="mt-1 text-xs text-dimmed">
      {{ t('gameChat.live.composer.keyboardHelp') }}
    </p>
  </div>
</template>
