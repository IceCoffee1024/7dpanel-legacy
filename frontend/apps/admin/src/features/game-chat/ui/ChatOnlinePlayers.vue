<script setup lang="ts">
import type { OnlinePlayer } from '../../players/api/onlinePlayers'

const props = withDefaults(defineProps<{
  players: readonly OnlinePlayer[]
  selectedTarget: OnlinePlayer | null
  emptyLabel?: string
  unavailableLabel?: string
}>(), {
  emptyLabel: 'No players are online.',
  unavailableLabel: 'Private messaging unavailable',
})

const emit = defineEmits<{
  select: [player: OnlinePlayer]
}>()

function canSelect(player: OnlinePlayer): boolean {
  return player.crossplatformIdentity?.combinedId.trim() !== ''
    && player.crossplatformIdentity?.combinedId !== undefined
}

function isSelected(player: OnlinePlayer): boolean {
  const selectedIdentity = props.selectedTarget?.crossplatformIdentity?.combinedId
  return selectedIdentity !== undefined
    && selectedIdentity === player.crossplatformIdentity?.combinedId
}
</script>

<template>
  <div class="min-h-0">
    <p v-if="players.length === 0" class="py-8 text-center text-sm text-muted">
      {{ emptyLabel }}
    </p>
    <ul v-else class="space-y-2">
      <li v-for="player in players" :key="`${player.entityId}:${player.platformIdentity.combinedId}`">
        <UButton
          block
          class="h-auto justify-start px-3 py-2 text-left"
          color="neutral"
          :data-testid="`chat-player-${player.entityId}`"
          :disabled="!canSelect(player)"
          :variant="isSelected(player) ? 'soft' : 'ghost'"
          @click="emit('select', player)"
        >
          <span class="min-w-0 flex-1">
            <span class="block truncate font-medium text-highlighted">{{ player.name }}</span>
            <span class="block font-mono text-xs text-muted">entity {{ player.entityId }}</span>
            <span v-if="player.crossplatformIdentity" class="block truncate font-mono text-xs text-dimmed">
              {{ player.crossplatformIdentity.combinedId }}
            </span>
            <span v-else class="block text-xs text-warning">{{ unavailableLabel }}</span>
          </span>
        </UButton>
      </li>
    </ul>
  </div>
</template>
