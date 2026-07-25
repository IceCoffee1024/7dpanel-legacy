<script setup lang="ts">
import type { HistoricalPlayerSnapshot, PlayerHistoryGap } from '../api/historyPlayers'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { formatPosition } from '../model/onlinePlayerFormatting'

const props = defineProps<{ snapshots: readonly HistoricalPlayerSnapshot[], gaps: readonly PlayerHistoryGap[], canLoadMore: boolean, isLoadingMore: boolean }>()
const emit = defineEmits<{ selectSnapshot: [snapshot: HistoricalPlayerSnapshot], loadMore: [] }>()
const { d, locale, t } = useI18n()
const entries = computed(() => [...props.snapshots.map(snapshot => ({ kind: 'snapshot' as const, at: snapshot.player.observedAtUtc, value: snapshot })), ...props.gaps.map(gap => ({ kind: 'gap' as const, at: gap.completedAtUtc, value: gap }))].sort((a, b) => b.at.localeCompare(a.at)))
function gapReason(reason: PlayerHistoryGap['reason']) {
  return t(`players.history.gap.${reason}`)
}
</script>

<template>
  <ol class="space-y-3">
    <li v-for="entry in entries" :key="entry.kind === 'snapshot' ? entry.value.snapshotId : entry.value.gapId" class="rounded-lg border border-default p-3">
      <template v-if="entry.kind === 'snapshot'">
        <UButton
          block
          color="neutral"
          variant="ghost"
          class="justify-start text-left"
          @click="emit('selectSnapshot', entry.value)"
        >
          <span><strong>{{ entry.value.player.name }}</strong> · {{ d(new Date(entry.value.player.observedAtUtc), 'playerObservation') }}<br><span class="text-sm text-muted">entity {{ entry.value.player.entityId }} · {{ t('players.fields.level') }} {{ entry.value.player.level }} · {{ entry.value.player.health }}/{{ entry.value.player.maxHealth }} · {{ entry.value.player.ping }} ms · {{ formatPosition(entry.value.player.position, locale) }}</span></span>
        </UButton>
      </template>
      <template v-else>
        <UAlert
          color="warning"
          icon="i-lucide-triangle-alert"
          :title="gapReason(entry.value.reason)"
          :description="t('players.history.gap.description', { count: entry.value.droppedCount })"
        />
      </template>
    </li>
  </ol>
  <div v-if="canLoadMore" class="mt-4 flex justify-center">
    <UButton
      color="neutral"
      :label="t('players.history.loadMore')"
      :loading="isLoadingMore"
      variant="outline"
      @click="emit('loadMore')"
    />
  </div>
</template>
