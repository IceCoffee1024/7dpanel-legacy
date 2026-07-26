<script setup lang="ts">
import type { OnlineMapPlayer } from '../model/usePlayerMap'

import { useI18n } from 'vue-i18n'

const props = defineProps<{
  players: readonly OnlineMapPlayer[]
  selectedCombinedId: string | null
}>()

const emit = defineEmits<{
  select: [combinedId: string]
}>()

const { d, t } = useI18n()
</script>

<template>
  <section aria-labelledby="online-map-players-title" class="min-w-0 space-y-2">
    <h2 id="online-map-players-title" class="text-sm font-semibold">
      {{ t('players.map.onlinePlayers') }}
    </h2>
    <p v-if="props.players.length === 0" class="text-sm text-muted">
      {{ t('players.map.noOnlinePlayers') }}
    </p>
    <ul v-else class="max-h-64 space-y-1 overflow-y-auto">
      <li v-for="player in props.players" :key="player.combinedId">
        <button
          :aria-pressed="player.combinedId === props.selectedCombinedId"
          class="w-full rounded-md border px-3 py-2 text-left text-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
          :class="player.combinedId === props.selectedCombinedId ? 'border-primary bg-primary/10' : 'border-default bg-default'"
          :data-player-id="player.combinedId"
          type="button"
          @click="emit('select', player.combinedId)"
        >
          <span class="block font-medium">{{ player.name }}</span>
          <span class="block font-mono text-xs">X {{ player.position.x }} · Y {{ player.position.y }} · Z {{ player.position.z }}</span>
          <span class="block text-xs text-muted">{{ t('players.map.observedAt', { time: d(new Date(player.observedAtUtc), 'playerObservation') }) }}</span>
        </button>
      </li>
    </ul>
  </section>
</template>
