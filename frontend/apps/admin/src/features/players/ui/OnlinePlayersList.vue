<script setup lang="ts">
import type { DropdownMenuItem } from '@nuxt/ui'
import type { OnlinePlayer } from '../api/onlinePlayers'

withDefaults(defineProps<{
  players: readonly OnlinePlayer[]
  canKick?: boolean
}>(), {
  canKick: true,
})

const emit = defineEmits<{
  copyIdentity: [combinedId: string]
  kickPlayer: [player: OnlinePlayer]
}>()

function playerActions(player: OnlinePlayer): DropdownMenuItem[] {
  return [{
    label: '踢出玩家',
    icon: 'i-lucide-log-out',
    onSelect: () => emit('kickPlayer', player),
  }]
}
</script>

<template>
  <ul class="divide-y divide-default md:hidden">
    <li v-for="player in players" :key="player.entityId" class="py-5 first:pt-0 last:pb-0">
      <div class="flex min-w-0 items-start justify-between gap-3">
        <div class="min-w-0">
          <h2 class="wrap-break-word text-sm font-semibold text-highlighted">
            {{ player.name }}
          </h2>
          <p class="mt-1 font-mono text-xs text-dimmed">
            entity {{ player.entityId }} · {{ player.ping }} ms
          </p>
        </div>
        <div class="flex shrink-0 items-center gap-1">
          <UBadge color="neutral" variant="subtle">
            Lv. {{ player.level }}
          </UBadge>
          <UDropdownMenu v-if="canKick" :items="playerActions(player)">
            <UButton
              :aria-label="`玩家操作：${player.name}`"
              class="size-8"
              color="neutral"
              icon="i-lucide-ellipsis-vertical"
              square
              variant="ghost"
            />
          </UDropdownMenu>
        </div>
      </div>

      <dl class="player-details mt-4 grid gap-x-5 gap-y-4">
        <div>
          <dt>平台身份</dt>
          <dd>
            <span class="block text-xs text-muted">{{ player.platformIdentity.platform }}</span>
            <span class="identity-row">
              <code>{{ player.platformIdentity.combinedId }}</code>
              <UButton
                :aria-label="`复制 ${player.platformIdentity.platform} 身份`"
                color="neutral"
                :data-testid="`copy-platform-identity-list-${player.entityId}`"
                icon="i-lucide-copy"
                size="xs"
                square
                variant="ghost"
                @click="$emit('copyIdentity', player.platformIdentity.combinedId)"
              />
            </span>
          </dd>
        </div>

        <div>
          <dt>跨平台身份</dt>
          <dd v-if="player.crossplatformIdentity">
            <span class="block text-xs text-muted">{{ player.crossplatformIdentity.platform }}</span>
            <span class="identity-row">
              <code>{{ player.crossplatformIdentity.combinedId }}</code>
              <UButton
                :aria-label="`复制 ${player.crossplatformIdentity.platform} 身份`"
                color="neutral"
                icon="i-lucide-copy"
                size="xs"
                square
                variant="ghost"
                @click="$emit('copyIdentity', player.crossplatformIdentity.combinedId)"
              />
            </span>
          </dd>
          <dd v-else class="text-dimmed">
            未绑定
          </dd>
        </div>

        <div>
          <dt>等级</dt>
          <dd class="font-mono tabular-nums">
            {{ player.level }}
          </dd>
        </div>
        <div>
          <dt>生命值</dt>
          <dd class="font-mono tabular-nums">
            {{ player.health }}
          </dd>
        </div>
        <div>
          <dt>延迟</dt>
          <dd class="font-mono tabular-nums">
            {{ player.ping }} ms
          </dd>
        </div>
      </dl>
    </li>
  </ul>
</template>

<style scoped>
.player-details {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.player-details dt {
  color: var(--ui-text-dimmed);
  font-size: 0.75rem;
  line-height: 1rem;
}

.player-details dd {
  min-width: 0;
  margin-top: 0.25rem;
  color: var(--ui-text-highlighted);
  font-size: 0.875rem;
  line-height: 1.25rem;
}

.identity-row {
  display: flex;
  min-width: 0;
  align-items: flex-start;
  gap: 0.25rem;
  margin-top: 0.25rem;
}

.identity-row code {
  min-width: 0;
  overflow-wrap: anywhere;
  font-size: 0.75rem;
  line-height: 1rem;
}

@media (max-width: 359px) {
  .player-details {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
