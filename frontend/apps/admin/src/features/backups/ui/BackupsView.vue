<script setup lang="ts">
import type { BackupPoliciesController } from '../model/useBackupPolicies'
import type { BackupKind, BackupRecord, BackupsController } from '../model/useBackups'

import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

import BackupCatalogTable from './BackupCatalogTable.vue'
import BackupPoliciesPanel from './BackupPoliciesPanel.vue'
import CreateBackupCard from './CreateBackupCard.vue'
import JobProgressPanel from './JobProgressPanel.vue'
import RestoreConfirmModal from './RestoreConfirmModal.vue'

const props = defineProps<{
  controller: BackupsController
  policyController: BackupPoliciesController
}>()
const { t } = useI18n()
const restoreTarget = shallowRef<BackupRecord | null>(null)
const restoreOpen = shallowRef(false)
const deleteTarget = shallowRef<BackupRecord | null>(null)

function create(kind: BackupKind, worldName: string) {
  void props.controller.create(kind, worldName)
}

function requestRestore(backup: BackupRecord) {
  restoreTarget.value = backup
  restoreOpen.value = true
}

async function confirmRestore(restartAfterStage: boolean) {
  const backup = restoreTarget.value
  if (backup !== null && await props.controller.restore(backup, restartAfterStage)) {
    restoreOpen.value = false
    restoreTarget.value = null
  }
}

function requestDelete(backup: BackupRecord) {
  deleteTarget.value = backup
}

function confirmDelete() {
  const backup = deleteTarget.value
  if (backup === null)
    return
  deleteTarget.value = null
  void props.controller.remove(backup)
}

function refresh() {
  void props.controller.refresh()
  void props.policyController.refresh()
}
</script>

<template>
  <UDashboardPanel id="backups">
    <template #header>
      <UDashboardNavbar :title="t('backups.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            color="neutral"
            :disabled="controller.isMutating.value || policyController.isSaving.value"
            icon="i-lucide-refresh-cw"
            :label="t('common.reload')"
            variant="outline"
            @click="refresh"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UContainer class="space-y-5 py-5">
        <UAlert v-if="controller.state.value === 'forbidden'" color="error" :title="t('backups.state.forbidden')" />
        <UAlert v-else-if="controller.state.value === 'failed'" color="error" :title="t('backups.state.failed')" />
        <UAlert v-else-if="controller.state.value === 'protocol-error'" color="error" :title="t('backups.state.protocolError')" />
        <UAlert v-else-if="controller.state.value === 'stale'" color="warning" :title="t('backups.state.stale')" />
        <UAlert
          v-if="controller.errorCode.value"
          color="error"
          :description="controller.errorCode.value"
          :title="t('backups.state.operationFailed')"
        />

        <BackupPoliciesPanel :controller="policyController" />
        <CreateBackupCard :disabled="controller.isMutating.value" @create="create" />
        <JobProgressPanel v-if="controller.activeJob.value" :job="controller.activeJob.value" />
        <BackupCatalogTable
          :backups="controller.backups.value"
          :disabled="controller.isMutating.value"
          @download="controller.download"
          @remove="requestDelete"
          @restore="requestRestore"
        />
      </UContainer>
    </template>
  </UDashboardPanel>

  <RestoreConfirmModal
    v-model:open="restoreOpen"
    :backup="restoreTarget"
    :disabled="controller.isMutating.value"
    @confirm="confirmRestore"
  />
  <UModal
    :open="deleteTarget !== null"
    :title="t('common.confirm')"
    :description="deleteTarget === null ? '' : t('backups.delete.confirm', { kind: t(`backups.kind.${deleteTarget.kind}`) })"
    :ui="{ footer: 'justify-end' }"
    @update:open="open => { if (!open) deleteTarget = null }"
  >
    <template #footer>
      <UButton
        color="neutral"
        :label="t('common.cancel')"
        variant="outline"
        @click="deleteTarget = null"
      />
      <UButton color="error" :label="t('common.confirm')" @click="confirmDelete" />
    </template>
  </UModal>
</template>
