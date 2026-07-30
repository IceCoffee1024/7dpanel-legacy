<script setup lang="ts">
import type { PlayerActionFeedback, PlayerActionTarget } from './playerProfileUi'

import { shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import PlayerActionDialogFrame from './PlayerActionDialogFrame.vue'

const props = defineProps<{ open: boolean, target: PlayerActionTarget | null, targetValid: boolean, pending: boolean, feedback: PlayerActionFeedback | null }>()
const emit = defineEmits<{ close: [], submit: [payload: { dangerConfirmed: true }] }>()
const { t } = useI18n()
const confirmed = shallowRef(false)
watch(() => props.open, (open) => {
  if (open)
    confirmed.value = false
})
</script>

<template>
  <PlayerActionDialogFrame
    :open="open"
    :target="target"
    :target-valid="targetValid"
    :pending="pending"
    :feedback="feedback"
    :can-submit="confirmed"
    :title="t('players.profile.actions.resetPartial.title')"
    :description="t('players.profile.actions.resetPartial.description')"
    :confirm-label="t('players.profile.actions.resetPartial.confirm')"
    @close="emit('close')"
    @confirm="emit('submit', { dangerConfirmed: true })"
  >
    <UAlert color="warning" :title="t('players.profile.actions.resetPartial.impact')" :description="t('players.profile.actions.resetPartial.scope')" />
    <UCheckbox v-model="confirmed" :label="t('players.profile.actions.dangerConfirm')" />
  </PlayerActionDialogFrame>
</template>
