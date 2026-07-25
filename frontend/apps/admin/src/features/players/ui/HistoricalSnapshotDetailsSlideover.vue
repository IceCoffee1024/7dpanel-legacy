<script setup lang="ts">
import type { HistoricalPlayerSnapshot } from '../api/historyPlayers'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import PlayerSnapshotDetails from './PlayerSnapshotDetails.vue'

const props = defineProps<{ snapshot: HistoricalPlayerSnapshot | null }>()
const open = defineModel<boolean>('open', { required: true })
const { t } = useI18n()
const title = computed(() => props.snapshot === null ? t('players.history.snapshotDetails') : props.snapshot.player.name)
</script>

<template>
  <USlideover
    v-model:open="open"
    :title="title"
    :description="snapshot ? `snapshot ${snapshot.snapshotId}` : undefined"
    :ui="{ content: 'w-full max-w-xl', body: 'overflow-y-auto' }"
  >
    <template #body>
      <PlayerSnapshotDetails v-if="snapshot" :player="snapshot.player" />
    </template><template #footer>
      <UButton
        color="neutral"
        :label="t('common.cancel')"
        variant="outline"
        @click="open = false"
      />
    </template>
  </USlideover>
</template>
