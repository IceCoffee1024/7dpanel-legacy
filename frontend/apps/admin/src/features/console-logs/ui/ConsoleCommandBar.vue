<script setup lang="ts">
import type { ConsoleCommandCatalogEntry } from '../api/consoleCommands'
import type { ConsoleCommandFeedbackCode } from '../model/useConsoleCommands'

import { useToast } from '@nuxt/ui/composables'
import { watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  input: string
  suggestions: readonly ConsoleCommandCatalogEntry[]
  selectedSuggestionIndex: number
  suggestionsOpen: boolean
  catalogUnavailable: boolean
  isSubmitting: boolean
  feedbackCode?: ConsoleCommandFeedbackCode | null
}>()

const emit = defineEmits<{
  updateInput: [value: string]
  moveSuggestion: [direction: -1 | 1]
  selectSuggestion: [index: number]
  completeSuggestion: []
  dismissSuggestions: []
  navigateHistory: [direction: -1 | 1]
  submit: []
}>()

const { t } = useI18n()
const toast = useToast()

watch(() => props.feedbackCode, (code) => {
  if (code === null || code === undefined)
    return
  toast.add({
    title: t(`console.command.feedback.${code}`),
    color: 'error',
  })
})

function updateInput(value: string | number) {
  emit('updateInput', String(value))
}

function handleKeydown(event: KeyboardEvent) {
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    const direction = event.key === 'ArrowDown' ? 1 : -1
    if (props.suggestionsOpen)
      emit('moveSuggestion', direction)
    else
      emit('navigateHistory', direction)
    return
  }
  if (event.key === 'Tab' && props.suggestionsOpen) {
    event.preventDefault()
    emit('completeSuggestion')
    return
  }
  if (event.key === 'Escape' && props.suggestionsOpen) {
    event.preventDefault()
    emit('dismissSuggestions')
    return
  }
  if (event.key === 'Enter') {
    event.preventDefault()
    emit('submit')
  }
}
</script>

<template>
  <div class="relative shrink-0 border-t border-default bg-default p-2 sm:p-3">
    <div
      v-if="suggestionsOpen"
      class="absolute inset-x-2 bottom-full z-20 max-h-72 overflow-y-auto rounded-md border border-default bg-elevated shadow-xl sm:inset-x-3"
      data-testid="console-suggestions"
    >
      <button
        v-for="(suggestion, index) in suggestions"
        :key="suggestion.name"
        class="block w-full border-b border-muted px-3 py-2 text-left last:border-b-0"
        :class="index === selectedSuggestionIndex ? 'bg-accented' : 'hover:bg-muted'"
        type="button"
        @mousedown.prevent
        @click="emit('selectSuggestion', index); emit('completeSuggestion')"
      >
        <span class="flex flex-wrap items-center gap-2">
          <code class="font-semibold text-highlighted">{{ suggestion.name }}</code>
          <span v-if="suggestion.aliases.length" class="text-xs text-muted">
            {{ suggestion.aliases.join(', ') }}
          </span>
          <UBadge color="neutral" size="xs" variant="subtle">
            {{ suggestion.permissionLevel === null ? t('console.command.permissionUnknown') : t('console.command.permission', { level: suggestion.permissionLevel }) }}
          </UBadge>
        </span>
        <span v-if="suggestion.description" class="mt-1 block text-xs text-toned">
          {{ suggestion.description }}
        </span>
        <span v-if="suggestion.help" class="mt-1 block whitespace-pre-wrap text-xs text-muted">
          {{ suggestion.help }}
        </span>
      </button>
    </div>

    <p
      v-if="catalogUnavailable"
      class="mb-2 text-xs text-warning"
      data-testid="console-catalog-unavailable"
      role="status"
    >
      {{ t('console.command.catalogUnavailable') }}
    </p>

    <div class="flex min-w-0 gap-2">
      <UInput
        id="console-command"
        :aria-label="t('console.command.placeholder')"
        class="min-w-0 flex-1 font-mono"
        :disabled="isSubmitting"
        icon="i-lucide-terminal"
        :model-value="input"
        name="console-command"
        :placeholder="t('console.command.placeholder')"
        size="lg"
        @keydown="handleKeydown"
        @update:model-value="updateInput"
      />
      <UButton
        class="shrink-0"
        :disabled="isSubmitting || input.trim() === ''"
        icon="i-lucide-send-horizontal"
        :label="t('console.command.submit')"
        :loading="isSubmitting"
        size="lg"
        @click="emit('submit')"
      />
    </div>
  </div>
</template>
