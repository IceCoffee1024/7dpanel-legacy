<script setup lang="ts">
import type { ConsoleCommandCatalogEntry } from '../api/consoleCommands'
import type { ConsoleCommandFeedbackCode } from '../model/useConsoleCommands'

import { useI18n } from 'vue-i18n'

import ConsoleCommandBar from './ConsoleCommandBar.vue'
import ConsoleLogViewport from './ConsoleLogViewport.vue'

interface ConsoleLogEntry {
  sequence: number
  formattedMessage?: string | null
  message?: string | null
  trace?: string | null
  logType?: string | null
}

defineProps<{
  entries: readonly ConsoleLogEntry[]
  snapshotLoading: boolean
  connectionStatus: 'connecting' | 'live' | 'reconnecting' | 'stopped'
  hasGap: boolean
  unreadCount: number
  commandInput: string
  commandCatalogUnavailable: boolean
  commandSuggestions: readonly ConsoleCommandCatalogEntry[]
  selectedSuggestionIndex: number
  suggestionsOpen: boolean
  isSubmitting: boolean
  commandFeedbackCode?: ConsoleCommandFeedbackCode | null
}>()

const emit = defineEmits<{
  clear: []
  updateCommandInput: [value: string]
  moveSuggestion: [direction: -1 | 1]
  selectSuggestion: [index: number]
  completeSuggestion: []
  dismissSuggestions: []
  navigateHistory: [direction: -1 | 1]
  submitCommand: []
  updateFollowingLatest: [following: boolean]
}>()

const { t } = useI18n()

function connectionColor(status: 'connecting' | 'live' | 'reconnecting' | 'stopped') {
  if (status === 'live')
    return 'success'
  if (status === 'stopped')
    return 'neutral'
  return 'warning'
}
</script>

<template>
  <UDashboardPanel id="console" class="h-full min-h-0">
    <template #header>
      <UDashboardNavbar :title="t('console.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
      </UDashboardNavbar>
      <div class="flex min-w-0 flex-wrap items-center gap-2 border-b border-default px-3 py-2 text-xs sm:px-4">
        <UBadge :color="connectionColor(connectionStatus)" variant="subtle">
          {{ t(`console.connection.${connectionStatus}`) }}
        </UBadge>
        <UBadge v-if="hasGap" color="warning" variant="subtle">
          {{ t('console.gap') }}
        </UBadge>
        <span class="text-muted">
          {{ snapshotLoading ? t('console.snapshotLoading') : t('console.buffered', { count: entries.length }) }}
        </span>
        <UButton
          class="ml-auto"
          color="neutral"
          :disabled="snapshotLoading || entries.length === 0"
          icon="i-lucide-eraser"
          :label="t('console.clear')"
          size="xs"
          variant="ghost"
          @click="emit('clear')"
        />
      </div>
    </template>

    <template #body>
      <div class="flex min-h-0 flex-1 flex-col overflow-hidden">
        <ConsoleLogViewport
          :entries="entries"
          :unread-count="unreadCount"
          @update-following-latest="emit('updateFollowingLatest', $event)"
        />
        <ConsoleCommandBar
          :catalog-unavailable="commandCatalogUnavailable"
          :feedback-code="commandFeedbackCode"
          :input="commandInput"
          :is-submitting="isSubmitting"
          :selected-suggestion-index="selectedSuggestionIndex"
          :suggestions="commandSuggestions"
          :suggestions-open="suggestionsOpen"
          @complete-suggestion="emit('completeSuggestion')"
          @dismiss-suggestions="emit('dismissSuggestions')"
          @move-suggestion="emit('moveSuggestion', $event)"
          @select-suggestion="emit('selectSuggestion', $event)"
          @navigate-history="emit('navigateHistory', $event)"
          @submit="emit('submitCommand')"
          @update-input="emit('updateCommandInput', $event)"
        />
      </div>
    </template>
  </UDashboardPanel>
</template>
