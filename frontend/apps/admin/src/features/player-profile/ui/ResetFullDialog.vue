<script setup lang="ts">
import type { PlayerActionFeedback, PlayerActionTarget } from './playerProfileUi'

import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import PlayerActionDialogFrame from './PlayerActionDialogFrame.vue'

const props = defineProps<{ open: boolean, target: PlayerActionTarget | null, targetValid: boolean, pending: boolean, feedback: PlayerActionFeedback | null }>()
const emit = defineEmits<{ close: [], submit: [payload: { dangerConfirmed: true }] }>()
const { t } = useI18n()
const confirmed = shallowRef(false)
const confirmationText = shallowRef('')
const canSubmit = computed(() => confirmed.value && confirmationText.value === props.target?.crossplatformId)
watch(() => props.open, open => { if (open) { confirmed.value = false; confirmationText.value = '' } })
</script>

<template>
  <PlayerActionDialogFrame :open="open" :target="target" :target-valid="targetValid" :pending="pending" :feedback="feedback" :can-submit="canSubmit" :title="t('players.profile.actions.resetFull.title')" :description="t('players.profile.actions.resetFull.description')" :confirm-label="t('players.profile.actions.resetFull.confirm')" @close="emit('close')" @confirm="emit('submit', { dangerConfirmed: true })">
    <UAlert color="error" :title="t('players.profile.actions.resetFull.impact')" :description="t('players.profile.actions.resetFull.recoveryWarning')" />
    <UFormField :label="t('players.profile.actions.resetFull.typeIdentity')"><UInput v-model="confirmationText" class="w-full" :placeholder="target?.crossplatformId" /></UFormField>
    <UCheckbox v-model="confirmed" :label="t('players.profile.actions.resetFull.disposableConfirm')" />
  </PlayerActionDialogFrame>
</template>
