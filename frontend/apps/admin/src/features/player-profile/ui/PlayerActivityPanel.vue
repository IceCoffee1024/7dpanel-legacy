<script setup lang="ts">
import type { PlayerProfileData } from './playerProfileUi'

import { useI18n } from 'vue-i18n'

import PlayerEvidenceBadge from './PlayerEvidenceBadge.vue'

defineProps<{
  sessions: PlayerProfileData['sessions']
  activity: PlayerProfileData['activity']
  dailyActivity: PlayerProfileData['dailyActivity']
}>()
const { d, t } = useI18n()
</script>

<template>
  <section class="space-y-4" aria-labelledby="player-activity-title">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 id="player-activity-title" class="font-semibold text-highlighted">{{ t('players.profile.activity.title') }}</h2>
      <div class="flex gap-2"><PlayerEvidenceBadge :state="activity.state" /><PlayerEvidenceBadge v-if="activity.gapMetadata.length" gap /></div>
    </div>
    <UAlert v-if="activity.state === 'Partial' || activity.gapMetadata.length" color="warning" :title="t('players.profile.evidence.gap')" :description="t('players.profile.evidence.incompleteDescription')" />
    <div class="grid gap-4 lg:grid-cols-2">
      <div>
        <h3 class="mb-2 text-sm font-semibold">{{ t('players.profile.activity.sessions') }}</h3>
        <ul v-if="sessions.value?.length" class="space-y-2"><li v-for="session in sessions.value" :key="session.sessionId" class="rounded-lg border border-default p-3 text-sm"><p>{{ session.worldId }}</p><p class="text-muted">{{ d(new Date(session.startedAtUtc), 'playerObservation') }} → {{ session.endedAtUtc ? d(new Date(session.endedAtUtc), 'playerObservation') : t('players.profile.activity.openSession') }}</p></li></ul>
        <p v-else class="text-sm text-muted">{{ t(`players.profile.section.${sessions.state.toLowerCase()}`) }}</p>
      </div>
      <div>
        <h3 class="mb-2 text-sm font-semibold">{{ t('players.profile.activity.events') }}</h3>
        <ul v-if="activity.value?.length" class="space-y-2"><li v-for="event in activity.value" :key="event.activityId" class="rounded-lg border border-default p-3 text-sm"><p>{{ event.kind }}</p><p class="text-muted">{{ event.worldId }} · {{ d(new Date(event.observedAtUtc), 'playerObservation') }}</p></li></ul>
        <p v-else class="text-sm text-muted">{{ t(`players.profile.section.${activity.state.toLowerCase()}`) }}</p>
      </div>
    </div>
    <div v-if="dailyActivity.value?.length" class="overflow-x-auto"><table class="w-full text-sm"><thead><tr class="border-b border-default text-left text-muted"><th class="p-2">{{ t('players.profile.activity.date') }}</th><th class="p-2">{{ t('players.profile.activity.sessions') }}</th><th class="p-2">{{ t('players.profile.activity.logins') }}</th><th class="p-2">{{ t('players.profile.activity.inventoryObservations') }}</th></tr></thead><tbody><tr v-for="day in dailyActivity.value" :key="day.localDate" class="border-b border-muted"><td class="p-2">{{ day.localDate }}</td><td class="p-2">{{ day.sessionCount ?? t('common.unknown') }}</td><td class="p-2">{{ day.loginCount ?? t('common.unknown') }}</td><td class="p-2">{{ day.inventoryObservationCount ?? t('common.unknown') }}</td></tr></tbody></table></div>
  </section>
</template>
