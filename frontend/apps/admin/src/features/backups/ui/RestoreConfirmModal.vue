<script setup lang="ts">
import type { BackupRecord } from '../model/useBackups'

import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

defineProps<{ backup: BackupRecord | null, disabled: boolean }>()
const emit = defineEmits<{ confirm: [restartAfterStage: boolean] }>()
const open = defineModel<boolean>('open', { required: true })
const { t } = useI18n()
const restartAfterStage = shallowRef(true)

function confirm() {
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
            {{ t('backups.table.kind') }}
          </dt><dd>{{ t(`backups.kind.${backup.kind}`) }}</dd>
          <dt class="text-muted">
            {{ t('backups.table.createdAt') }}
          </dt><dd>{{ new Date(backup.createdAtUtc).toLocaleString() }}</dd>
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
        :disabled="disabled || backup === null"
        :label="t('backups.restore.confirm')"
        @click="confirm"
      />
    </template>
  </UModal>
</template>
