<script setup lang="ts">
import type { RestartServerErrorCode, RestartServerState } from '../model/useRestartServer'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ state: RestartServerState, errorCode: RestartServerErrorCode | null }>()
const emit = defineEmits<{ cancel: [], confirm: [] }>()
const { t } = useI18n()
const open = computed(() => props.state === 'confirming' || props.state === 'submitting')
function updateOpen(value: boolean) {
  if (!value && props.state === 'confirming') emit('cancel')
}
</script>

<template>
  <UModal :open="open" :portal="false" :dismissible="state !== 'submitting'" :title="t('overview.restartDialog.title')" :description="t('overview.restartDialog.description')" @update:open="updateOpen">
    <template #body><div data-testid="restart-dialog"><UAlert color="warning" variant="subtle" :description="t('overview.restartDialog.risk')" /></div></template>
    <template #footer>
      <div class="flex justify-end gap-2"><UButton data-testid="restart-cancel" color="neutral" variant="outline" :disabled="state === 'submitting'" :label="t('common.cancel')" @click="emit('cancel')" /><UButton data-testid="restart-confirm" color="warning" icon="i-lucide-rotate-ccw" :loading="state === 'submitting'" :disabled="state === 'submitting'" :label="t('overview.restartDialog.confirm')" @click="emit('confirm')" /></div>
    </template>
  </UModal>
  <UAlert v-if="state === 'accepted'" color="success" variant="subtle" :title="t('overview.restartDialog.accepted')" />
  <UAlert v-else-if="state === 'failed'" color="error" variant="subtle" :title="t('overview.restartDialog.failed')" />
</template>
