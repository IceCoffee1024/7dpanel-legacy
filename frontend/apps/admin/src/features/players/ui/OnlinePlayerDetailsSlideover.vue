<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import type { OnlinePlayer } from '../api/onlinePlayers'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import PlayerSnapshotDetails from './PlayerSnapshotDetails.vue'

const props = defineProps<{
  player: OnlinePlayer | null
  unavailable: boolean
  canKick: boolean
  canOpenProfile: boolean
  canRefresh?: boolean
  isRefreshing?: boolean
}>()
const emit = defineEmits<{
  copyValue: [value: string]
  kickPlayer: [player: OnlinePlayer]
  openProfile: [crossplatformId: string]
  refresh: []
}>()
const open = defineModel<boolean>('open', { required: true })
const { t } = useI18n()
const title = computed(() => props.player === null ? t('players.details.title') : `${props.player.name} · ${props.player.isDead ? t('players.fields.dead') : t('players.fields.alive')}`)
const stableId = computed(() => props.player?.crossplatformIdentity?.combinedId ?? null)
const historyTarget = computed<RouteLocationRaw | undefined>(() => stableId.value === null
  ? undefined
  : { name: '/players/history/[crossplatformId]', params: { crossplatformId: stableId.value } } as never)
const mapTarget = computed<RouteLocationRaw | undefined>(() => stableId.value === null
  ? undefined
  : { name: '/players/map', query: { player: stableId.value } } as never)
function copy(value: string | null) {
  if (value !== null)
    emit('copyValue', value)
}
</script>

<template>
  <USlideover
    v-model:open="open"
    :title="title"
    :description="player ? `entity ${player.entityId}` : undefined"
    :ui="{ content: 'w-full max-w-xl', body: 'overflow-y-auto' }"
  >
    <template #body>
      <div v-if="player" class="space-y-4">
        <UAlert
          v-if="unavailable"
          role="alert"
          color="warning"
          :title="t('players.details.unavailableTitle')"
          :description="t('players.details.unavailableDescription')"
        /><div class="flex flex-wrap gap-1">
          <UButton
            :aria-label="t('players.actions.copyPlatformIdentity')"
            color="neutral"
            icon="i-lucide-copy"
            size="xs"
            variant="ghost"
            @click="copy(player.platformIdentity.combinedId)"
          /><UButton
            v-if="player.crossplatformIdentity"
            :aria-label="t('players.actions.copyCrossplatformIdentity')"
            color="neutral"
            icon="i-lucide-copy"
            size="xs"
            variant="ghost"
            @click="copy(player.crossplatformIdentity.combinedId)"
          /><UButton
            v-if="player.discordUserId"
            :aria-label="t('players.actions.copyDiscordUserId')"
            color="neutral"
            icon="i-lucide-copy"
            size="xs"
            variant="ghost"
            @click="copy(player.discordUserId)"
          /><UButton
            v-if="player.ip"
            :aria-label="t('players.actions.copyIp')"
            color="neutral"
            icon="i-lucide-copy"
            size="xs"
            variant="ghost"
            @click="copy(player.ip)"
          />
        </div><PlayerSnapshotDetails :player="player" />
      </div>
    </template>
    <template #footer>
      <UButton
        v-if="canRefresh"
        color="neutral"
        :label="t('players.refresh')"
        icon="i-lucide-refresh-cw"
        :loading="isRefreshing"
        variant="soft"
        @click="emit('refresh')"
      />
      <UButton
        color="neutral"
        :label="t('common.cancel')"
        variant="outline"
        @click="open = false"
      /><UButton
        v-if="canOpenProfile && player?.crossplatformIdentity && !unavailable"
        color="neutral"
        :label="t('players.profile.navigation')"
        icon="i-lucide-contact-round"
        variant="soft"
        @click="emit('openProfile', player.crossplatformIdentity.combinedId)"
      /><UButton
        v-if="canOpenProfile && stableId && !unavailable"
        color="neutral"
        :label="t('players.actions.viewHistory')"
        icon="i-lucide-history"
        :to="historyTarget"
        variant="ghost"
      /><UButton
        v-if="canOpenProfile && stableId && !unavailable"
        color="neutral"
        :label="t('players.actions.viewMap')"
        icon="i-lucide-map"
        :to="mapTarget"
        variant="ghost"
      /><UButton
        v-if="canKick && player && !unavailable"
        color="error"
        :label="t('players.actions.kick')"
        icon="i-lucide-log-out"
        @click="emit('kickPlayer', player)"
      />
    </template>
  </USlideover>
</template>
