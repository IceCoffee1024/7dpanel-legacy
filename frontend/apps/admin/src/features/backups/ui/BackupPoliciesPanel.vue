<script setup lang="ts">
import type { BackupPolicyUpdate } from '../api/backupPolicies'
import type { BackupPoliciesController } from '../model/useBackupPolicies'

import { useI18n } from 'vue-i18n'

import BackupPolicyForm from './BackupPolicyForm.vue'

const props = defineProps<{ controller: BackupPoliciesController }>()
const { t } = useI18n()

function updateDraft(draft: BackupPolicyUpdate) {
  props.controller.updateDraft(draft)
}

function save(kind: BackupPolicyUpdate['kind']) {
  void props.controller.save(kind)
}

function errorFor(kind: BackupPolicyUpdate['kind']) {
  return props.controller.saveError.value?.kind === kind
    ? props.controller.saveError.value
    : null
}
</script>

<template>
  <section class="space-y-4" :aria-label="t('backups.policies.title')">
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 class="font-semibold">{{ t('backups.policies.title') }}</h2>
        <p class="text-sm text-muted">{{ t('backups.policies.description') }}</p>
      </div>
      <UButton
        color="neutral"
        icon="i-lucide-refresh-cw"
        :label="t('backups.policies.refresh')"
        variant="outline"
        :disabled="controller.isSaving.value"
        @click="controller.refresh"
      />
    </div>

    <UAlert v-if="controller.state.value === 'loading'" color="neutral" :title="t('backups.policies.state.loading')" />
    <UAlert v-else-if="controller.state.value === 'forbidden'" color="error" :title="t('backups.policies.state.forbidden')" />
    <UAlert v-else-if="controller.state.value === 'failed'" color="error" :title="t('backups.policies.state.failed')" />
    <UAlert v-else-if="controller.state.value === 'protocol-error'" color="error" :title="t('backups.policies.state.protocolError')" />
    <UAlert v-else-if="controller.state.value === 'stale'" color="warning" :title="t('backups.policies.state.stale')" />

    <div class="space-y-4">
      <BackupPolicyForm
        v-for="draft in controller.drafts.value"
        :key="draft.kind"
        :draft="draft"
        :disabled="controller.isSaving.value"
        :save-error="errorFor(draft.kind)"
        @update-draft="updateDraft"
        @save="save(draft.kind)"
      />
    </div>
  </section>
</template>
