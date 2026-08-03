<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import { useAuthStore } from '../../../features/auth'
import {
  createRecentChatMessagesLoader,
  LiveChatView,
  useLiveChat,
  useSendChat,
} from '../../../features/game-chat'
import { useOnlinePlayers } from '../../../features/players'

const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()
const live = useLiveChat({
  loadRecent: createRecentChatMessagesLoader(() => auth.authorizationHeader),
})
const sender = useSendChat()
const players = useOnlinePlayers({
  onSessionExpired: () => router.replace({
    path: '/login',
    query: { redirect: '/community/chat/live' },
  }),
})

const onlinePlayers = computed(() => players.snapshot.value?.players ?? [])
const selectedTarget = computed(() => onlinePlayers.value.find(player =>
  player.crossplatformIdentity?.combinedId === sender.targetCrossplatformId.value,
) ?? null)
const sendError = computed(() => sender.error.value === null
  ? null
  : t(`gameChat.live.sendFeedback.${sender.error.value.code}`))
</script>

<template>
  <LiveChatView
    :messages="live.messages.value"
    :channel-filter="live.channelFilter.value"
    :snapshot-loading="live.snapshotLoading.value"
    :connection-status="live.connectionStatus.value"
    :has-gap="live.hasGap.value"
    :unread-count="live.unreadCount.value"
    :draft="sender.draft.value"
    :selected-target="selectedTarget"
    :online-players="onlinePlayers"
    :is-submitting="sender.isSubmitting.value"
    :send-error="sendError"
    :send-history="sender.sendHistory.value"
    @update-channel-filter="live.setChannelFilter"
    @update-draft="sender.setDraft"
    @update-following-latest="live.setFollowingLatest"
    @select-target="player => sender.setTarget(player.crossplatformIdentity!.combinedId)"
    @clear-target="sender.clearTarget"
    @navigate-history="sender.navigateHistory"
    @submit="sender.submit"
  />
</template>
