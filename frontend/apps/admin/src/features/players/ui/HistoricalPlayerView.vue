<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'

import { useHistoricalPlayer } from '../model/useHistoricalPlayer'
import HistoricalSnapshotDetailsSlideover from './HistoricalSnapshotDetailsSlideover.vue'
import HistoricalSnapshotTimeline from './HistoricalSnapshotTimeline.vue'
import PlayersSectionNavigation from './PlayersSectionNavigation.vue'

const route = useRoute()
const { d, t } = useI18n()
const routeParams = route.params as { crossplatformId?: string }
const crossplatformId = computed(() => {
  const value = routeParams.crossplatformId
  return typeof value === 'string' ? value : ''
})
const profilePath = computed(() => crossplatformId.value === ''
  ? null
  : `/players/profile/${encodeURIComponent(crossplatformId.value)}`)
const controller = useHistoricalPlayer({ crossplatformId })
const selectedSnapshot = shallowRef<typeof controller.snapshots.value[number] | null>(null)
const detailsOpen = computed({ get: () => selectedSnapshot.value !== null, set: (open) => {
  if (!open)
    selectedSnapshot.value = null
} })
function selectSnapshot(snapshot: typeof controller.snapshots.value[number]) {
  selectedSnapshot.value = snapshot
}
</script>

<template>
  <UDashboardPanel id="history-player">
    <template #header>
      <div class="space-y-3 p-3 sm:p-4">
        <PlayersSectionNavigation /><div class="flex items-center justify-between gap-3">
          <div>
            <h1 class="text-lg font-semibold text-highlighted">
              {{ controller.details.value?.player.latestName ?? t('players.history.title') }}
            </h1><p class="break-all text-xs text-muted">
              {{ crossplatformId }}
            </p>
          </div><div class="flex flex-wrap gap-2">
            <UButton
              v-if="profilePath"
              color="neutral"
              icon="i-lucide-contact-round"
              :label="t('players.profile.viewReadOnly')"
              :to="profilePath"
              variant="soft"
            /><UButton
              color="neutral"
              icon="i-lucide-refresh-cw"
              :label="t('common.reload')"
              :loading="controller.isRefreshing.value"
              variant="outline"
              @click="controller.refresh"
            />
          </div>
        </div>
      </div>
    </template>
    <template #body>
      <div class="space-y-4 p-3 sm:p-4">
        <div v-if="controller.state.value === 'loading'" class="space-y-3">
          <USkeleton class="h-20 w-full" /><USkeleton class="h-32 w-full" />
        </div>
        <UAlert
          v-else-if="controller.state.value === 'forbidden'"
          color="warning"
          :title="t('players.history.state.forbiddenTitle')"
          :description="t('players.history.state.forbiddenDescription')"
        />
        <UAlert v-else-if="controller.state.value === 'not-found'" color="warning" :title="t('players.history.state.notFoundTitle')" />
        <UAlert
          v-else-if="controller.state.value === 'failed'"
          color="error"
          :title="t('players.history.state.failedTitle')"
          :description="t('players.history.state.failedDescription')"
        />
        <template v-else-if="controller.details.value">
          <UAlert v-if="controller.state.value === 'stale'" color="warning" :title="t('players.history.state.staleTitle')" /><section class="grid gap-3 rounded-lg border border-default p-4 sm:grid-cols-3">
            <div><span class="text-sm text-muted">{{ t('players.history.firstObserved') }}</span><p>{{ d(new Date(controller.details.value.player.firstObservedAtUtc), 'playerObservation') }}</p></div><div><span class="text-sm text-muted">{{ t('players.history.lastObserved') }}</span><p>{{ d(new Date(controller.details.value.player.lastObservedAtUtc), 'playerObservation') }}</p></div><div><span class="text-sm text-muted">{{ t('players.history.gapCount') }}</span><p>{{ controller.details.value.gapSummary.gapCount }} · {{ controller.details.value.gapSummary.droppedObservationCount }}</p></div>
          </section><section>
            <h2 class="mb-3 font-semibold">
              {{ t('players.history.timeline') }}
            </h2><HistoricalSnapshotTimeline
              :snapshots="controller.snapshots.value"
              :gaps="controller.gaps.value"
              :can-load-more="controller.nextBeforeSnapshotId.value !== null"
              :is-loading-more="controller.isLoadingMore.value"
              @select-snapshot="selectSnapshot"
              @load-more="controller.loadMore"
            />
          </section>
        </template>
      </div>
    </template>
  </UDashboardPanel>
  <HistoricalSnapshotDetailsSlideover v-model:open="detailsOpen" :snapshot="selectedSnapshot" />
</template>
