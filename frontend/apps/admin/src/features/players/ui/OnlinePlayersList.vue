<script setup lang="ts">
import type { OnlinePlayer } from '../api/onlinePlayers'

import { useI18n } from 'vue-i18n'
import { isOnlinePlayerObservationStale } from '../model/onlinePlayerFreshness'

withDefaults(defineProps<{
  players: readonly OnlinePlayer[]
  canKick?: boolean
}>(), {
  canKick: true,
})

const emit = defineEmits<{
  viewDetails: [player: OnlinePlayer]
  kickPlayer: [player: OnlinePlayer]
}>()
const { d, t } = useI18n()
</script>

<template>
  <ul class="divide-y divide-default md:hidden">
    <li v-for="player in players" :key="player.entityId" class="py-5 first:pt-0 last:pb-0">
      <div class="flex min-w-0 items-start justify-between gap-3">
        <div class="min-w-0">
          <h2 class="wrap-break-word text-sm font-semibold text-highlighted">
            {{ player.name }}
          </h2>
          <p class="mt-1 font-mono text-xs text-dimmed">
            entity {{ player.entityId }} · {{ player.ping }} ms
          </p>
          <p class="mt-1 text-xs text-muted">
            {{ t('players.fields.updatedAt', { time: d(new Date(player.observedAtUtc), 'playerObservation') }) }}
          </p>
          <UBadge
            v-if="isOnlinePlayerObservationStale(player.observedAtUtc)"
            class="mt-2"
            color="warning"
            variant="subtle"
          >
            {{ t('players.fields.stale') }}
          </UBadge>
        </div>
        <div class="flex shrink-0 items-center gap-1">
          <UBadge :color="player.isDead ? 'error' : 'success'" variant="subtle">
            {{ player.isDead ? t('players.fields.dead') : t('players.fields.alive') }}
          </UBadge>
          <UBadge color="neutral" variant="subtle">
            Lv. {{ player.level }}
          </UBadge>
          <UButton
            :aria-label="t('players.actions.viewDetails', { name: player.name })"
            class="size-8"
            color="neutral"
            icon="i-lucide-panel-right-open"
            square
            variant="ghost"
            @click="emit('viewDetails', player)"
          />
          <UButton
            v-if="canKick"
            :aria-label="t('players.actions.kickPlayer', { name: player.name })"
            class="size-8"
            color="error"
            icon="i-lucide-log-out"
            square
            variant="ghost"
            @click="emit('kickPlayer', player)"
          />
        </div>
      </div>

      <p class="mt-3 font-mono text-sm tabular-nums text-muted">
        {{ player.health }} / {{ player.maxHealth }} HP
      </p>
    </li>
  </ul>
</template>
