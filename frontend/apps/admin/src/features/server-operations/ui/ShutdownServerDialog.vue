<script setup lang="ts">
import type { ShutdownServerErrorCode, ShutdownServerState } from '../model/useShutdownServer'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ state: ShutdownServerState, errorCode: ShutdownServerErrorCode | null }>()
const emit = defineEmits<{ cancel: [], confirm: [] }>()
const { t } = useI18n()
const open = computed(() => props.state === 'confirming' || props.state === 'submitting')
function updateOpen(value: boolean) {
  if (!value && props.state === 'confirming') emit('cancel')
}
</script>

<template>
  <UModal :open="open" :portal="false" :dismissible="state !== 'submitting'" :title="t('overview.shutdownDialog.title')" :description="t('overview.shutdownDialog.description')" @update:open="updateOpen">
    <template #body><div data-testid="shutdown-dialog"><UAlert color="error" variant="subtle" :description="t('overview.shutdownDialog.risk')" /></div></template>
    <template #footer>
      <div class="flex justify-end gap-2"><UButton data-testid="shutdown-cancel" color="neutral" variant="outline" :disabled="state === 'submitting'" :label="t('common.cancel')" @click="emit('cancel')" /><UButton data-testid="shutdown-confirm" color="error" icon="i-lucide-power" :loading="state === 'submitting'" :disabled="state === 'submitting'" :label="t('overview.shutdownDialog.confirm')" @click="emit('confirm')" /></div>
    </template>
  </UModal>
  <UAlert v-if="state === 'accepted'" color="success" variant="subtle" :title="t('overview.shutdownDialog.accepted')" />
  <UAlert v-else-if="state === 'failed'" color="error" variant="subtle" :title="t('overview.shutdownDialog.failed')" />
</template>
