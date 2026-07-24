<script setup lang="ts">
import type { OnlinePlayer } from '../api/onlinePlayers'

import { useToast } from '@nuxt/ui/composables'
import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import { useKickPlayer } from '../model/useKickPlayer'
import { useOnlinePlayers } from '../model/useOnlinePlayers'

import KickPlayerDialog from './KickPlayerDialog.vue'
import OnlinePlayerDetailsSlideover from './OnlinePlayerDetailsSlideover.vue'
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
const sessionExpired = shallowRef(false)

function handleSessionExpired() {
  sessionExpired.value = true
  void redirectToLogin()
}

const {
  state,
  snapshot,
  errorCode,
  isRefreshing,
  refresh,
} = useOnlinePlayers({
  onSessionExpired: handleSessionExpired,
})
const {
  isSubmitting: isKickSubmitting,
  feedback: kickFeedback,
  submit: submitKick,
  clearFeedback: clearKickFeedback,
} = useKickPlayer({
  onSessionExpired: handleSessionExpired,
})

interface SelectedPlayerKey {
  entityId: number
  combinedId: string
}

const copyFeedback = shallowRef<string | null>(null)
const selectedPlayer = shallowRef<OnlinePlayer | null>(null)
const detailsKey = shallowRef<SelectedPlayerKey | null>(null)
const detailsPlayer = shallowRef<OnlinePlayer | null>(null)
const detailsUnavailable = shallowRef(false)
const kickDialogOpen = computed({
  get: () => selectedPlayer.value !== null,
  set: (open: boolean) => {
    if (!open && !isKickSubmitting.value)
      selectedPlayer.value = null
  },
})
const authorizedToKick = computed(() =>
  !sessionExpired.value && kickFeedback.value?.code !== 'forbidden')
const detailsCanKick = computed(() =>
  authorizedToKick.value
  && state.value === 'fresh'
  && !sessionExpired.value
  && !detailsUnavailable.value)
const detailsOpen = computed({
  get: () => detailsPlayer.value !== null,
  set: (open: boolean) => {
    if (!open)
      closeDetails()
  },
})

async function copyValue(value: string) {
  try {
    if (!navigator.clipboard) {
      throw new Error('Clipboard API unavailable')
    }

    await navigator.clipboard.writeText(value)
    copyFeedback.value = t('players.copy.success')
  }
  catch {
    copyFeedback.value = t('players.copy.failure')
  }
}

function openDetails(player: OnlinePlayer) {
  detailsKey.value = {
    entityId: player.entityId,
    combinedId: player.platformIdentity.combinedId,
  }
  detailsPlayer.value = player
  detailsUnavailable.value = false
}

function closeDetails() {
  detailsKey.value = null
  detailsPlayer.value = null
  detailsUnavailable.value = false
}

function openDetailsKickDialog() {
  if (detailsPlayer.value !== null && detailsCanKick.value)
    openKickDialog(detailsPlayer.value)
}

watch([state, snapshot], ([nextState, nextSnapshot]) => {
  if (nextState !== 'fresh' || nextSnapshot === null || detailsUnavailable.value)
    return
  const target = detailsKey.value
  if (target === null || detailsPlayer.value === null)
    return

  const matchingPlayer = nextSnapshot.players.find(player =>
    player.entityId === target.entityId
    && player.platformIdentity.combinedId === target.combinedId)
  if (matchingPlayer === undefined) {
    detailsUnavailable.value = true
    return
  }
  detailsPlayer.value = matchingPlayer
})

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
          :can-kick="authorizedToKick"
          @view-details="openDetails"
          @kick-player="openKickDialog"
        />
        <OnlinePlayersList
          :players="snapshot.players"
          :can-kick="authorizedToKick"
          @view-details="openDetails"
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

  <OnlinePlayerDetailsSlideover
    v-model:open="detailsOpen"
    :player="detailsPlayer"
    :unavailable="detailsUnavailable"
    :can-kick="detailsCanKick"
    @copy-value="copyValue"
    @kick-player="openDetailsKickDialog"
  />
</template>
