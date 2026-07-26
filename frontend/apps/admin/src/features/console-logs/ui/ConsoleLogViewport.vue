<script setup lang="ts">
import { nextTick, onMounted, shallowRef, useTemplateRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

interface ConsoleLogEntry {
  sequence: number
  formattedMessage?: string | null
  message?: string | null
  trace?: string | null
  logType?: string | null
}

const props = defineProps<{
  entries: readonly ConsoleLogEntry[]
  unreadCount: number
}>()

const emit = defineEmits<{
  updateFollowingLatest: [following: boolean]
}>()

const { t } = useI18n()
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
  const nextFollowing = isNearBottom(element)
  isFollowing.value = nextFollowing
  emit('updateFollowingLatest', nextFollowing)
}

function displayText(entry: ConsoleLogEntry): string {
  const primary = entry.formattedMessage?.trim()
    ? entry.formattedMessage
    : entry.message?.trim()
      ? entry.message
      : ''
  return entry.trace?.trim() ? `${primary}${primary === '' ? '' : '\n'}${entry.trace}` : primary
}

function logTypeClass(logType: string | null | undefined): string {
  const normalized = logType?.toLocaleLowerCase() ?? ''
  if (normalized.includes('error') || normalized.includes('exception'))
    return 'text-error'
  if (normalized.includes('warn'))
    return 'text-warning'
  if (normalized.includes('debug') || normalized.includes('trace'))
    return 'text-muted'
  return 'text-default'
}

watch(
  () => props.entries,
  async (nextEntries) => {
    if (nextEntries.length === 0) {
      await nextTick()
      scrollToLatest()
      return
    }

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
      class="console-log-viewport absolute inset-0 overflow-y-auto bg-default px-3 py-2 font-mono text-xs leading-5 sm:px-4 sm:text-sm"
      data-testid="console-log-viewport"
      role="log"
      aria-live="off"
      @scroll="handleScroll"
    >
      <pre
        v-for="entry in entries"
        :key="entry.sequence"
        class="console-log-entry m-0 whitespace-pre-wrap break-words font-inherit"
        :class="logTypeClass(entry.logType)"
        data-testid="console-log-entry"
      >{{ displayText(entry) }}</pre>
    </div>

    <UButton
      v-if="unreadCount > 0"
      class="absolute bottom-3 left-1/2 -translate-x-1/2 shadow-lg"
      color="neutral"
      data-testid="console-unread"
      icon="i-lucide-arrow-down"
      :label="t('console.viewport.backToLatest', { count: unreadCount })"
      size="sm"
      variant="solid"
      @click="scrollToLatest"
    />
  </div>
</template>

<style scoped>
.console-log-viewport {
  scrollbar-gutter: stable;
}

.console-log-entry {
  font-family: inherit;
}
</style>
