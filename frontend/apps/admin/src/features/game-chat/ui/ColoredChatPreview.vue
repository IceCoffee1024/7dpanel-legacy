<script setup lang="ts">
import type { ColoredChatPreviewContext } from '../model/gameChatManagement'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import { renderColoredChatName, toChatColorStyle } from '../model/gameChatManagement'

const props = withDefaults(defineProps<{
  customName?: string | null
  nameColor?: string | null
  textColor?: string | null
  message?: string
  context?: ColoredChatPreviewContext
}>(), {
  customName: null,
  nameColor: null,
  textColor: null,
})

const { t } = useI18n()
const previewContext = computed<ColoredChatPreviewContext>(() => props.context ?? ({
  playerName: t('gameChat.colored.preview.playerName'),
  playerId: 'EOS_example',
  entityId: 42,
  chatType: 'Global',
}))
const renderedName = computed(() => renderColoredChatName(props.customName, previewContext.value))
const renderedMessage = computed(() => props.message ?? t('gameChat.colored.preview.message'))
const nameStyle = computed(() => ({ color: toChatColorStyle(props.nameColor) }))
const messageStyle = computed(() => ({ color: toChatColorStyle(props.textColor) }))
</script>

<template>
  <figure class="rounded-lg border border-default bg-elevated p-4" :aria-label="t('gameChat.colored.preview.aria')">
    <figcaption class="mb-3 text-xs font-medium uppercase tracking-wide text-muted">
      {{ t('gameChat.colored.preview.title') }}
    </figcaption>
    <p class="whitespace-pre-wrap wrap-break-word text-sm">
      <strong data-testid="preview-name" :style="nameStyle">{{ renderedName }}</strong><span class="text-muted">：</span>
      <span data-testid="preview-message" :style="messageStyle">{{ renderedMessage }}</span>
    </p>
  </figure>
</template>
