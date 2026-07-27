<script setup lang="ts">
import type { PlayerProfileData } from './playerProfileUi'

import { useI18n } from 'vue-i18n'

import PlayerEvidenceBadge from './PlayerEvidenceBadge.vue'

defineProps<{
  profile: PlayerProfileData
}>()
const { d, t } = useI18n()
</script>

<template>
  <section class="space-y-3" aria-labelledby="player-profile-summary-title">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 id="player-profile-summary-title" class="font-semibold text-highlighted">
        {{ t('players.profile.summary.title') }}
      </h2>
      <PlayerEvidenceBadge :state="profile.summary.state" />
    </div>
    <UAlert
      v-if="profile.summary.gapMetadata.length"
      color="warning"
      :title="t('players.profile.evidence.gap')"
      :description="t('players.profile.evidence.gapDescription', { count: profile.summary.gapMetadata.length })"
    />
    <dl v-if="profile.summary.value" class="grid gap-3 rounded-lg border border-default p-4 sm:grid-cols-2 lg:grid-cols-4">
      <div>
        <dt class="text-sm text-muted">{{ t('players.fields.player') }}</dt>
        <dd class="font-medium">{{ profile.summary.value.latestName }}</dd>
      </div>
      <div>
        <dt class="text-sm text-muted">{{ t('players.fields.crossplatformIdentity') }}</dt>
        <dd class="break-all font-mono text-sm">{{ profile.crossplatformId }}</dd>
      </div>
      <div>
        <dt class="text-sm text-muted">{{ t('players.history.firstObserved') }}</dt>
        <dd>{{ d(new Date(profile.summary.value.firstObservedAtUtc), 'playerObservation') }}</dd>
      </div>
      <div>
        <dt class="text-sm text-muted">{{ t('players.history.lastObserved') }}</dt>
        <dd>{{ d(new Date(profile.summary.value.lastObservedAtUtc), 'playerObservation') }}</dd>
      </div>
    </dl>
    <UAlert v-else color="neutral" :title="t(`players.profile.section.${profile.summary.state.toLowerCase()}`)" />
  </section>
</template>
