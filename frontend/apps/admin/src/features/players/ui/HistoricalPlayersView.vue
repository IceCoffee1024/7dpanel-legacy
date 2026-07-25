<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import { useHistoricalPlayers } from '../model/useHistoricalPlayers'
import PlayersSectionNavigation from './PlayersSectionNavigation.vue'

const { d, t } = useI18n()
const controller = useHistoricalPlayers()
const quality = computed(() => (player: typeof controller.players.value[number]) => {
  if (player.hasGaps)
    return t('players.history.quality.gaps')
  if (player.compactedSnapshotCount > 0)
    return t('players.history.quality.compacted')
  return t('players.history.quality.complete')
})
const errorTitle = computed(() => controller.state.value === 'forbidden'
  ? t('players.history.state.forbiddenTitle')
  : t('players.history.state.failedTitle'))
function refresh() {
  void controller.refresh()
}

function loadMore() {
  void controller.loadMore()
}
</script>

<template>
  <UDashboardPanel id="history-players">
    <template #header>
      <div class="space-y-3 p-3 sm:p-4">
        <PlayersSectionNavigation />
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div>
            <h1 class="text-lg font-semibold text-highlighted">
              {{ t('players.history.title') }}
            </h1><p class="text-sm text-muted">
              {{ t('players.history.description') }}
            </p>
          </div>
          <div class="flex gap-2">
            <UInput
              v-model="controller.search.value"
              data-testid="history-search"
              icon="i-lucide-search"
              :placeholder="t('players.history.searchPlaceholder')"
            /><UButton
              color="neutral"
              icon="i-lucide-refresh-cw"
              :loading="controller.isRefreshing.value"
              :label="t('common.reload')"
              variant="outline"
              @click="refresh"
            />
          </div>
        </div>
      </div>
    </template>
    <template #body>
      <div class="p-3 sm:p-4">
        <div v-if="controller.state.value === 'loading'" class="space-y-3" data-testid="history-loading">
          <USkeleton v-for="row in 5" :key="row" class="h-16 w-full" />
        </div>
        <UAlert
          v-else-if="controller.state.value === 'forbidden' || controller.state.value === 'failed'"
          :description="controller.state.value === 'forbidden' ? t('players.history.state.forbiddenDescription') : t('players.history.state.failedDescription')"
          :title="errorTitle"
          :color="controller.state.value === 'forbidden' ? 'warning' : 'error'"
        />
        <section v-else-if="controller.state.value === 'empty'" class="py-12 text-center">
          <h2 class="font-semibold">
            {{ t('players.history.state.emptyTitle') }}
          </h2>
        </section>
        <template v-else>
          <UAlert
            v-if="controller.state.value === 'stale'"
            class="mb-3"
            color="warning"
            :title="t('players.history.state.staleTitle')"
          />
          <ul class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            <li v-for="player in controller.players.value" :key="player.crossplatformId" class="min-w-0 rounded-lg border border-default p-4">
              <RouterLink :to="`/players/history/${encodeURIComponent(player.crossplatformId)}`" class="block space-y-2">
                <div class="flex items-start justify-between gap-2">
                  <strong class="min-w-0 truncate">{{ player.latestName }}</strong><UBadge color="neutral" variant="subtle">
                    {{ quality(player) }}
                  </UBadge>
                </div>
                <p class="break-all text-xs text-muted">
                  {{ player.crossplatformId }}
                </p>
                <dl class="grid grid-cols-2 gap-2 text-sm">
                  <div>
                    <dt class="text-muted">
                      {{ t('players.history.firstObserved') }}
                    </dt><dd>{{ d(new Date(player.firstObservedAtUtc), 'playerObservation') }}</dd>
                  </div><div>
                    <dt class="text-muted">
                      {{ t('players.history.lastObserved') }}
                    </dt><dd>{{ d(new Date(player.lastObservedAtUtc), 'playerObservation') }}</dd>
                  </div><div>
                    <dt class="text-muted">
                      {{ t('players.history.totalObservations') }}
                    </dt><dd>{{ player.totalObservationCount }}</dd>
                  </div><div>
                    <dt class="text-muted">
                      {{ t('players.history.retainedSnapshots') }}
                    </dt><dd>{{ player.retainedSnapshotCount }}</dd>
                  </div>
                </dl>
              </RouterLink>
            </li>
          </ul>
          <div v-if="controller.nextCursor.value" class="mt-4 flex justify-center">
            <UButton
              data-testid="history-load-more"
              color="neutral"
              icon="i-lucide-chevron-down"
              :label="t('players.history.loadMore')"
              :loading="controller.isLoadingMore.value"
              variant="outline"
              @click="loadMore"
            />
          </div>
        </template>
      </div>
    </template>
  </UDashboardPanel>
</template>
