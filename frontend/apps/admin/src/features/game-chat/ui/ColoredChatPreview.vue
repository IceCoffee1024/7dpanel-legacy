<script setup lang="ts">
import type { ColoredChatPreviewContext } from '../model/gameChatManagement'

import { computed } from 'vue'

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
  message: '这是一条纯文本聊天预览。',
  context: () => ({
    playerName: 'Player',
    playerId: 'EOS_example',
    entityId: 42,
    chatType: 'Global',
  }),
})

const renderedName = computed(() => renderColoredChatName(props.customName, props.context))
const nameStyle = computed(() => ({ color: toChatColorStyle(props.nameColor) }))
const messageStyle = computed(() => ({ color: toChatColorStyle(props.textColor) }))
</script>

<template>
  <figure class="rounded-lg border border-default bg-elevated p-4" aria-label="彩色聊天纯文本预览">
    <figcaption class="mb-3 text-xs font-medium uppercase tracking-wide text-muted">
      安全预览（纯文本）
    </figcaption>
    <p class="whitespace-pre-wrap wrap-break-word text-sm">
      <strong data-testid="preview-name" :style="nameStyle">{{ renderedName }}</strong><span class="text-muted">：</span>
      <span data-testid="preview-message" :style="messageStyle">{{ message }}</span>
    </p>
  </figure>
</template>
