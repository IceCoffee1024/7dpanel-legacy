<script setup lang="ts">
import type { PlayerActionFeedback, PlayerActionTarget } from './playerProfileUi'

import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import PlayerActionDialogFrame from './PlayerActionDialogFrame.vue'

const props = defineProps<{ open: boolean, target: PlayerActionTarget | null, targetValid: boolean, pending: boolean, feedback: PlayerActionFeedback | null, catalogVersion: string | null }>()
const emit = defineEmits<{ close: [], submit: [payload: { resourceId: string, quantity: number, quality: number | null, removalScope: 'BagOnly', removalMode: 'Exact' | 'UpToAvailable', catalogVersion: string }] }>()
const { t } = useI18n()
const resourceId = shallowRef('')
const quantity = shallowRef(1)
const quality = shallowRef<number | null>(null)
const removalMode = shallowRef<'Exact' | 'UpToAvailable'>('Exact')
const canSubmit = computed(() => resourceId.value.trim() !== '' && quantity.value > 0 && props.catalogVersion !== null)
watch(() => props.open, (open) => { if (open) { resourceId.value = ''; quantity.value = 1; quality.value = null; removalMode.value = 'Exact' } })
function submit() { if (canSubmit.value && props.catalogVersion) emit('submit', { resourceId: resourceId.value.trim(), quantity: quantity.value, quality: quality.value, removalScope: 'BagOnly', removalMode: removalMode.value, catalogVersion: props.catalogVersion }) }
</script>

<template>
  <PlayerActionDialogFrame :open="open" :target="target" :target-valid="targetValid" :pending="pending" :feedback="feedback" :can-submit="canSubmit" :title="t('players.profile.actions.remove.title')" :description="t('players.profile.actions.remove.description')" :confirm-label="t('players.profile.actions.remove.confirm')" @close="emit('close')" @confirm="submit">
    <UForm :state="{ resourceId, quantity, quality, removalMode }" class="space-y-3">
      <UFormField :label="t('players.profile.actions.resourceId')"><UInput v-model="resourceId" class="w-full" /></UFormField>
      <div class="grid gap-3 sm:grid-cols-2"><UFormField :label="t('players.profile.inventory.quantity')"><UInputNumber v-model="quantity" :min="1" /></UFormField><UFormField :label="t('players.profile.inventory.quality')"><UInputNumber v-model="quality" :min="0" /></UFormField></div>
      <UFormField :label="t('players.profile.actions.remove.mode')"><USelect v-model="removalMode" :items="[{ label: t('players.profile.actions.remove.exact'), value: 'Exact' }, { label: t('players.profile.actions.remove.upToAvailable'), value: 'UpToAvailable' }]" /></UFormField>
      <UAlert color="warning" :title="t('players.profile.actions.remove.bagOnly')" />
      <p class="text-xs text-muted">{{ t('players.profile.actions.catalogVersion', { version: catalogVersion ?? '—' }) }}</p>
    </UForm>
  </PlayerActionDialogFrame>
</template>
