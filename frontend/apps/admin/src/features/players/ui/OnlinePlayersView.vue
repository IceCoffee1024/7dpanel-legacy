<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'

import { useOnlinePlayers } from '../model/useOnlinePlayers'

import OnlinePlayersList from './OnlinePlayersList.vue'
import OnlinePlayersState from './OnlinePlayersState.vue'
import OnlinePlayersTable from './OnlinePlayersTable.vue'
import OnlinePlayersToolbar from './OnlinePlayersToolbar.vue'

const router = useRouter()
const {
  state,
  snapshot,
  errorCode,
  isRefreshing,
  refresh,
} = useOnlinePlayers({
  onSessionExpired: () => router.replace({
    path: '/login',
    query: { redirect: '/players' },
  }),
})

const copyFeedback = ref<string | null>(null)

async function copyIdentity(combinedId: string) {
  try {
    if (!navigator.clipboard) {
      throw new Error('Clipboard API unavailable')
    }

    await navigator.clipboard.writeText(combinedId)
    copyFeedback.value = '身份已复制'
  }
  catch {
    copyFeedback.value = '复制失败，请手动选择身份标识'
  }
}
</script>

<template>
  <UDashboardPanel id="players">
    <template #header>
      <OnlinePlayersToolbar
        :captured-at-utc="snapshot?.capturedAtUtc"
        :count="snapshot?.players.length ?? 0"
        :is-refreshing="isRefreshing"
        :state="state"
        @refresh="refresh"
      />
    </template>

    <template #body>
      <OnlinePlayersState
        v-if="!snapshot"
        :error-code="errorCode"
        :state="state"
        @refresh="refresh"
      />

      <OnlinePlayersState
        v-else-if="snapshot.players.length === 0"
        :captured-at-utc="snapshot.capturedAtUtc"
        state="empty"
      />

      <template v-else>
        <OnlinePlayersTable :players="snapshot.players" @copy-identity="copyIdentity" />
        <OnlinePlayersList :players="snapshot.players" @copy-identity="copyIdentity" />
        <p
          v-if="copyFeedback"
          data-testid="copy-feedback"
          role="status"
          aria-live="polite"
        >
          {{ copyFeedback }}
        </p>
      </template>
    </template>
  </UDashboardPanel>
</template>
