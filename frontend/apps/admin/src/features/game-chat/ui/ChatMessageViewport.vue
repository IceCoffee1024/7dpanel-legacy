<script setup lang="ts">
import type { ChatChannel, ChatMessage, ChatSourceKind } from '../model/chatMessage'

import { nextTick, onMounted, shallowRef, useTemplateRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  messages: readonly ChatMessage[]
  unreadCount: number
}>()

const emit = defineEmits<{
  updateFollowingLatest: [following: boolean]
}>()
const { locale, t } = useI18n()

type SemanticColor = 'neutral' | 'info' | 'success' | 'warning' | 'error'

const viewport = useTemplateRef<HTMLElement>('viewport')
const isFollowing = shallowRef(true)
const bottomThreshold = 24

function isNearBottom(element: HTMLElement): boolean {
  return element.scrollHeight - element.clientHeight - element.scrollTop <= bottomThreshold
}

function scrollToLatest() {
  const element = viewport.value
  if (element !== null)
    element.scrollTop = element.scrollHeight
  isFollowing.value = true
  emit('updateFollowingLatest', true)
}

function handleScroll() {
  const element = viewport.value
  if (element === null)
    return
  const following = isNearBottom(element)
  isFollowing.value = following
  emit('updateFollowingLatest', following)
}

function formatTime(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return value
  return new Intl.DateTimeFormat(locale.value, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(date)
}

function channelColor(channel: ChatChannel): SemanticColor {
  switch (channel) {
    case 'Global': return 'info'
    case 'Friends': return 'success'
    case 'Party': return 'warning'
    case 'Whisper': return 'error'
    default: return 'neutral'
  }
}

function sourceColor(source: ChatSourceKind): SemanticColor {
  if (source === 'Administrator')
    return 'warning'
  if (source === 'System')
    return 'neutral'
  return 'info'
}

watch(
  () => props.messages,
  async () => {
    await nextTick()
    if (isFollowing.value)
      scrollToLatest()
  },
)

onMounted(() => {
  scrollToLatest()
})
</script>

<template>
  <div class="relative min-h-0 flex-1">
    <div
      ref="viewport"
      aria-live="off"
      class="chat-message-viewport absolute inset-0 overflow-y-auto bg-default px-3 py-3 sm:px-4"
      data-testid="chat-message-viewport"
      role="log"
      @scroll="handleScroll"
    >
      <p v-if="messages.length === 0" class="py-10 text-center text-sm text-muted">
        {{ t('gameChat.live.messagesEmpty') }}
      </p>

      <ol v-else class="space-y-3">
        <li
          v-for="message in messages"
          :key="message.sequence"
          class="rounded-lg border border-muted bg-elevated/60 px-3 py-2"
          :data-testid="`chat-message-${message.sequence}`"
        >
          <div class="flex min-w-0 flex-wrap items-center gap-2 text-xs">
            <UBadge :color="channelColor(message.channel)" size="xs" variant="subtle">
              {{ t(`gameChat.channels.${message.channel}`) }}
            </UBadge>
            <span class="font-semibold text-highlighted">{{ message.senderName }}</span>
            <UBadge :color="sourceColor(message.sourceKind)" size="xs" variant="outline">
              {{ t(`gameChat.sources.${message.sourceKind}`) }}
            </UBadge>
            <time class="ml-auto text-dimmed" :datetime="message.occurredAtUtc">
              {{ formatTime(message.occurredAtUtc) }}
            </time>
          </div>
          <p class="mt-1 whitespace-pre-wrap break-words text-sm text-default">
            {{ message.message }}
          </p>
        </li>
      </ol>
    </div>

    <UButton
      v-if="unreadCount > 0"
      class="absolute bottom-3 left-1/2 -translate-x-1/2 shadow-lg"
      color="neutral"
      data-testid="chat-unread"
      icon="i-lucide-arrow-down"
      :label="t('gameChat.live.backToLatest', { count: unreadCount })"
      size="sm"
      variant="solid"
      @click="scrollToLatest"
    />
  </div>
</template>

<style scoped>
.chat-message-viewport {
  scrollbar-gutter: stable;
}
</style>
