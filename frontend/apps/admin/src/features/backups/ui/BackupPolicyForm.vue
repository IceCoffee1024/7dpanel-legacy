<script setup lang="ts">
import type { BackupPolicyUpdate } from '../api/backupPolicies'
import type { BackupPolicySaveError } from '../model/useBackupPolicies'

import { reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  draft: BackupPolicyUpdate
  disabled: boolean
  saveError: BackupPolicySaveError | null
}>()

const emit = defineEmits<{
  updateDraft: [draft: BackupPolicyUpdate]
  save: []
}>()
const { t } = useI18n()

const state = reactive<BackupPolicyUpdate>({ ...props.draft })

watch(() => props.draft, (draft) => {
  Object.assign(state, draft)
})

watch(state, () => {
  emit('updateDraft', Object.freeze({ ...state }))
}, { deep: true, flush: 'sync' })

function validate(): { name: string, message: string }[] {
  const errors: { name: string, message: string }[] = []
  if (state.cronExpression.trim() === '')
    errors.push({ name: 'cronExpression', message: t('backups.policies.field.cronExpression') })
  if (state.timeZoneId.trim() === '')
    errors.push({ name: 'timeZoneId', message: t('backups.policies.field.timeZoneId') })
  if (state.backupRootId.trim() === '')
    errors.push({ name: 'backupRootId', message: t('backups.policies.field.backupRootId') })
  if (!Number.isSafeInteger(state.retentionCount) || state.retentionCount < 0)
    errors.push({ name: 'retentionCount', message: t('backups.policies.field.retentionCount') })
  if (!Number.isSafeInteger(state.retentionDays) || state.retentionDays < 0)
    errors.push({ name: 'retentionDays', message: t('backups.policies.field.retentionDays') })
  return errors
}

function submit() {
  emit('updateDraft', Object.freeze({ ...state }))
  emit('save')
}
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex flex-wrap items-center justify-between gap-2">
        <h3 class="font-semibold">
          {{ t(`backups.kind.${draft.kind}`) }}
        </h3>
        <span class="text-xs text-muted">{{ t('backups.policies.version', { version: state.rowVersion }) }}</span>
      </div>
    </template>

    <UForm
      :state="state"
      :validate="validate"
      class="space-y-4"
      @submit="submit"
    >
      <UAlert
        v-if="saveError?.code === 'conflict'"
        color="warning"
        :title="t('backups.policies.error.conflictTitle')"
        :description="t('backups.policies.error.conflictDescription')"
      />
      <UAlert
        v-else-if="saveError?.code === 'invalid'"
        color="error"
        :title="t('backups.policies.error.invalidTitle')"
        :description="t('backups.policies.error.invalidDescription')"
      />
      <UAlert
        v-else-if="saveError?.code === 'unavailable'"
        color="error"
        :title="t('backups.policies.error.unavailableTitle')"
        :description="t('backups.policies.error.unavailableDescription')"
      />

      <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <UFormField name="enabled" :label="t('backups.policies.field.enabled')">
          <USwitch v-model="state.enabled" :disabled="disabled" />
        </UFormField>
        <UFormField name="cronExpression" :label="t('backups.policies.field.cronExpression')" required>
          <UInput v-model="state.cronExpression" class="w-full" :disabled="disabled" />
        </UFormField>
        <UFormField name="timeZoneId" :label="t('backups.policies.field.timeZoneId')" required>
          <UInput v-model="state.timeZoneId" class="w-full" :disabled="disabled" />
        </UFormField>
        <UFormField name="backupRootId" :label="t('backups.policies.field.backupRootId')" required>
          <UInput v-model="state.backupRootId" class="w-full" :disabled="disabled" />
        </UFormField>
        <UFormField name="retentionCount" :label="t('backups.policies.field.retentionCount')" required>
          <UInputNumber
            v-model="state.retentionCount"
            class="w-full"
            :disabled="disabled"
            :min="0"
          />
        </UFormField>
        <UFormField name="retentionDays" :label="t('backups.policies.field.retentionDays')" required>
          <UInputNumber
            v-model="state.retentionDays"
            class="w-full"
            :disabled="disabled"
            :min="0"
          />
        </UFormField>
        <UFormField name="compressionEnabled" :label="t('backups.policies.field.compressionEnabled')">
          <USwitch v-model="state.compressionEnabled" :disabled="disabled" />
        </UFormField>
      </div>

      <div class="flex justify-end">
        <UButton
          type="submit"
          :label="t('backups.policies.save')"
          :disabled="disabled"
          :loading="disabled"
        />
      </div>
    </UForm>
  </UCard>
</template>
