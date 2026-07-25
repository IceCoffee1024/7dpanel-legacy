<script setup lang="ts">
import type { GameOverview } from '../model/overview'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ game: GameOverview }>()
const { t } = useI18n()
const rows = computed(() => [
  ['serverTitle', props.game.gameTitle], ['save', props.game.saveGameName], ['worldName', props.game.worldName],
  ['version', props.game.version], ['mode', props.game.gameMode], ['difficulty', props.game.difficulty],
  ['region', props.game.region], ['language', props.game.language],
  ['address', props.game.connectionAddress === null ? null : `${props.game.connectionAddress}${props.game.connectionPort === null ? '' : `:${props.game.connectionPort}`}`],
  ['historicalPlayers', props.game.historicalPlayerCount],
] as const)
</script>

<template>
  <UCard class="rounded-md">
    <template #header><h2 class="font-semibold text-highlighted">{{ t('overview.serverInformation.title') }}</h2></template>
    <dl class="grid gap-x-6 gap-y-3 sm:grid-cols-2">
      <div v-for="row in rows" :key="row[0]" class="min-w-0"><dt class="text-xs text-muted">{{ t(`overview.serverInformation.${row[0]}`) }}</dt><dd class="mt-1 truncate text-sm text-highlighted">{{ row[1] ?? '—' }}</dd></div>
    </dl>
  </UCard>
</template>
