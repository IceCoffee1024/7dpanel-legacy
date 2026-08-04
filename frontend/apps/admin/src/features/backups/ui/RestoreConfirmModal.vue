<script setup lang="ts">
import type { BackupRecord } from '../model/useBackups'

import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{ backup: BackupRecord | null, disabled: boolean }>()
const emit = defineEmits<{ confirm: [restartAfterStage: boolean] }>()
const open = defineModel<boolean>('open', { required: true })
const { t } = useI18n()
const restartAfterStage = shallowRef(true)
const backupSize = computed(() => props.backup === null ? '—' : formatBytes(props.backup.sizeBytes))
const canConfirm = computed(() => props.backup?.validationStatus === 'Verified')

function formatBytes(value: number): string {
  if (value < 1024)
    return `${value} B`
  if (value < 1024 * 1024)
    return `${(value / 1024).toFixed(1)} KiB`
  if (value < 1024 * 1024 * 1024)
    return `${(value / 1024 / 1024).toFixed(1)} MiB`
  return `${(value / 1024 / 1024 / 1024).toFixed(1)} GiB`
}

function confirm() {
  if (!canConfirm.value)
    return
  emit('confirm', restartAfterStage.value)
}
</script>

<template>
  <UModal
    v-model:open="open"
    :description="t('backups.restore.description')"
    :title="t('backups.restore.title')"
    :ui="{ footer: 'justify-end' }"
  >
    <template #body>
      <div v-if="backup" class="space-y-4">
        <UAlert
          color="warning"
          icon="i-lucide-triangle-alert"
          :title="t('backups.restore.warning')"
          :description="t('backups.restore.nextStart')"
        />
        <dl class="grid gap-2 text-sm sm:grid-cols-[auto_1fr]">
          <dt class="text-muted">
            {{ t('backups.restore.backupId') }}
          </dt><dd class="break-all font-mono text-xs">
            {{ backup.id }}
          </dd>
          <dt class="text-muted">
            {{ t('backups.table.kind') }}
          </dt><dd>{{ t(`backups.kind.${backup.kind}`) }}</dd>
          <dt class="text-muted">
            {{ t('backups.restore.world') }}
          </dt><dd>{{ backup.worldId ?? '—' }}</dd>
          <dt class="text-muted">
            {{ t('backups.table.createdAt') }}
          </dt><dd>{{ new Date(backup.createdAtUtc).toLocaleString() }}</dd>
          <dt class="text-muted">
            {{ t('backups.restore.size') }}
          </dt><dd>{{ backupSize }}</dd>
          <dt class="text-muted">
            {{ t('backups.table.validation') }}
          </dt><dd>{{ backup.validationStatus }}</dd>
          <dt class="text-muted">
            SHA-256
          </dt><dd class="break-all font-mono text-xs">
            {{ backup.sha256 }}
          </dd>
        </dl>
        <UAlert v-if="backup.kind === 'PanelDatabase'" color="neutral" :title="t('backups.restore.panelDatabaseReceipt')" />
        <UCheckbox v-model="restartAfterStage" :label="t('backups.restore.restartAfterStage')" />
      </div>
    </template>
    <template #footer="{ close }">
      <UButton
        color="neutral"
        :disabled="disabled"
        :label="t('common.cancel')"
        variant="outline"
        @click="close"
      />
      <UButton
        color="error"
        :disabled="disabled || !canConfirm"
        :label="t('backups.restore.confirm')"
        @click="confirm"
      />
    </template>
  </UModal>
</template>
