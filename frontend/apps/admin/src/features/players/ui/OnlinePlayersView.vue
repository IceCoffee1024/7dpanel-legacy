<script setup lang="ts">
import type { OnlinePlayer } from '../api/onlinePlayers'

import { useToast } from '@nuxt/ui/composables'
import { computed, ref, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import { useKickPlayer } from '../model/useKickPlayer'
import { useOnlinePlayers } from '../model/useOnlinePlayers'

import KickPlayerDialog from './KickPlayerDialog.vue'
import OnlinePlayersList from './OnlinePlayersList.vue'
import OnlinePlayersState from './OnlinePlayersState.vue'
import OnlinePlayersTable from './OnlinePlayersTable.vue'
import OnlinePlayersToolbar from './OnlinePlayersToolbar.vue'

const router = useRouter()
const toast = useToast()
const { t } = useI18n()

function redirectToLogin() {
  return router.replace({
    path: '/login',
    query: { redirect: '/players' },
  })
}
const {
  state,
  snapshot,
  errorCode,
  isRefreshing,
  refresh,
} = useOnlinePlayers({
  onSessionExpired: redirectToLogin,
})
const {
  isSubmitting: isKickSubmitting,
  feedback: kickFeedback,
  submit: submitKick,
  clearFeedback: clearKickFeedback,
} = useKickPlayer({
  onSessionExpired: redirectToLogin,
})

const copyFeedback = ref<string | null>(null)
const selectedPlayer = shallowRef<OnlinePlayer | null>(null)
const kickDialogOpen = computed({
  get: () => selectedPlayer.value !== null,
  set: (open: boolean) => {
    if (!open && !isKickSubmitting.value)
      selectedPlayer.value = null
  },
})
const canKick = computed(() => kickFeedback.value?.code !== 'forbidden')

async function copyIdentity(combinedId: string) {
  try {
    if (!navigator.clipboard) {
      throw new Error('Clipboard API unavailable')
    }

    await navigator.clipboard.writeText(combinedId)
    copyFeedback.value = t('players.copy.success')
  }
  catch {
    copyFeedback.value = t('players.copy.failure')
  }
}

function openKickDialog(player: OnlinePlayer) {
  clearKickFeedback()
  selectedPlayer.value = player
}

function closeKickDialog() {
  if (isKickSubmitting.value)
    return
  selectedPlayer.value = null
  clearKickFeedback()
}

async function confirmKick(reason: string) {
  const target = selectedPlayer.value
  if (target === null)
    return

  const result = await submitKick(target, reason)
  if (result !== null) {
    toast.add({ title: t('players.kick.success', { name: target.name }), color: 'success' })
    selectedPlayer.value = null
    await refresh()
    return
  }

  if (kickFeedback.value?.code === 'player_not_online'
    || kickFeedback.value?.code === 'player_identity_changed') {
    selectedPlayer.value = null
    await refresh()
    return
  }

  if (kickFeedback.value?.code === 'forbidden')
    selectedPlayer.value = null
}
</script>

<template>
  <UDashboardPanel id="players">
    <template #header>
      <OnlinePlayersToolbar
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
        state="empty"
      />

      <template v-else>
        <OnlinePlayersTable
          :players="snapshot.players"
          :can-kick="canKick"
          @copy-identity="copyIdentity"
          @kick-player="openKickDialog"
        />
        <OnlinePlayersList
          :players="snapshot.players"
          :can-kick="canKick"
          @copy-identity="copyIdentity"
          @kick-player="openKickDialog"
        />
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

  <KickPlayerDialog
    v-model:open="kickDialogOpen"
    :player="selectedPlayer"
    :is-submitting="isKickSubmitting"
    :feedback="kickFeedback"
    @confirm="confirmKick"
    @cancel="closeKickDialog"
  />
</template>
