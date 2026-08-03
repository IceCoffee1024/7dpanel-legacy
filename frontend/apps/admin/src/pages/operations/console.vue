<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner", "Admin"]
  }
}
</route>

<script setup lang="ts">
import { useRouter } from 'vue-router'

import { useAuthStore } from '../../features/auth'
import { ConsoleWorkspace, useConsoleCommands, useConsoleLogs } from '../../features/console-logs'
import { createRecentConsoleLogsLoader } from '../../features/console-logs/api/consoleLogs'
import { consoleLogsGetRecent } from '../../shared/api/generated/sdk.gen'
import { HttpError } from '../../shared/api/http'

const router = useRouter()
const auth = useAuthStore()
const loadRecent = createRecentConsoleLogsLoader(async ({ query, signal }) => {
  if (auth.authorizationHeader === null)
    throw new HttpError('http', 'Authentication required', { status: 401 })
  try {
    return await consoleLogsGetRecent({
      query,
      signal,
    })
  }
  catch (error) {
    if (error instanceof HttpError && error.status === 401) {
      auth.expireSession()
      await router.replace({ path: '/login', query: { redirect: '/operations/console' } })
    }
    throw error
  }
})
const logs = useConsoleLogs({ loadRecent })
const commands = useConsoleCommands({
  onSessionExpired: () => router.replace({
    path: '/login',
    query: { redirect: '/operations/console' },
  }),
})
</script>

<template>
  <ConsoleWorkspace
    :command-catalog-unavailable="commands.catalogUnavailable.value"
    :command-feedback-code="commands.feedback.value?.code ?? null"
    :command-input="commands.input.value"
    :command-suggestions="commands.suggestions.value"
    :connection-status="logs.connectionStatus.value"
    :entries="logs.entries.value"
    :has-gap="logs.hasGap.value"
    :is-submitting="commands.isSubmitting.value"
    :selected-suggestion-index="commands.selectedSuggestionIndex.value"
    :snapshot-loading="logs.snapshotLoading.value"
    :suggestions-open="commands.suggestionsOpen.value"
    :unread-count="logs.unreadCount.value"
    @clear="logs.clearEntries"
    @complete-suggestion="commands.completeSuggestion"
    @dismiss-suggestions="commands.dismissSuggestions"
    @move-suggestion="commands.moveSuggestion"
    @navigate-history="commands.navigateHistory"
    @select-suggestion="commands.selectSuggestion"
    @submit-command="commands.submit"
    @update-command-input="commands.setInput"
    @update-following-latest="logs.setFollowingLatest"
  />
</template>
