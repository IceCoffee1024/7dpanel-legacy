import type { ComputedRef, DeepReadonly, ShallowRef } from 'vue'
import type { ServerEventType } from '../../../app/serverEvents'
import type { ConsoleCommandCatalog, ConsoleCommandCatalogEntry } from '../api/consoleCommands'

import { useQuery, useQueryCache } from '@pinia/colada'
import { computed, onUnmounted, readonly, shallowRef, watch } from 'vue'

import { subscribeServerEvents } from '../../../app/serverEvents'
import {
  consoleCommandsGetCatalogQuery,
  consoleCommandsGetCatalogQueryKey,
} from '../../../shared/api/generated/@pinia/colada.gen'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import {
  executeConsoleCommand,
  fetchConsoleCommandCatalog,
  parseConsoleCommandCatalog,
} from '../api/consoleCommands'

export type ConsoleCommandFeedbackCode
  = | 'invalid'
    | 'session-expired'
    | 'forbidden'
    | 'unavailable'
    | 'result-unknown'
    | 'unknown'

export interface ConsoleCommandFeedback {
  code: ConsoleCommandFeedbackCode
}

export interface ConsoleCommandsController {
  input: DeepReadonly<ShallowRef<string>>
  suggestions: Readonly<ComputedRef<readonly ConsoleCommandCatalogEntry[]>>
  selectedSuggestionIndex: DeepReadonly<ShallowRef<number>>
  suggestionsOpen: Readonly<ComputedRef<boolean>>
  catalogUnavailable: Readonly<ComputedRef<boolean>>
  isSubmitting: DeepReadonly<ShallowRef<boolean>>
  feedback: DeepReadonly<ShallowRef<ConsoleCommandFeedback | null>>
  history: DeepReadonly<ShallowRef<readonly string[]>>
  setInput: (value: string) => void
  moveSuggestion: (direction: -1 | 1) => void
  selectSuggestion: (index: number) => void
  completeSuggestion: () => boolean
  dismissSuggestions: () => void
  navigateHistory: (direction: -1 | 1) => void
  submit: () => Promise<boolean>
  clearFeedback: () => void
  dispose: () => void
}

export interface UseConsoleCommandsOptions {
  auth?: {
    authorizationHeader: string | null
    expireSession: () => void
  }
  fetchCatalog?: (authorizationHeader: string, signal?: AbortSignal) => Promise<ConsoleCommandCatalog>
  executeCommand?: (command: string, signal?: AbortSignal) => Promise<unknown>
  invalidateCatalog?: (filter: { exact: true }) => Promise<unknown>
  subscribeServerEvents?: (listener: (event: { type: ServerEventType | string }) => void) => () => void
  onSessionExpired?: () => void
}

function firstCommandWord(value: string): string {
  return value.trimStart().split(/\s/, 1)[0] ?? ''
}

function matchesPrefix(command: ConsoleCommandCatalogEntry, prefix: string): boolean {
  const normalized = prefix.toLocaleLowerCase()
  return command.name.toLocaleLowerCase().startsWith(normalized)
    || command.aliases.some(alias => alias.toLocaleLowerCase().startsWith(normalized))
}

function replaceFirstWord(value: string, replacement: string): string {
  const wordStart = value.search(/\S/)
  if (wordStart === -1)
    return replacement
  const whitespaceOffset = value.slice(wordStart).search(/\s/)
  if (whitespaceOffset === -1)
    return `${value.slice(0, wordStart)}${replacement}`
  const wordEnd = wordStart + whitespaceOffset
  return `${value.slice(0, wordStart)}${replacement}${value.slice(wordEnd)}`
}

function feedbackFor(error: unknown): ConsoleCommandFeedback {
  if (!(error instanceof HttpError))
    return Object.freeze({ code: 'unknown' })
  if (error.status === 400)
    return Object.freeze({ code: 'invalid' })
  if (error.status === 401)
    return Object.freeze({ code: 'session-expired' })
  if (error.status === 403)
    return Object.freeze({ code: 'forbidden' })
  if (error.status === 503)
    return Object.freeze({ code: 'unavailable' })
  if (error.code === 'network' || error.code === 'timeout')
    return Object.freeze({ code: 'result-unknown' })
  return Object.freeze({ code: 'unknown' })
}

export function useConsoleCommands(options: UseConsoleCommandsOptions = {}): ConsoleCommandsController {
  const auth = options.auth ?? useAuthStore()
  const requestCatalog = options.fetchCatalog ?? fetchConsoleCommandCatalog
  const requestExecution = options.executeCommand ?? executeConsoleCommand
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const queryCache = useQueryCache()
  const generatedCatalogDefinition = options.fetchCatalog === undefined
    ? consoleCommandsGetCatalogQuery()
    : null
  const catalogQueryKey = generatedCatalogDefinition?.key ?? ['console-command-catalog'] as const
  const catalogQuery = useQuery<ConsoleCommandCatalog, Error>({
    key: catalogQueryKey,
    query: async (context) => {
      if (generatedCatalogDefinition !== null) {
        return parseConsoleCommandCatalog(await generatedCatalogDefinition.query(
          context as unknown as Parameters<typeof generatedCatalogDefinition.query>[0],
        ))
      }
      const authorizationHeader = auth.authorizationHeader
      if (authorizationHeader === null)
        throw new HttpError('http', 'Authentication required', { status: 401 })
      return requestCatalog(authorizationHeader, context.signal)
    },
    staleTime: 0,
    refetchOnWindowFocus: false,
  })
  const invalidateCatalog = options.invalidateCatalog
    ?? (filter => queryCache.invalidateQueries({
      key: generatedCatalogDefinition === null
        ? catalogQueryKey
        : consoleCommandsGetCatalogQueryKey(),
      exact: filter.exact,
    }))
  const input = shallowRef('')
  const selectedSuggestionIndex = shallowRef(0)
  const isSubmitting = shallowRef(false)
  const feedback = shallowRef<ConsoleCommandFeedback | null>(null)
  const history = shallowRef<readonly string[]>(Object.freeze([]))
  const dismissedInput = shallowRef<string | null>(null)
  const historyIndex = shallowRef(0)
  const historyDraft = shallowRef('')
  let controller: AbortController | null = null
  let inFlight: Promise<boolean> | null = null
  let disposed = false
  let sessionExpiryNotified = false

  const suggestions = computed<readonly ConsoleCommandCatalogEntry[]>(() => {
    const prefix = firstCommandWord(input.value)
    if (prefix === '' || catalogQuery.data.value == null)
      return Object.freeze([])
    return Object.freeze(catalogQuery.data.value.commands.filter(command => matchesPrefix(command, prefix)))
  })
  const suggestionsOpen = computed(() =>
    suggestions.value.length > 0 && dismissedInput.value !== input.value)
  const catalogUnavailable = computed(() => catalogQuery.status.value === 'error')

  watch(catalogQuery.error, (error) => {
    if (!(error instanceof HttpError) || error.status !== 401 || sessionExpiryNotified)
      return
    sessionExpiryNotified = true
    auth.expireSession()
    onSessionExpired()
  })

  function setInput(value: string) {
    input.value = value
    selectedSuggestionIndex.value = 0
    dismissedInput.value = null
    historyIndex.value = history.value.length
    historyDraft.value = value
  }

  function moveSuggestion(direction: -1 | 1) {
    const count = suggestions.value.length
    if (count === 0)
      return
    selectedSuggestionIndex.value = (selectedSuggestionIndex.value + direction + count) % count
  }

  function selectSuggestion(index: number) {
    if (Number.isInteger(index) && index >= 0 && index < suggestions.value.length)
      selectedSuggestionIndex.value = index
  }

  function completeSuggestion(): boolean {
    if (!suggestionsOpen.value)
      return false
    const suggestion = suggestions.value[selectedSuggestionIndex.value]
    if (suggestion === undefined)
      return false
    input.value = replaceFirstWord(input.value, suggestion.name)
    dismissedInput.value = input.value
    historyDraft.value = input.value
    return true
  }

  function dismissSuggestions() {
    dismissedInput.value = input.value
  }

  function navigateHistory(direction: -1 | 1) {
    if (history.value.length === 0)
      return
    if (historyIndex.value === history.value.length)
      historyDraft.value = input.value
    historyIndex.value = Math.min(
      history.value.length,
      Math.max(0, historyIndex.value + direction),
    )
    input.value = historyIndex.value === history.value.length
      ? historyDraft.value
      : history.value[historyIndex.value] ?? ''
    dismissedInput.value = input.value
  }

  function clearFeedback() {
    feedback.value = null
  }

  function submit(): Promise<boolean> {
    if (inFlight !== null)
      return inFlight
    const command = input.value
    if (disposed || command.trim() === '')
      return Promise.resolve(false)

    if (auth.authorizationHeader === null) {
      feedback.value = Object.freeze({ code: 'session-expired' })
      onSessionExpired()
      return Promise.resolve(false)
    }

    const currentController = new AbortController()
    controller = currentController
    isSubmitting.value = true
    feedback.value = null
    const request = requestExecution(command, currentController.signal)
      .then(() => {
        if (disposed || currentController.signal.aborted)
          return false
        if (history.value[history.value.length - 1] !== command)
          history.value = Object.freeze([...history.value, command].slice(-50))
        input.value = ''
        historyIndex.value = history.value.length
        historyDraft.value = ''
        dismissedInput.value = null
        return true
      })
      .catch((error: unknown) => {
        if (disposed || currentController.signal.aborted)
          return false
        const nextFeedback = feedbackFor(error)
        feedback.value = nextFeedback
        if (nextFeedback.code === 'session-expired') {
          sessionExpiryNotified = true
          auth.expireSession()
          onSessionExpired()
        }
        return false
      })
      .finally(() => {
        if (controller === currentController)
          controller = null
        if (inFlight === request)
          inFlight = null
        isSubmitting.value = false
      })
    inFlight = request
    return request
  }

  const unsubscribe = (
    options.subscribeServerEvents
    ?? (listener => subscribeServerEvents(listener as Parameters<typeof subscribeServerEvents>[0]))
  )((event) => {
    if (event.type === 'game-ready')
      void invalidateCatalog({ exact: true })
  })

  function dispose() {
    if (disposed)
      return
    disposed = true
    controller?.abort()
    controller = null
    unsubscribe()
  }

  onUnmounted(dispose)

  return {
    input: readonly(input),
    suggestions,
    selectedSuggestionIndex: readonly(selectedSuggestionIndex),
    suggestionsOpen,
    catalogUnavailable,
    isSubmitting: readonly(isSubmitting),
    feedback: readonly(feedback),
    history: readonly(history),
    setInput,
    moveSuggestion,
    selectSuggestion,
    completeSuggestion,
    dismissSuggestions,
    navigateHistory,
    submit,
    clearFeedback,
    dispose,
  }
}
