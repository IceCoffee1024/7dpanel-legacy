<script setup lang="ts">
import type { PlayerActionFeedback, PlayerActionTarget } from './playerProfileUi'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  open: boolean
  title: string
  description: string
  confirmLabel: string
  target: PlayerActionTarget | null
  targetValid: boolean
  pending: boolean
  feedback: PlayerActionFeedback | null
  canSubmit: boolean
}>()
const emit = defineEmits<{ close: [], confirm: [] }>()
const { t } = useI18n()
const statusColor = computed(() => props.feedback?.status === 'Succeeded'
  ? 'success'
  : props.feedback?.status === 'ResultUnknown' ? 'warning' : 'error')

function updateOpen(open: boolean) {
  if (!open && !props.pending)
    emit('close')
}
</script>

<template>
  <UModal
    :open="open"
    :title="title"
    :description="description"
    :dismissible="!pending"
    :close="!pending"
    :ui="{ content: 'w-full max-w-xl', footer: 'justify-end' }"
    @update:open="updateOpen"
  >
    <template #body>
      <div v-if="target" class="space-y-4">
        <dl class="grid gap-2 rounded-lg border border-default p-3 text-sm sm:grid-cols-2">
          <div><dt class="text-muted">{{ t('players.fields.player') }}</dt><dd>{{ target.name ?? target.crossplatformId }}</dd></div>
          <div><dt class="text-muted">{{ t('players.fields.entityId') }}</dt><dd>{{ target.entityId }}</dd></div>
          <div><dt class="text-muted">{{ t('players.profile.actions.world') }}</dt><dd>{{ target.worldId }}</dd></div>
          <div><dt class="text-muted">{{ t('players.fields.observedAt') }}</dt><dd>{{ target.onlineObservedAtUtc }}</dd></div>
        </dl>
        <UAlert
          v-if="!targetValid"
          color="warning"
          :title="t('players.profile.actions.targetInvalidTitle')"
          :description="t('players.profile.actions.targetInvalidDescription')"
        />
        <slot />
        <UAlert
          v-if="feedback"
          :color="statusColor"
          :title="t(`players.profile.actions.status.${feedback.status.charAt(0).toLowerCase()}${feedback.status.slice(1)}`)"
          :description="feedback.status === 'ResultUnknown' ? t('players.profile.actions.resultUnknownDescription', { operationId: feedback.operationId ?? '—' }) : (feedback.failureCode ?? undefined)"
        />
      </div>
    </template>
    <template #footer>
      <UButton color="neutral" variant="outline" :disabled="pending" :label="t('common.cancel')" @click="emit('close')" />
      <UButton color="error" :loading="pending" :disabled="!targetValid || !canSubmit || pending" :label="confirmLabel" @click="emit('confirm')" />
    </template>
  </UModal>
</template>
