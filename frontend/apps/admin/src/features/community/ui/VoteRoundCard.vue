<script setup lang="ts">
import type { VoteRound } from '../api/community'

import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

const props = withDefaults(defineProps<{
  round: VoteRound
  allowSettle?: boolean
  settling?: boolean
}>(), {
  allowSettle: false,
  settling: false,
})

const emit = defineEmits<{ settle: [roundId: string] }>()
const { t } = useI18n()
const confirming = shallowRef(false)

const badgeColor = computed(() => {
  if (props.round.state === 'ActionSucceeded')
    return 'success' as const
  if (props.round.state === 'ActionFailed' || props.round.state === 'Rejected' || props.round.state === 'Cancelled')
    return 'error' as const
  if (props.round.state === 'ActionResultUnknown')
    return 'warning' as const
  return 'neutral' as const
})

function confirmSettlement() {
  confirming.value = false
  emit('settle', props.round.roundId)
}
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex min-w-0 flex-wrap items-start justify-between gap-2">
        <div class="min-w-0">
          <h3 class="break-all font-semibold text-highlighted">{{ round.roundId }}</h3>
          <p class="text-xs text-muted">{{ round.kind }} · {{ round.scopeKey }}</p>
        </div>
        <UBadge :color="badgeColor" variant="subtle">{{ round.state === 'ActionResultUnknown' ? t('community.voteRound.actionResultUnknown') : round.state }}</UBadge>
      </div>
    </template>

    <UAlert
      v-if="round.state === 'ActionResultUnknown'"
      class="mb-4"
      color="warning"
      icon="i-lucide-triangle-alert"
      :title="t('community.voteRound.actionResultUnknown')"
      :description="t('community.voteRound.actionResultUnknownDescription')"
    />

    <dl class="grid gap-3 text-sm sm:grid-cols-2 xl:grid-cols-3">
      <div><dt class="text-muted">{{ t('community.voteRound.initiator') }}</dt><dd class="mt-1 break-all">{{ round.initiatorCrossplatformId }}</dd></div>
      <div><dt class="text-muted">{{ t('community.voteRound.target') }}</dt><dd class="mt-1 break-all">{{ round.targetCrossplatformId ?? t('community.voteRound.noFixedTarget') }}</dd></div>
      <div><dt class="text-muted">{{ t('community.voteRound.eligibleCount') }}</dt><dd class="mt-1">{{ round.eligibleCount }}</dd></div>
      <div><dt class="text-muted">{{ t('community.voteRound.threshold') }}</dt><dd class="mt-1">{{ t('community.voteRound.thresholdValue', { percent: round.thresholdPercent, count: round.minimumParticipants }) }}</dd></div>
      <div><dt class="text-muted">{{ t('community.voteRound.openedAt') }}</dt><dd class="mt-1 break-all">{{ round.openedAtUtc }}</dd></div>
      <div><dt class="text-muted">{{ t('community.voteRound.expiresAt') }}</dt><dd class="mt-1 break-all">{{ round.expiresAtUtc }}</dd></div>
    </dl>

    <template v-if="allowSettle" #footer>
      <div v-if="confirming" class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p class="text-sm text-warning">{{ t('community.voteRound.settlementConfirmation') }}</p>
        <div class="flex justify-end gap-2">
          <UButton color="neutral" :label="t('community.common.cancel')" variant="outline" :disabled="settling" @click="confirming = false" />
          <UButton color="warning" :label="t('community.voteRound.confirmSettlement')" :loading="settling" @click="confirmSettlement" />
        </div>
      </div>
      <div v-else class="flex justify-end">
        <UButton color="warning" :label="t('community.voteRound.requestSettlement')" variant="outline" :disabled="settling" @click="confirming = true" />
      </div>
    </template>
  </UCard>
</template>
