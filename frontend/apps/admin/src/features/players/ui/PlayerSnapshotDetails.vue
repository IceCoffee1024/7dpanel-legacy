<script setup lang="ts">
import type { PlayerIdentity, PlayerSnapshot } from '../api/playerSnapshot'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import { formatDeviceType, formatDurationMinutes, formatPosition, formatRoundedNumber } from '../model/onlinePlayerFormatting'

defineProps<{ player: PlayerSnapshot }>()
const { d, locale, t } = useI18n()
const displayLocale = computed(() => locale.value)

function optional(value: string | number | null): string | number {
  return value ?? t('players.history.unknown')
}
function identity(value: PlayerIdentity | null): string {
  return value === null ? t('players.history.unknown') : `${value.platform} · ${value.combinedId}`
}
</script>

<template>
  <div class="details-body">
    <section class="details-section">
      <h3 class="details-heading">
        {{ t('players.details.identity') }}
      </h3>
      <dl class="details-grid">
        <div><dt>{{ t('players.fields.player') }}</dt><dd>{{ player.name }}</dd></div>
        <div><dt>{{ t('players.fields.entityId') }}</dt><dd>{{ player.entityId }}</dd></div>
        <div class="wide">
          <dt>{{ t('players.fields.platformIdentity') }}</dt><dd>{{ identity(player.platformIdentity) }}</dd>
        </div>
        <div class="wide">
          <dt>{{ t('players.fields.crossplatformIdentity') }}</dt><dd>{{ identity(player.crossplatformIdentity) }}</dd>
        </div>
        <div><dt>{{ t('players.fields.discordUserId') }}</dt><dd>{{ optional(player.discordUserId) }}</dd></div>
        <div><dt>{{ t('players.fields.playGroup') }}</dt><dd>{{ optional(player.playGroup) }}</dd></div>
        <div class="wide">
          <dt>{{ t('players.fields.lastLoginUtc') }}</dt><dd>{{ player.lastLoginUtc === null ? t('players.history.unknown') : d(new Date(player.lastLoginUtc), 'playerObservation') }}</dd>
        </div>
      </dl>
    </section>
    <section class="details-section">
      <h3 class="details-heading">
        {{ t('players.details.connection') }}
      </h3>
      <dl class="details-grid">
        <div><dt>{{ t('players.fields.device') }}</dt><dd>{{ formatDeviceType(player.deviceType) }}</dd></div>
        <div><dt>{{ t('players.fields.ping') }}</dt><dd>{{ player.ping }} ms</dd></div>
        <div><dt>{{ t('players.fields.ip') }}</dt><dd>{{ optional(player.ip) }}</dd></div>
        <div><dt>{{ t('players.fields.permissionLevel') }}</dt><dd>{{ player.permissionLevel }}</dd></div>
        <div class="wide">
          <dt>{{ t('players.fields.compatibilityVersion') }}</dt><dd>{{ optional(player.compatibilityVersion) }}</dd>
        </div>
      </dl>
    </section>
    <section class="details-section">
      <h3 class="details-heading">
        {{ t('players.details.currentStatus') }}
      </h3>
      <dl class="details-grid">
        <div><dt>{{ t('players.fields.state') }}</dt><dd>{{ player.isDead ? t('players.fields.dead') : t('players.fields.alive') }}</dd></div>
        <div><dt>{{ t('players.fields.level') }}</dt><dd>{{ player.level }}</dd></div>
        <div><dt>{{ t('players.fields.health') }}</dt><dd>{{ player.health }}</dd></div>
        <div><dt>{{ t('players.fields.maxHealth') }}</dt><dd>{{ player.maxHealth }}</dd></div>
        <div class="wide">
          <dt>{{ t('players.fields.position') }}</dt><dd>{{ formatPosition(player.position, displayLocale) }}</dd>
        </div>
        <div class="wide">
          <dt>{{ t('players.fields.bedroll') }}</dt><dd>{{ player.bedroll === null ? t('players.history.unset') : formatPosition(player.bedroll, displayLocale) }}</dd>
        </div>
        <div class="wide">
          <dt>{{ t('players.fields.observedAt') }}</dt><dd>{{ d(new Date(player.observedAtUtc), 'playerObservation') }}</dd>
        </div>
      </dl>
    </section>
    <section class="details-section">
      <h3 class="details-heading">
        {{ t('players.details.progression') }}
      </h3>
      <dl class="details-grid">
        <div><dt>{{ t('players.fields.gameStage') }}</dt><dd>{{ optional(player.gameStage) }}</dd></div>
        <div><dt>{{ t('players.fields.expToNextLevel') }}</dt><dd>{{ player.expToNextLevel === null ? t('players.history.unknown') : formatRoundedNumber(player.expToNextLevel, displayLocale) }}</dd></div>
        <div><dt>{{ t('players.fields.skillPoints') }}</dt><dd>{{ optional(player.skillPoints) }}</dd></div>
      </dl>
    </section>
    <section class="details-section">
      <h3 class="details-heading">
        {{ t('players.details.statistics') }}
      </h3>
      <dl class="details-grid">
        <div><dt>{{ t('players.fields.score') }}</dt><dd>{{ formatRoundedNumber(player.score, displayLocale) }}</dd></div>
        <div><dt>{{ t('players.fields.zombieKills') }}</dt><dd>{{ formatRoundedNumber(player.zombieKills, displayLocale) }}</dd></div>
        <div><dt>{{ t('players.fields.playerKills') }}</dt><dd>{{ formatRoundedNumber(player.playerKills, displayLocale) }}</dd></div>
        <div><dt>{{ t('players.fields.deaths') }}</dt><dd>{{ formatRoundedNumber(player.deaths, displayLocale) }}</dd></div>
        <div><dt>{{ t('players.fields.totalTimePlayedMinutes') }}</dt><dd>{{ formatDurationMinutes(player.totalTimePlayedMinutes, displayLocale) }}</dd></div>
        <div><dt>{{ t('players.fields.distanceWalkedMeters') }}</dt><dd>{{ formatRoundedNumber(player.distanceWalkedMeters, displayLocale) }}</dd></div>
        <div><dt>{{ t('players.fields.totalItemsCrafted') }}</dt><dd>{{ formatRoundedNumber(player.totalItemsCrafted, displayLocale) }}</dd></div>
        <div><dt>{{ t('players.fields.longestLifeMinutes') }}</dt><dd>{{ formatDurationMinutes(player.longestLifeMinutes, displayLocale) }}</dd></div>
        <div><dt>{{ t('players.fields.currentLifeMinutes') }}</dt><dd>{{ formatDurationMinutes(player.currentLifeMinutes, displayLocale) }}</dd></div>
      </dl>
    </section>
  </div>
</template>

<style scoped>
.details-body {
  display: grid;
  gap: 1.25rem;
  min-width: 0;
}
.details-section {
  border-top: 1px solid var(--ui-border);
  padding-top: 1rem;
}
.details-section:first-child {
  border-top: 0;
  padding-top: 0;
}
.details-heading {
  color: var(--ui-text-highlighted);
  font-size: 0.875rem;
  font-weight: 600;
  margin: 0;
}
.details-grid {
  display: grid;
  gap: 1rem;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin-top: 1rem;
}
.details-grid div {
  min-width: 0;
}
.details-grid .wide {
  grid-column: 1 / -1;
}
dt {
  color: var(--ui-text-dimmed);
  font-size: 0.75rem;
}
dd {
  margin: 0.25rem 0 0;
  overflow-wrap: anywhere;
}
@media (max-width: 359px) {
  .details-grid {
    grid-template-columns: minmax(0, 1fr);
  }
  .details-grid .wide {
    grid-column: auto;
  }
}
</style>
