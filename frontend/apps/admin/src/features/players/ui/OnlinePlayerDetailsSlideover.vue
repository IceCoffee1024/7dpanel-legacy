<script setup lang="ts">
import type { OnlinePlayer, PlayerIdentity } from '../api/onlinePlayers'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import {
  formatDeviceType,
  formatDurationMinutes,
  formatNullable,
  formatPosition,
  formatRoundedNumber,
} from '../model/onlinePlayerFormatting'

const props = defineProps<{
  player: OnlinePlayer | null
  unavailable: boolean
  canKick: boolean
}>()

const emit = defineEmits<{
  copyValue: [value: string]
  kickPlayer: [player: OnlinePlayer]
}>()
const open = defineModel<boolean>('open', { required: true })
const { d, locale, t } = useI18n()
const displayLocale = computed(() => locale.value)
const playerTitle = computed(() => props.player === null
  ? t('players.details.title')
  : `${props.player.name} · ${props.player.isDead ? t('players.fields.dead') : t('players.fields.alive')}`)

function formatIdentity(identity: PlayerIdentity | null): string {
  return identity === null
    ? formatNullable(null)
    : `${identity.platform} · ${identity.combinedId}`
}

function copy(value: string | null) {
  if (value !== null)
    emit('copyValue', value)
}

function kick() {
  if (props.player !== null && props.canKick && !props.unavailable)
    emit('kickPlayer', props.player)
}
</script>

<template>
  <USlideover
    v-model:open="open"
    :title="playerTitle"
    :description="player ? `entity ${player.entityId}` : undefined"
    :ui="{ content: 'w-full max-w-xl', body: 'overflow-y-auto' }"
  >
    <template #body>
      <div v-if="player" class="details-body">
        <UAlert
          v-if="unavailable"
          color="warning"
          icon="i-lucide-triangle-alert"
          :title="t('players.details.unavailableTitle')"
          :description="t('players.details.unavailableDescription')"
        />

        <section class="details-section" :aria-labelledby="`identity-${player.entityId}`">
          <h3 :id="`identity-${player.entityId}`" class="details-heading">
            {{ t('players.details.identity') }}
          </h3>
          <dl class="details-grid">
            <div class="details-item">
              <dt>{{ t('players.fields.player') }}</dt><dd>{{ player.name }}</dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.entityId') }}</dt><dd class="tabular-value">
                {{ player.entityId }}
              </dd>
            </div>
            <div class="details-item details-item--wide">
              <dt>{{ t('players.fields.platformIdentity') }}</dt>
              <dd class="details-value-with-copy">
                <span>{{ formatIdentity(player.platformIdentity) }}</span><UButton
                  :aria-label="t('players.actions.copyPlatformIdentity')"
                  color="neutral"
                  icon="i-lucide-copy"
                  size="xs"
                  square
                  variant="ghost"
                  @click="copy(player.platformIdentity.combinedId)"
                />
              </dd>
            </div>
            <div class="details-item details-item--wide">
              <dt>{{ t('players.fields.crossplatformIdentity') }}</dt>
              <dd class="details-value-with-copy">
                <span>{{ formatIdentity(player.crossplatformIdentity) }}</span><UButton
                  v-if="player.crossplatformIdentity"
                  :aria-label="t('players.actions.copyCrossplatformIdentity')"
                  color="neutral"
                  icon="i-lucide-copy"
                  size="xs"
                  square
                  variant="ghost"
                  @click="copy(player.crossplatformIdentity.combinedId)"
                />
              </dd>
            </div>
            <div class="details-item details-item--wide">
              <dt>{{ t('players.fields.discordUserId') }}</dt>
              <dd class="details-value-with-copy">
                <span>{{ formatNullable(player.discordUserId) }}</span><UButton
                  v-if="player.discordUserId"
                  :aria-label="t('players.actions.copyDiscordUserId')"
                  color="neutral"
                  icon="i-lucide-copy"
                  size="xs"
                  square
                  variant="ghost"
                  @click="copy(player.discordUserId)"
                />
              </dd>
            </div>
          </dl>
        </section>

        <section class="details-section" :aria-labelledby="`connection-${player.entityId}`">
          <h3 :id="`connection-${player.entityId}`" class="details-heading">
            {{ t('players.details.connection') }}
          </h3>
          <dl class="details-grid">
            <div class="details-item">
              <dt>{{ t('players.fields.device') }}</dt><dd>{{ formatDeviceType(player.deviceType) }}</dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.ping') }}</dt><dd class="tabular-value">
                {{ player.ping }} ms
              </dd>
            </div>
            <div class="details-item details-item--wide">
              <dt>{{ t('players.fields.ip') }}</dt><dd class="details-value-with-copy">
                <span>{{ formatNullable(player.ip) }}</span><UButton
                  v-if="player.ip"
                  :aria-label="t('players.actions.copyIp')"
                  color="neutral"
                  icon="i-lucide-copy"
                  size="xs"
                  square
                  variant="ghost"
                  @click="copy(player.ip)"
                />
              </dd>
            </div>
            <div class="details-item details-item--wide">
              <dt>{{ t('players.fields.compatibilityVersion') }}</dt><dd>{{ formatNullable(player.compatibilityVersion) }}</dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.permissionLevel') }}</dt><dd class="tabular-value">
                {{ player.permissionLevel }}
              </dd>
            </div>
          </dl>
        </section>

        <section class="details-section" :aria-labelledby="`status-${player.entityId}`">
          <h3 :id="`status-${player.entityId}`" class="details-heading">
            {{ t('players.details.currentStatus') }}
          </h3>
          <dl class="details-grid">
            <div class="details-item">
              <dt>{{ t('players.fields.state') }}</dt><dd>{{ player.isDead ? t('players.fields.dead') : t('players.fields.alive') }}</dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.level') }}</dt><dd class="tabular-value">
                {{ player.level }}
              </dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.health') }}</dt><dd class="tabular-value">
                {{ player.health }}
              </dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.maxHealth') }}</dt><dd class="tabular-value">
                {{ player.maxHealth }}
              </dd>
            </div>
            <div class="details-item details-item--wide">
              <dt>{{ t('players.fields.position') }}</dt><dd class="tabular-value">
                {{ formatPosition(player.position, displayLocale) }}
              </dd>
            </div>
            <div class="details-item details-item--wide">
              <dt>{{ t('players.fields.observedAt') }}</dt><dd>{{ d(new Date(player.observedAtUtc), 'playerObservation') }}</dd>
            </div>
          </dl>
        </section>

        <section class="details-section" :aria-labelledby="`statistics-${player.entityId}`">
          <h3 :id="`statistics-${player.entityId}`" class="details-heading">
            {{ t('players.details.statistics') }}
          </h3>
          <dl class="details-grid">
            <div class="details-item">
              <dt>{{ t('players.fields.score') }}</dt><dd class="tabular-value">
                {{ formatRoundedNumber(player.score, displayLocale) }}
              </dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.zombieKills') }}</dt><dd class="tabular-value">
                {{ formatRoundedNumber(player.zombieKills, displayLocale) }}
              </dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.playerKills') }}</dt><dd class="tabular-value">
                {{ formatRoundedNumber(player.playerKills, displayLocale) }}
              </dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.deaths') }}</dt><dd class="tabular-value">
                {{ formatRoundedNumber(player.deaths, displayLocale) }}
              </dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.totalTimePlayedMinutes') }}</dt><dd>{{ formatDurationMinutes(player.totalTimePlayedMinutes, displayLocale) }}</dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.distanceWalkedMeters') }}</dt><dd class="tabular-value">
                {{ formatRoundedNumber(player.distanceWalkedMeters, displayLocale) }}
              </dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.totalItemsCrafted') }}</dt><dd class="tabular-value">
                {{ formatRoundedNumber(player.totalItemsCrafted, displayLocale) }}
              </dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.longestLifeMinutes') }}</dt><dd>{{ formatDurationMinutes(player.longestLifeMinutes, displayLocale) }}</dd>
            </div>
            <div class="details-item">
              <dt>{{ t('players.fields.currentLifeMinutes') }}</dt><dd>{{ formatDurationMinutes(player.currentLifeMinutes, displayLocale) }}</dd>
            </div>
          </dl>
        </section>
      </div>
    </template>

    <template #footer>
      <UButton
        :label="t('common.cancel')"
        color="neutral"
        variant="outline"
        @click="open = false"
      />
      <UButton
        v-if="canKick && player && !unavailable"
        :label="t('players.actions.kick')"
        color="error"
        icon="i-lucide-log-out"
        @click="kick"
      />
    </template>
  </USlideover>
</template>

<style scoped>
.details-body {
  display: grid;
  gap: 1.5rem;
  min-width: 0;
}
.details-section {
  border-top: 1px solid var(--ui-border);
  padding-top: 1.25rem;
  min-width: 0;
}
.details-section:first-child {
  border-top: 0;
  padding-top: 0;
}
.details-heading {
  color: var(--ui-text-highlighted);
  font-size: 0.875rem;
  font-weight: 600;
  line-height: 1.25rem;
  margin: 0;
}
.details-grid {
  display: grid;
  gap: 1rem;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin-top: 1rem;
}
.details-item {
  min-width: 0;
}
.details-item--wide {
  grid-column: 1 / -1;
}
.details-item dt {
  color: var(--ui-text-dimmed);
  font-size: 0.75rem;
  line-height: 1rem;
}
.details-item dd {
  color: var(--ui-text-default);
  margin: 0.25rem 0 0;
  min-width: 0;
  overflow-wrap: anywhere;
}
.details-value-with-copy {
  align-items: flex-start;
  display: flex;
  gap: 0.25rem;
}
.details-value-with-copy > span {
  min-width: 0;
  overflow-wrap: anywhere;
}
.tabular-value {
  font-variant-numeric: tabular-nums;
}
@media (max-width: 359px) {
  .details-grid {
    grid-template-columns: minmax(0, 1fr);
  }
  .details-item--wide {
    grid-column: auto;
  }
}
</style>
