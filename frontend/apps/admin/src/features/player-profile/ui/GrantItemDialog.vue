<script setup lang="ts">
import type { PlayerActionFeedback, PlayerActionTarget } from './playerProfileUi'

import { computed, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import PlayerActionDialogFrame from './PlayerActionDialogFrame.vue'

const props = defineProps<{ open: boolean, target: PlayerActionTarget | null, targetValid: boolean, pending: boolean, feedback: PlayerActionFeedback | null, catalogVersion: string | null }>()
const emit = defineEmits<{ close: [], submit: [payload: { resourceId: string, quantity: number, quality: number | null, hiddenItemConfirmed: boolean, catalogVersion: string }] }>()
const { t } = useI18n()
const resourceId = shallowRef('')
const quantity = shallowRef(1)
const quality = shallowRef<number | null>(null)
const hiddenItemConfirmed = shallowRef(false)
const canSubmit = computed(() => resourceId.value.trim() !== '' && quantity.value > 0 && props.catalogVersion !== null)
watch(() => props.open, (open) => {
  if (!open)
    return
  resourceId.value = ''
  quantity.value = 1
  quality.value = null
  hiddenItemConfirmed.value = false
})
function submit() {
  if (canSubmit.value && props.catalogVersion)
    emit('submit', { resourceId: resourceId.value.trim(), quantity: quantity.value, quality: quality.value, hiddenItemConfirmed: hiddenItemConfirmed.value, catalogVersion: props.catalogVersion })
}
</script>

<template>
  <PlayerActionDialogFrame
    :open="open"
    :target="target"
    :target-valid="targetValid"
    :pending="pending"
    :feedback="feedback"
    :can-submit="canSubmit"
    :title="t('players.profile.actions.grant.title')"
    :description="t('players.profile.actions.grant.description')"
    :confirm-label="t('players.profile.actions.grant.confirm')"
    @close="emit('close')"
    @confirm="submit"
  >
    <UAlert v-if="!catalogVersion" color="warning" :title="t('players.profile.inventory.catalogUnavailable')" />
    <UForm :state="{ resourceId, quantity, quality, hiddenItemConfirmed }" class="space-y-3">
      <UFormField :label="t('players.profile.actions.resourceId')">
        <UInput v-model="resourceId" class="w-full" />
      </UFormField>
      <div class="grid gap-3 sm:grid-cols-2">
        <UFormField :label="t('players.profile.inventory.quantity')">
          <UInputNumber v-model="quantity" :min="1" />
        </UFormField><UFormField :label="t('players.profile.inventory.quality')">
          <UInputNumber v-model="quality" :min="0" />
        </UFormField>
      </div>
      <UCheckbox v-model="hiddenItemConfirmed" :label="t('players.profile.actions.grant.hiddenConfirm')" />
      <p class="text-xs text-muted">
        {{ t('players.profile.actions.catalogVersion', { version: catalogVersion ?? '—' }) }}
      </p>
    </UForm>
  </PlayerActionDialogFrame>
</template>
