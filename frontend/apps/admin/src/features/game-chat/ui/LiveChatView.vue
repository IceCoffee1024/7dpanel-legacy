<script setup lang="ts">
import type { OnlinePlayer } from '../../players/api/onlinePlayers'
import type {
  ChatChannelFilter,
  ChatConnectionStatus,
  ChatMessage,
} from '../model/chatMessage'

import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

import { chatChannels } from '../model/chatMessage'
import ChatComposer from './ChatComposer.vue'
import ChatMessageViewport from './ChatMessageViewport.vue'
import ChatOnlinePlayers from './ChatOnlinePlayers.vue'

const props = defineProps<{
  messages: readonly ChatMessage[]
  channelFilter: ChatChannelFilter
  snapshotLoading: boolean
  connectionStatus: ChatConnectionStatus
  hasGap: boolean
  unreadCount: number
  draft: string
  selectedTarget: OnlinePlayer | null
  onlinePlayers: readonly OnlinePlayer[]
  isSubmitting: boolean
  sendError: string | null
  sendHistory: readonly string[]
}>()

const emit = defineEmits<{
  updateChannelFilter: [filter: ChatChannelFilter]
  updateDraft: [value: string]
  updateFollowingLatest: [following: boolean]
  selectTarget: [player: OnlinePlayer]
  clearTarget: []
  navigateHistory: [direction: -1 | 1]
  submit: []
}>()
const { t } = useI18n()

type SemanticColor = 'neutral' | 'success' | 'warning'

const filters: readonly ChatChannelFilter[] = ['All', ...chatChannels]
const mobilePlayersOpen = shallowRef(false)
const visibleMessages = computed(() => props.channelFilter === 'All'
  ? props.messages
  : props.messages.filter(message => message.channel === props.channelFilter))

function connectionColor(status: ChatConnectionStatus): SemanticColor {
  if (status === 'live')
    return 'success'
  if (status === 'stopped')
    return 'neutral'
  return 'warning'
}

function selectMobileTarget(player: OnlinePlayer) {
  emit('selectTarget', player)
  mobilePlayersOpen.value = false
}
</script>

<template>
  <UDashboardPanel id="live-chat" class="h-full min-h-0">
    <template #header>
      <UDashboardNavbar :title="t('gameChat.live.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            class="lg:hidden"
            color="neutral"
            data-testid="open-online-players"
            icon="i-lucide-users"
            :label="t('gameChat.live.onlinePlayers')"
            size="sm"
            variant="ghost"
            @click="mobilePlayersOpen = true"
          />
        </template>
      </UDashboardNavbar>

      <div class="space-y-2 border-b border-default px-3 py-2 sm:px-4">
        <div class="flex min-w-0 flex-wrap items-center gap-2 text-xs">
          <UBadge :color="connectionColor(connectionStatus)" variant="subtle">
            {{ t(`gameChat.connection.${connectionStatus}`) }}
          </UBadge>
          <span class="text-muted">
            {{ snapshotLoading ? t('gameChat.live.loadingRecent') : t('gameChat.live.buffered', { count: messages.length }) }}
          </span>
          <UBadge
            v-if="hasGap"
            color="warning"
            data-testid="chat-gap"
            variant="subtle"
          >
            {{ t('gameChat.live.gap') }}
          </UBadge>
        </div>
        <div :aria-label="t('gameChat.live.channelAria')" class="flex min-w-0 gap-1 overflow-x-auto" role="group">
          <UButton
            v-for="filter in filters"
            :key="filter"
            color="neutral"
            :data-testid="`chat-filter-${filter}`"
            :label="t(`gameChat.channels.${filter}`)"
            size="xs"
            :variant="channelFilter === filter ? 'soft' : 'ghost'"
            @click="emit('updateChannelFilter', filter)"
          />
        </div>
      </div>
    </template>

    <div class="flex min-h-0 flex-1 overflow-hidden">
      <section class="flex min-w-0 flex-1 flex-col overflow-hidden" :aria-label="t('gameChat.live.messagesAria')">
        <ChatMessageViewport
          :messages="visibleMessages"
          :unread-count="unreadCount"
          @update-following-latest="emit('updateFollowingLatest', $event)"
        />
        <ChatComposer
          :draft="draft"
          :is-submitting="isSubmitting"
          :selected-target="selectedTarget"
          :send-error="sendError"
          :send-history="sendHistory"
          @clear-target="emit('clearTarget')"
          @navigate-history="emit('navigateHistory', $event)"
          @submit="emit('submit')"
          @update-draft="emit('updateDraft', $event)"
        />
      </section>

      <aside class="hidden w-80 shrink-0 overflow-y-auto border-l border-default bg-default p-3 lg:block" :aria-label="t('gameChat.live.onlinePlayers')">
        <h2 class="mb-3 text-sm font-semibold text-highlighted">
          {{ t('gameChat.live.onlinePlayersCount', { count: onlinePlayers.length }) }}
        </h2>
        <ChatOnlinePlayers
          :players="onlinePlayers"
          :selected-target="selectedTarget"
          @select="emit('selectTarget', $event)"
        />
      </aside>
    </div>
  </UDashboardPanel>

  <USlideover
    v-model:open="mobilePlayersOpen"
    :title="t('gameChat.live.onlinePlayers')"
    :ui="{ content: 'w-full max-w-sm', body: 'overflow-y-auto' }"
  >
    <template #body>
      <ChatOnlinePlayers
        :players="onlinePlayers"
        :selected-target="selectedTarget"
        @select="selectMobileTarget"
      />
    </template>
  </USlideover>
</template>
